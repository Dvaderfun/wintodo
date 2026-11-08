"""Synthetic scanner failures must fail closed and never export marked data."""
import contextlib
import importlib.util
import io
import json
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location(
    "ci_security", Path(__file__).parents[2] / "scripts" / "Test-CiSecurity.py")
security = importlib.util.module_from_spec(spec)
spec.loader.exec_module(security)
MARKER = "SYNTHETIC-SECRET person@example.invalid C:\\Users\\SYNTHETIC-OWNER\\private"


def clean_dependencies():
    return {"version": 1, "parameters": "--vulnerable --include-transitive",
            "projects": [{"path": str(Path(MARKER) / name)}
                         for name in sorted(security.SOLUTION_PROJECTS)]}


class CiSecurityTests(unittest.TestCase):
    def test_secret_findings_counted_without_detail(self):
        summary = security.secret_summary([{"Secret": MARKER, "File": MARKER}], 1)
        self.assertEqual("FAIL", summary["status"])
        self.assertEqual(1, summary["findings"])
        self.assertNotIn("SYNTHETIC", json.dumps(summary))

    def test_secret_exit_and_report_must_agree(self):
        for report, code in (([], 1), ([{}], 0), ([], 2), ({}, 0)):
            with self.subTest(report=report, code=code), self.assertRaises(ValueError):
                security.secret_summary(report, code)

    def test_clean_secret_scan(self):
        self.assertEqual("PASS", security.secret_summary([], 0)["status"])

    def test_clean_solution_audit(self):
        summary = security.dependency_summary(clean_dependencies(), 0)
        self.assertEqual("PASS", summary["status"])
        self.assertEqual(9, summary["projects"])
        self.assertNotIn("SYNTHETIC", json.dumps(summary))

    def test_direct_and_transitive_findings_fail(self):
        report = clean_dependencies()
        report["projects"][0]["frameworks"] = [{
            kind: [{"id": MARKER, "vulnerabilities": [{"severity": MARKER}]}]
            for kind in ("topLevelPackages", "transitivePackages")}]
        summary = security.dependency_summary(report, 0)
        self.assertEqual("FAIL", summary["status"])
        self.assertEqual(2, summary["advisories"])
        self.assertNotIn("SYNTHETIC", json.dumps(summary))

    def test_missing_duplicate_or_error_projects_rejected(self):
        for mutation in ("missing", "duplicate", "problem", "exit", "scope"):
            report = clean_dependencies()
            code = 0
            if mutation == "missing": report["projects"].pop()
            if mutation == "duplicate": report["projects"].append(report["projects"][0])
            if mutation == "problem": report["problems"] = [{"text": MARKER}]
            if mutation == "exit": code = 1
            if mutation == "scope": report["parameters"] = "--vulnerable"
            with self.subTest(mutation=mutation), self.assertRaises(ValueError):
                security.dependency_summary(report, code)

    def test_app_scope_is_explicit(self):
        report = clean_dependencies()
        report["projects"] = [{"path": "src/Wintodo.App/Wintodo.csproj"}]
        self.assertEqual("PASS", security.dependency_summary(report, 0, "app")["status"])
        with self.assertRaises(ValueError): security.dependency_summary(report, 0)

    def test_explicit_malformed_framework_and_package_collections_rejected(self):
        for value in ({}, "", None, ["invalid"]):
            report = clean_dependencies()
            report["projects"][0]["frameworks"] = value
            with self.subTest(value=value), self.assertRaises(ValueError):
                security.dependency_summary(report, 0)
            for kind in ("topLevelPackages", "transitivePackages"):
                report["projects"][0]["frameworks"] = [{kind: value}]
                with self.subTest(value=value, kind=kind), self.assertRaises(ValueError):
                    security.dependency_summary(report, 0)

    def test_corrupt_report_removes_stale_success_and_redacts_error(self):
        with tempfile.TemporaryDirectory() as directory:
            report = Path(directory) / "report.json"
            output = Path(directory) / "summary.json"
            report.write_text(MARKER)
            output.write_text('{"status":"PASS"}')
            stdout = io.StringIO()
            with contextlib.redirect_stdout(stdout):
                result = security.main(["secrets", str(report), "--exit-code", "0", "--output", str(output)])
            self.assertEqual(1, result)
            self.assertFalse(output.exists())
            self.assertNotIn("SYNTHETIC", stdout.getvalue())


if __name__ == "__main__":
    unittest.main()
