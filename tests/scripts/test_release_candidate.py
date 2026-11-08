"""Unsigned handoff checks exercise real synthetic MSIX and hash-pinned reports."""

import contextlib
import importlib.util
import io
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import xml.etree.ElementTree as ET
import zipfile

from test_release_package import payload

ROOT = Path(__file__).parents[2]
spec = importlib.util.spec_from_file_location("release_candidate", ROOT / "scripts" / "Test-ReleaseCandidate.py")
candidate = importlib.util.module_from_spec(spec)
spec.loader.exec_module(candidate)
COMMIT = "1" * 40


def dependencies(architecture="x64"):
    return {"scanner": "NuGet", "projects": 1, "vulnerable_packages": 0, "advisories": 0, "status": "PASS",
            "binding": {"source_commit": COMMIT, "architecture": architecture.lower(),
                        "assets_sha256": ("a" if architecture.lower() == "x64" else "b") * 64,
                        "project_set": "app", "scope": "app_assets_observed_before_and_after_audit"}}


class ReleaseCandidateTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.descriptor = self.root / "candidate.json"
        self.output = self.root / "result.json"
        self.document = {"source_commit": COMMIT, "packages": []}
        for architecture in candidate.ARCHITECTURES:
            record = {"architecture": architecture}
            for kind in candidate.KINDS:
                suffix = ".msix" if kind == "package" else ".json"
                record[kind] = {"path": architecture + "-" + kind + suffix, "sha256": ""}
            self.document["packages"].append(record)
            self.make_package(record, payload(architecture))
            self.write_input(record, "toolchain", {
                "SourceCommit": COMMIT, "Architecture": architecture.lower(), "DotnetSdk": "10.0.403",
                "AssetsSha256": dependencies(architecture)["binding"]["assets_sha256"],
                "WinAppCli": "0.7.1", "TargetRuntime": "10.0.14", "Msvc": "14.51.36231",
                "VisualStudio": "18.8.12009.203", "WindowsSdkAvailable": "10.0.26100.0", "AotGraphVerified": True,
                "Packages": {"Microsoft.DotNet.ILCompiler": "10.0.14",
                             "runtime.win-" + architecture.lower() + ".Microsoft.DotNet.ILCompiler": "10.0.14",
                             "Microsoft.Windows.SDK.BuildTools.WinApp": "0.7.1", "Microsoft.WindowsAppSDK": "2.5.1"}})
            self.write_input(record, "dependencies", dependencies(architecture))
        self.save_descriptor()

    def make_package(self, record, files):
        path = self.root / record["package"]["path"]
        with zipfile.ZipFile(path, "w") as package:
            for name, content in files.items():
                package.writestr(name, content)
        record["package"]["sha256"] = candidate.file_hash(path)
        structural = candidate.inspector.inspect_package(path, record["architecture"])
        self.assertEqual("PASS", structural["status"])
        self.write_input(record, "structural", structural)
        self.write_input(record, "privacy", candidate.inspector.privacy.scan_inputs([path], required_architectures=[record["architecture"]]))

    def write_input(self, record, kind, document):
        path = self.root / record[kind]["path"]
        path.write_text(json.dumps(document), encoding="utf-8")
        record[kind]["sha256"] = candidate.file_hash(path)

    def change_report(self, kind, mutate, architecture=0):
        record = self.document["packages"][architecture]
        path = self.root / record[kind]["path"]
        document = json.loads(path.read_text(encoding="utf-8"))
        mutate(document)
        self.write_input(record, kind, document)
        self.save_descriptor()

    def save_descriptor(self):
        self.descriptor.write_text(json.dumps(self.document), encoding="utf-8")

    def run_gate(self, output=None):
        stream = io.StringIO()
        with contextlib.redirect_stdout(stream):
            code = candidate.main([str(self.descriptor), "--output", str(output or self.output)])
        return code, stream.getvalue()

    def assert_rejected(self, category=None):
        code, stdout = self.run_gate()
        self.assertEqual(1, code)
        report = json.loads(self.output.read_text(encoding="utf-8"))
        self.assertEqual("FAIL", report["status"])
        self.assertFalse(report["release_authorized"])
        if category:
            self.assertEqual(category, report["findings"][0]["category"])
        return report, stdout

    def test_valid_pair_is_consistency_only_with_pending_production_gates(self):
        # Input order is immaterial; output order is stable.
        self.document["packages"].reverse()
        self.save_descriptor()
        code, stdout = self.run_gate()
        self.assertEqual(0, code)
        report = json.loads(self.output.read_text(encoding="utf-8"))
        self.assertEqual("PASS", report["status"])
        self.assertFalse(report["release_authorized"])
        self.assertEqual(list(candidate.ARCHITECTURES), [r["architecture"] for r in report["packages"]])
        self.assertTrue(all(r["unsigned"] for r in report["packages"]))
        self.assertIn("source_to_package_execution_provenance", report["pending_gates"])
        self.assertNotIn("audit_to_resolved_graph_binding", report["pending_gates"])
        self.assertTrue(all(r["audit_to_resolved_graph_binding"] == "MATCH; locally_observed_reports_only"
                            for r in report["packages"]))
        self.assertNotIn(str(self.root), stdout + json.dumps(report))
        self.assertNotIn("Synthetic Publisher", stdout + json.dumps(report))

    def test_required_pair_and_no_duplicate_architecture(self):
        for records in (self.document["packages"][:1], [self.document["packages"][0]] * 2):
            self.document["packages"] = records
            self.save_descriptor()
            self.assert_rejected("required_architectures_missing_or_duplicated")

    def test_abbreviated_or_private_source_commit_rejected_without_echo(self):
        for value in ("1234567", "0" * 40, "C:/Users/synthetic-private/repo", None):
            self.document["source_commit"] = value
            self.save_descriptor()
            report, stdout = self.assert_rejected("invalid_source_commit")
            self.assertNotIn("synthetic-private", stdout + json.dumps(report))

    def test_swapped_package_files_rejected(self):
        records = self.document["packages"]
        records[0]["package"], records[1]["package"] = records[1]["package"], records[0]["package"]
        self.save_descriptor()
        self.assert_rejected("gate_not_passed_or_malformed")

    def test_package_byte_change_and_stale_structural_report_rejected(self):
        record = self.document["packages"][0]
        path = self.root / record["package"]["path"]
        with zipfile.ZipFile(path, "a") as package:
            package.writestr("extra.txt", b"synthetic new payload")
        self.assert_rejected("input_hash_mismatch")
        record["package"]["sha256"] = candidate.file_hash(path)
        self.save_descriptor()
        self.assert_rejected("structural_evidence_mismatch")

    def test_each_report_hash_is_pinned(self):
        for kind in candidate.KINDS[1:]:
            path = self.root / self.document["packages"][0][kind]["path"]
            original = path.read_bytes()
            path.write_bytes(original + b" ")
            self.assert_rejected("input_hash_mismatch")
            path.write_bytes(original)

    def test_gate_failure_or_incomplete_report_cannot_be_promoted(self):
        for kind in ("structural", "privacy"):
            path = self.root / self.document["packages"][0][kind]["path"]
            original = json.loads(path.read_text(encoding="utf-8"))
            for bad in ({"status": "PASS"}, {**original, "status": "FAIL"}, {**original, "findings": [{"category": "private-marker"}]}):
                self.write_input(self.document["packages"][0], kind, bad)
                self.save_descriptor()
                self.assert_rejected("gate_not_passed_or_malformed")
            self.write_input(self.document["packages"][0], kind, original)
            self.save_descriptor()

    def test_structural_and_privacy_hash_architecture_binding(self):
        self.change_report("structural", lambda r: r.update(architecture="ARM64"))
        self.assert_rejected("evidence_architecture_mismatch")
        self.change_report("structural", lambda r: r.update(architecture="x64", manifest_sha256="f" * 64))
        self.assert_rejected("structural_evidence_mismatch")
        self.make_package(self.document["packages"][0], payload())
        self.save_descriptor()
        self.change_report("privacy", lambda r: next(f for f in r["files"] if "package_sha256" in f).update(package_sha256="f" * 64))
        self.assert_rejected("privacy_package_hash_mismatch")

    def test_missing_privacy_entry_or_wrong_executable_hash_rejected(self):
        self.change_report("privacy", lambda r: r["first_party_binaries"][0].update(sha256="f" * 64))
        self.assert_rejected("privacy_evidence_mismatch")
        self.make_package(self.document["packages"][0], payload())
        self.save_descriptor()
        self.change_report("privacy", lambda r: r["files"].pop(0))
        self.assert_rejected("invalid_privacy_evidence")

    def test_privacy_entry_hash_size_and_executable_ordinal_are_exact(self):
        for changes in ({"sha256": "f" * 64}, {"bytes": 9999}, {"entry": 9999}):
            self.make_package(self.document["packages"][0], payload())
            self.save_descriptor()
            self.change_report("privacy", lambda r: r["files"][0].update(changes))
            self.assert_rejected("privacy_evidence_mismatch")
        self.make_package(self.document["packages"][0], payload())
        self.save_descriptor()
        self.change_report("privacy", lambda r: r["first_party_binaries"][0].update(entry=0))
        self.assert_rejected("privacy_evidence_mismatch")

    def test_combined_layout_and_package_privacy_report_selects_package_binary(self):
        record = self.document["packages"][0]
        layout = self.root / "layout"
        layout.mkdir()
        (layout / "Wintodo.exe").write_bytes(payload()["Wintodo.exe"])
        report = candidate.inspector.privacy.scan_inputs([layout, self.root / record["package"]["path"]],
                                                        required_architectures=["x64"])
        self.assertEqual(2, len(report["first_party_binaries"]))
        self.write_input(record, "privacy", report)
        self.save_descriptor()
        self.assertEqual(0, self.run_gate()[0])
        self.change_report("privacy", lambda r: r["first_party_binaries"].pop(1))
        self.assert_rejected("privacy_evidence_mismatch")

    def test_privacy_input_ordinals_are_integers_not_booleans(self):
        self.change_report("privacy", lambda r: r["files"][0].update(input=False))
        self.assert_rejected("invalid_privacy_evidence")
        self.make_package(self.document["packages"][0], payload())
        self.save_descriptor()
        self.change_report("privacy", lambda r: r["first_party_binaries"][0].update(input=False))
        self.assert_rejected("privacy_evidence_mismatch")

    def test_toolchain_commit_and_architecture_must_match(self):
        self.change_report("toolchain", lambda r: r.update(SourceCommit="2" * 40))
        self.assert_rejected("source_commit_mismatch")
        self.change_report("toolchain", lambda r: r.update(SourceCommit=COMMIT, Architecture="arm64"))
        self.assert_rejected("evidence_architecture_mismatch")

    def test_incomplete_or_inconsistent_aot_tool_evidence_rejected(self):
        self.change_report("toolchain", lambda r: r.update(AotGraphVerified=False))
        self.assert_rejected("aot_graph_not_verified")
        self.change_report("toolchain", lambda r: r.update(AotGraphVerified=True, Msvc="C:/Users/private-tool"))
        report, stdout = self.assert_rejected("invalid_toolchain_evidence")
        self.assertNotIn("private-tool", json.dumps(report) + stdout)
        self.change_report("toolchain", lambda r: r.update(Msvc="14.51.36231", TargetRuntime="10.0.15"))
        self.assert_rejected("inconsistent_toolchain_graph")

    def test_explicit_toolchain_failure_is_rejected_even_with_complete_fields(self):
        self.change_report("toolchain", lambda r: r.update(Status="FAIL"))
        self.assert_rejected("invalid_toolchain_evidence")

    def test_cross_architecture_dependency_versions_must_match(self):
        self.change_report("toolchain", lambda r: r["Packages"].update({"Microsoft.WindowsAppSDK": "2.5.2"}), architecture=1)
        self.assert_rejected("cross_architecture_dependency_graph_mismatch")

    def test_dependency_failure_malformed_counters_or_solution_scope_rejected(self):
        for changes in ({"status": "FAIL"}, {"projects": 9}, {"projects": True}, {"advisories": 1},
                        {"vulnerable_packages": False}, {"scanner": "other"}):
            self.change_report("dependencies", lambda r: r.update(changes))
            self.assert_rejected("dependency_gate_not_passed_or_malformed")
            self.write_input(self.document["packages"][0], "dependencies", dependencies())
            self.save_descriptor()

    def test_missing_or_malformed_dependency_binding_rejected_without_echo(self):
        for value in (None, {}, {"source_commit": "private-owner"}, [], "private-owner"):
            self.change_report("dependencies", lambda r: r.update(binding=value))
            report, stdout = self.assert_rejected("dependency_binding_missing_or_malformed")
            self.assertNotIn("private-owner", stdout + json.dumps(report))
        self.change_report("dependencies", lambda r: r.pop("binding"))
        self.assert_rejected("dependency_binding_missing_or_malformed")

    def test_dependency_binding_source_target_and_hash_must_match_toolchain(self):
        for field, value, category in (("source_commit", "2" * 40, "source_commit_mismatch"),
                                       ("architecture", "arm64", "evidence_architecture_mismatch"),
                                       ("assets_sha256", "f" * 64, "dependency_assets_hash_mismatch")):
            self.write_input(self.document["packages"][0], "dependencies", dependencies())
            self.change_report("dependencies", lambda r: r["binding"].update({field: value}))
            self.assert_rejected(category)

    def test_dependency_binding_requires_app_scope_exact_shape_and_valid_hash(self):
        for changes in ({"project_set": "solution"}, {"scope": "other"}, {"assets_sha256": True},
                        {"assets_sha256": "C:/Users/private-owner/graph"}, {"extra": "private-owner"}):
            self.write_input(self.document["packages"][0], "dependencies", dependencies())
            self.change_report("dependencies", lambda r: r["binding"].update(changes))
            report, stdout = self.assert_rejected("dependency_binding_missing_or_malformed")
            self.assertNotIn("private-owner", stdout + json.dumps(report))

    def test_toolchain_assets_hash_is_required_and_bound(self):
        self.change_report("toolchain", lambda r: r.pop("AssetsSha256"))
        self.assert_rejected("invalid_toolchain_evidence")
        self.change_report("toolchain", lambda r: r.update(AssetsSha256="f" * 64))
        self.assert_rejected("dependency_assets_hash_mismatch")

    def test_unsigned_handoff_rejects_signature_presence(self):
        files = payload()
        files["AppxSignature.p7x"] = b"synthetic signature"
        self.make_package(self.document["packages"][0], files)
        self.save_descriptor()
        self.assert_rejected("signed_package_in_unsigned_handoff")

    def test_cross_architecture_identity_publisher_and_version(self):
        for field, value in (("Name", "Synthetic.Other.Package"), ("Publisher", "CN=Synthetic Other"), ("Version", "1.0.0.6")):
            files = payload("ARM64")
            document = ET.fromstring(files["AppxManifest.xml"])
            document.find("f:Identity", candidate.inspector.NS).set(field, value)
            files["AppxManifest.xml"] = ET.tostring(document)
            self.make_package(self.document["packages"][1], files)
            self.save_descriptor()
            self.assert_rejected("cross_architecture_identity_or_version_mismatch")

    def test_missing_private_input_and_corrupt_json_invalidate_old_pass(self):
        self.assertEqual(0, self.run_gate()[0])
        record = self.document["packages"][0]["dependencies"]
        record["path"] = "synthetic-private-account-report.json"
        self.save_descriptor()
        report, stdout = self.assert_rejected("invalid_input_or_output")
        self.assertNotIn("synthetic-private", json.dumps(report) + stdout)
        self.descriptor.write_bytes(b"not json")
        self.assert_rejected("invalid_input_or_output")

    def test_duplicate_json_keys_and_malformed_descriptor_invalidate_old_pass(self):
        self.assertEqual(0, self.run_gate()[0])
        self.descriptor.write_text('{"source_commit":"' + COMMIT + '","source_commit":"' + COMMIT + '"}', encoding="utf-8")
        self.assert_rejected("duplicate_json_key")
        self.save_descriptor()
        self.document["release_authorized"] = True
        self.save_descriptor()
        self.assert_rejected("invalid_descriptor")

    def test_output_alias_descriptor_each_evidence_and_hardlinks_preserves_bytes(self):
        paths = [self.descriptor] + [self.root / r[k]["path"] for r in self.document["packages"] for k in candidate.KINDS]
        for path in paths:
            original = path.read_bytes()
            code, stdout = self.run_gate(output=path)
            self.assertEqual(1, code)
            self.assertIn("output_aliases_input", stdout)
            self.assertEqual(original, path.read_bytes())
        alias = self.root / "alias.json"
        os.link(self.root / self.document["packages"][0]["structural"]["path"], alias)
        original = alias.read_bytes()
        self.assertEqual(1, self.run_gate(output=alias)[0])
        self.assertEqual(original, alias.read_bytes())

    def test_tampering_after_inspection_rejected(self):
        original = candidate.file_hash
        record = self.document["packages"][0]["structural"]
        target = self.root / record["path"]
        def changed(path):
            return "f" * 64 if path == target else original(path)
        with patch.object(candidate, "file_hash", side_effect=changed):
            self.assert_rejected("input_changed_during_verification")

    def test_unwritable_output_returns_failure_and_leaves_no_temporary_file(self):
        with patch.object(candidate.os, "replace", side_effect=OSError("synthetic-private-output")):
            code, stdout = self.run_gate()
        self.assertEqual(1, code)
        self.assertNotIn("synthetic-private-output", stdout)
        self.assertEqual(set(p.name for p in self.root.iterdir()), {"candidate.json", *(
            r[k]["path"] for r in self.document["packages"] for k in candidate.KINDS)})


if __name__ == "__main__":
    unittest.main()
