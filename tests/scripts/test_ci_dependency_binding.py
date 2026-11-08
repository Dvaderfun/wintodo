"""Exercise the real audit wrapper with isolated files and a synthetic NuGet command."""
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest


REPOSITORY = Path(__file__).parents[2]
COMMIT = "1" * 40
MARKER = "SYNTHETIC-OWNER's private"
FRAMEWORK = "net10.0-windows10.0.26100.0"


def quote(value):
    return "'" + str(value).replace("'", "''") + "'"


class CiDependencyBindingTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.pwsh = shutil.which("pwsh")
        if not cls.pwsh:
            raise RuntimeError("PowerShell is required for dependency binding regressions")
        cls.scratch = REPOSITORY / "artifacts" / "dep-audit-binding" / "tests"
        cls.scratch.mkdir(parents=True, exist_ok=True)

    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix=MARKER, dir=self.scratch)
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        scripts = self.root / "scripts"
        scripts.mkdir()
        for name in ("Invoke-CiDependencyAudit.ps1", "Test-CiSecurity.py"):
            shutil.copyfile(REPOSITORY / "scripts" / name, scripts / name)
        app = self.root / "src" / "Wintodo.App"
        (app / "obj").mkdir(parents=True)
        (app / "Wintodo.csproj").write_text("<Project />")
        self.assets = app / "obj" / "project.assets.json"
        self.graph = {"targets": {FRAMEWORK: {}, FRAMEWORK + "/win-x64": {}},
                      "project": {"frameworks": {FRAMEWORK: {}}, "restore": {
                          "projectPath": str(app / "Wintodo.csproj"), "originalTargetFrameworks": [FRAMEWORK]}}}
        self.write_graph()
        self.raw = self.root / "synthetic-nuget.json"
        self.raw.write_text(json.dumps({"version": 1, "parameters": "--vulnerable --include-transitive",
                                       "projects": [{"path": str(app / "Wintodo.csproj")}]}, ensure_ascii=False))
        self.summary = self.root / "SafeTestResults" / "dependencies.json"
        self.summary.parent.mkdir()
        self.summary.write_text('{"status":"PASS","stale":true}')

    def write_graph(self):
        self.assets.write_text(json.dumps(self.graph), encoding="utf-8")

    def run_audit(self, arguments="-AppOnly -Architecture x64", mutation="", git_result=COMMIT):
        command = f"""$ErrorActionPreference = 'Stop'
function git {{ $global:LASTEXITCODE = 0; return {quote(git_result)} }}
function dotnet {{
    {mutation}
    $global:LASTEXITCODE = 0
    [IO.File]::ReadAllText({quote(self.raw)})
}}
& {quote(self.root / 'scripts' / 'Invoke-CiDependencyAudit.ps1')} {arguments}
"""
        result = subprocess.run([self.pwsh, "-NoProfile", "-NonInteractive", "-Command", command],
                                capture_output=True, text=True, timeout=30)
        self.assertNotIn(MARKER, result.stdout + result.stderr)
        self.assertNotIn(str(self.root), result.stdout + result.stderr)
        return result

    def assert_failure(self, **kwargs):
        result = self.run_audit(**kwargs)
        self.assertEqual(1, result.returncode, result.stdout + result.stderr)
        self.assertFalse(self.summary.exists(), "A rejected audit retained public PASS evidence")

    def test_bound_audit_records_exact_graph_source_and_architecture_without_paths(self):
        digest = hashlib.sha256(self.assets.read_bytes()).hexdigest()
        original = self.assets.read_bytes()
        result = self.run_audit()
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        summary = json.loads(self.summary.read_text(encoding="utf-8-sig"))
        self.assertEqual("PASS", summary["status"])
        self.assertEqual({"source_commit": COMMIT, "architecture": "x64", "assets_sha256": digest,
                          "project_set": "app", "scope": "app_assets_observed_before_and_after_audit"}, summary["binding"])
        self.assertEqual(original, self.assets.read_bytes())
        self.assertNotIn(MARKER, json.dumps(summary))

    def test_arm64_graph_and_target_are_supported(self):
        self.graph["targets"] = {FRAMEWORK: {}, FRAMEWORK + "/win-arm64": {}}
        self.write_graph()
        result = self.run_audit(arguments="-AppOnly -Architecture arm64")
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        self.assertEqual("arm64", json.loads(self.summary.read_text(encoding="utf-8-sig"))["binding"]["architecture"])

    def test_target_and_framework_mismatch_are_rejected(self):
        for targets in ({FRAMEWORK: {}, FRAMEWORK + "/win-arm64": {}},
                        {FRAMEWORK: {}},
                        {FRAMEWORK: {}, "net9.0-windows10.0.26100.0/win-x64": {}},
                        {FRAMEWORK: {}, FRAMEWORK + "/win-x64": {}, FRAMEWORK + "/win-arm64": {}}):
            self.graph["targets"] = targets
            self.write_graph()
            self.assert_failure()

    def test_wrong_project_or_original_framework_rejected(self):
        for field, value in (("projectPath", str(self.root / "private-other.csproj")),
                             ("originalTargetFrameworks", ["net9.0"]), ("originalTargetFrameworks", [])):
            self.graph["project"]["restore"][field] = value
            self.write_graph()
            self.assert_failure()
            self.graph["project"]["restore"] = {"projectPath": str(self.assets.parent.parent / "Wintodo.csproj"),
                                                "originalTargetFrameworks": [FRAMEWORK]}

    def test_missing_corrupt_and_oversized_assets_fail_without_stale_pass(self):
        self.assets.unlink()
        self.assert_failure()
        self.assets.write_text(MARKER)
        self.assert_failure()
        self.assets.write_bytes(b"x" * (16 * 1024 * 1024 + 1))
        self.assert_failure()

    def test_invalid_source_identity_fails_without_echo(self):
        for value in ("0" * 40, "1234567", MARKER):
            self.assert_failure(git_result=value)

    def test_invalid_utf8_assets_fail_without_stale_pass(self):
        self.assets.write_bytes(self.assets.read_bytes() + b'\xff')
        self.assert_failure()

    def test_graph_mutation_during_audit_fails(self):
        self.assert_failure(mutation=f"[IO.File]::AppendAllText({quote(self.assets)}, ' ')")

    def test_source_mutation_during_audit_fails(self):
        self.assert_failure(mutation="function global:git { $global:LASTEXITCODE = 0; return ('2' * 40) }")

    def test_solution_scope_cannot_acquire_app_binding(self):
        self.assert_failure(arguments="-Architecture x64")

    def test_legacy_app_audit_remains_supported_without_exact_binding(self):
        self.assets.unlink()
        result = self.run_audit(arguments="-AppOnly")
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        summary = json.loads(self.summary.read_text(encoding="utf-8-sig"))
        self.assertEqual("PASS", summary["status"])
        self.assertNotIn("binding", summary)

    def test_legacy_solution_audit_remains_supported_without_exact_binding(self):
        names = ("Wintodo.csproj", "Wintodo.Core.csproj", "Wintodo.Data.csproj", "Wintodo.Sync.csproj",
                 "Wintodo.Import.csproj", "Wintodo.Core.Tests.csproj", "Wintodo.Data.Tests.csproj",
                 "Wintodo.Sync.Tests.csproj", "Wintodo.Import.Tests.csproj")
        self.raw.write_text(json.dumps({"version": 1, "parameters": "--vulnerable --include-transitive",
                                       "projects": [{"path": name} for name in names]}))
        self.assets.unlink()
        result = self.run_audit(arguments="")
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        summary = json.loads(self.summary.read_text(encoding="utf-8-sig"))
        self.assertEqual(9, summary["projects"])
        self.assertNotIn("binding", summary)

    def test_failed_audit_never_publishes_binding_or_success(self):
        report = json.loads(self.raw.read_text())
        report["projects"][0]["frameworks"] = [{"topLevelPackages": [{"id": MARKER,
            "vulnerabilities": [{"severity": "High"}]}]}]
        self.raw.write_text(json.dumps(report))
        self.assert_failure()


if __name__ == "__main__":
    unittest.main()
