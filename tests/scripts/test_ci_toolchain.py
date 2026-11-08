"""Exact tool metadata and configuration reports must omit private paths."""
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest


REPOSITORY = Path(__file__).parents[2]
EXPORTER = REPOSITORY / "scripts" / "Export-CiToolchain.ps1"
MARKER = "SYNTHETIC-OWNER's private"


def quote(value):
    return "'" + str(value).replace("'", "''") + "'"


class CiToolchainTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.pwsh = shutil.which("pwsh")
        if not cls.pwsh:
            raise RuntimeError("PowerShell is required for toolchain evidence regressions")
        cls.scratch = REPOSITORY / "artifacts" / "dep-next-ci" / "script-tests"
        cls.scratch.mkdir(parents=True, exist_ok=True)
        cls.functions = f"""$ErrorActionPreference = 'Stop'
$tokens = $null
$parseErrors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile({quote(EXPORTER)}, [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count) {{ throw 'Exporter parse failed.' }}
$names = @('ConvertTo-CiToolBinaryRecord', 'Get-CiToolBinaryRecord', 'Get-CiConfiguredSdkTools',
           'Get-CiAssetsSnapshot', 'Assert-CiAssetsUnchanged')
$functions = $ast.FindAll({{ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] }}, $true)
foreach ($name in $names) {{
    $function = @($functions | Where-Object Name -eq $name)
    if ($function.Count -ne 1) {{ throw 'Exporter evidence seam missing.' }}
    . ([scriptblock]::Create($function[0].Extent.Text))
}}
"""

    def run_script(self, body):
        completed = subprocess.run(
            [self.pwsh, "-NoProfile", "-NonInteractive", "-Command", self.functions + body],
            capture_output=True, text=True, check=False)
        self.assertEqual(0, completed.returncode, completed.stderr)
        self.assertNotIn(MARKER, completed.stdout + completed.stderr)
        self.assertNotIn(str(REPOSITORY), completed.stdout)
        return json.loads(completed.stdout)

    def convert(self, file_version, product_version, digest="AB" * 32):
        return self.run_script(f"""
try {{
    $result = ConvertTo-CiToolBinaryRecord -VersionInfo @{{
        FileVersionRaw = {quote(file_version)}; ProductVersionRaw = {quote(product_version)}
    }} -Sha256 {quote(digest)}
    $result | ConvertTo-Json -Compress
}} catch {{ @{{ error = $_.Exception.Message }} | ConvertTo-Json -Compress }}
""")

    def test_serviced_file_and_product_versions_are_not_directory_versions(self):
        result = self.convert("19.51.36248.0", "14.51.36248.0")
        self.assertEqual("19.51.36248.0", result["FileVersion"])
        self.assertEqual("14.51.36248.0", result["ProductVersion"])
        self.assertEqual("ab" * 32, result["Sha256"])

    def test_invalid_versions_and_hashes_fail_without_details(self):
        for file_version, product_version, digest in (
            (MARKER, "14.51.36248.0", "ab" * 32),
            ("19.51.36248.0", MARKER, "ab" * 32),
            ("0.0.0.0", "14.51.36248.0", "ab" * 32),
            ("19.51.36248.0", "0.0.0.0", "ab" * 32),
            ("19.51.36248", "14.51.36248.0", "ab" * 32),
            ("19.51.36248.0", "14.51.36248.0", MARKER),
            ("19.51.36248.0", "14.51.36248.0", "ab" * 31),
        ):
            with self.subTest(file_version=file_version, product_version=product_version):
                self.assertEqual({"error": "WINTODO_TOOL_BINARY_EVIDENCE_INVALID"},
                                 self.convert(file_version, product_version, digest))

    def test_binary_version_and_hash_are_read_from_exact_file_without_path(self):
        with tempfile.TemporaryDirectory(prefix=MARKER, dir=self.scratch) as directory:
            binary = Path(directory) / "private-tool.exe"
            shutil.copyfile(shutil.which("dotnet"), binary)
            result = self.run_script(f"Get-CiToolBinaryRecord -Path {quote(binary)} | ConvertTo-Json -Compress")
            self.assertEqual(hashlib.sha256(binary.read_bytes()).hexdigest(), result["Sha256"])
            self.assertRegex(result["FileVersion"], r"^\d+(\.\d+){3}$")
            self.assertEqual({"FileVersion", "ProductVersion", "Sha256"}, set(result))

    def test_missing_nonbinary_and_directory_inputs_fail_without_path(self):
        with tempfile.TemporaryDirectory(prefix=MARKER, dir=self.scratch) as directory:
            text = Path(directory) / "unversioned.exe"
            text.write_text(MARKER)
            for path in (Path(directory) / "missing.exe", text, Path(directory)):
                with self.subTest(kind=path.name):
                    result = self.run_script(f"""
try {{ Get-CiToolBinaryRecord -Path {quote(path)} | Out-Null; throw 'Unexpected success.' }}
catch {{ @{{ error = $_.Exception.Message }} | ConvertTo-Json -Compress }}
""")
                    self.assertEqual({"error": "WINTODO_TOOL_BINARY_EVIDENCE_INVALID"}, result)

    def configured(self, package_root, paths):
        properties = "; ".join(f"{name} = {quote(path)}" for name, path in paths.items())
        return self.run_script(f"""
try {{
    Get-CiConfiguredSdkTools -Properties @{{ {properties} }} -PackageRoot {quote(package_root)} |
        ConvertTo-Json -Depth 5 -Compress
}} catch {{ @{{ error = $_.Exception.Message }} | ConvertTo-Json -Compress }}
""")

    def fixture_paths(self, directory):
        paths = {}
        for property_name, name in (
            ("MakeAppxExeFullPath", "makeappx.exe"),
            ("MakePriExeFullPath", "makepri.exe"),
            ("RcExeFullPath", "rc.exe"),
        ):
            path = Path(directory) / name
            shutil.copyfile(shutil.which("dotnet"), path)
            paths[property_name] = path
        return paths

    def test_configured_package_tools_record_exact_hashes_and_no_paths(self):
        with tempfile.TemporaryDirectory(prefix=MARKER, dir=self.scratch) as directory:
            paths = self.fixture_paths(directory)
            result = self.configured(directory, paths)
            self.assertEqual({"makeappx.exe", "makepri.exe", "rc.exe"}, set(result))
            for path in paths.values():
                self.assertEqual("restored-sdk-buildtools", result[path.name]["Source"])
                self.assertEqual(hashlib.sha256(path.read_bytes()).hexdigest(), result[path.name]["Sha256"])

    def test_similar_package_prefix_is_labelled_external_override(self):
        with tempfile.TemporaryDirectory(prefix=MARKER, dir=self.scratch) as directory:
            paths = self.fixture_paths(directory)
            result = self.configured(directory[:-1], paths)
            self.assertTrue(all(tool["Source"] == "external-override" for tool in result.values()))

    def test_missing_relative_or_wrong_tool_selection_is_rejected(self):
        with tempfile.TemporaryDirectory(prefix=MARKER, dir=self.scratch) as directory:
            valid = self.fixture_paths(directory)
            for mutation in ("missing", "empty", "relative", "wrong-name", "absent-file"):
                paths = dict(valid)
                if mutation == "missing": paths.pop("RcExeFullPath")
                if mutation == "empty": paths["RcExeFullPath"] = ""
                if mutation == "relative": paths["RcExeFullPath"] = "rc.exe"
                if mutation == "wrong-name": paths["RcExeFullPath"] = valid["MakePriExeFullPath"]
                if mutation == "absent-file": paths["RcExeFullPath"] = Path(directory) / "missing" / "rc.exe"
                with self.subTest(mutation=mutation):
                    expected = ("WINTODO_TOOL_BINARY_EVIDENCE_INVALID" if mutation == "absent-file"
                                else "WINTODO_SDK_TOOL_SELECTION_INVALID")
                    self.assertEqual({"error": expected}, self.configured(directory, paths))

    def test_assets_hash_covers_exact_bom_and_whitespace_bytes(self):
        with tempfile.TemporaryDirectory(prefix=MARKER, dir=self.scratch) as directory:
            path = Path(directory) / "project.assets.json"
            path.write_bytes(b'\xef\xbb\xbf{ "libraries": {}, "targets": {} }\r\n')
            result = self.run_script(f"""
$snapshot = Get-CiAssetsSnapshot -Path {quote(path)}
Assert-CiAssetsUnchanged -Path {quote(path)} -Sha256 $snapshot.Sha256
@{{ sha256 = $snapshot.Sha256; libraries = $snapshot.Graph.libraries.Count }} | ConvertTo-Json -Compress
""")
            self.assertEqual(hashlib.sha256(path.read_bytes()).hexdigest(), result["sha256"])
            self.assertEqual(0, result["libraries"])

    def test_assets_mutation_or_removal_is_rejected_without_path(self):
        with tempfile.TemporaryDirectory(prefix=MARKER, dir=self.scratch) as directory:
            path = Path(directory) / "project.assets.json"
            for change in (f"[IO.File]::AppendAllText({quote(path)}, ' ')",
                           f"Remove-Item -LiteralPath {quote(path)}"):
                path.write_text('{"libraries":{},"targets":{}}', encoding="utf-8")
                result = self.run_script(f"""
$snapshot = Get-CiAssetsSnapshot -Path {quote(path)}
{change}
try {{ Assert-CiAssetsUnchanged -Path {quote(path)} -Sha256 $snapshot.Sha256; throw 'Unexpected success.' }}
catch {{ @{{ error = $_.Exception.Message }} | ConvertTo-Json -Compress }}
""")
                self.assertEqual({"error": "WINTODO_ASSETS_EVIDENCE_INVALID"}, result)

    def test_invalid_assets_fail_without_private_content_or_path(self):
        with tempfile.TemporaryDirectory(prefix=MARKER, dir=self.scratch) as directory:
            path = Path(directory) / "project.assets.json"
            for value in (MARKER.encode(), b'\xff', b'null', b'[]',
                          b'{"libraries":[],"targets":{}}', b'{"libraries":{}}'):
                path.write_bytes(value)
                result = self.run_script(f"""
try {{ Get-CiAssetsSnapshot -Path {quote(path)} | Out-Null; throw 'Unexpected success.' }}
catch {{ @{{ error = $_.Exception.Message }} | ConvertTo-Json -Compress }}
""")
                self.assertEqual({"error": "WINTODO_ASSETS_EVIDENCE_INVALID"}, result)


if __name__ == "__main__":
    unittest.main()
