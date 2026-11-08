"""Synthetic MSIX acceptance/rejection checks; no installation or native build."""

import contextlib
import copy
import importlib.util
import io
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import warnings
import xml.etree.ElementTree as ET
import zipfile

from test_release_privacy import native_pe

ROOT = Path(__file__).parents[2]
spec = importlib.util.spec_from_file_location("release_package", ROOT / "scripts" / "Test-ReleasePackage.py")
inspector = importlib.util.module_from_spec(spec)
spec.loader.exec_module(inspector)


def manifest(architecture="x64"):
    result = ET.parse(ROOT / "src/Wintodo.App/Package.appxmanifest").getroot()
    identity = result.find("f:Identity", inspector.NS)
    identity.set("ProcessorArchitecture", architecture.lower())
    identity.set("Name", "Synthetic.Test.Package")
    identity.set("Publisher", "CN=Synthetic Publisher")
    app = result.find("f:Applications/f:Application", inspector.NS)
    app.set("Executable", "Wintodo.exe")
    app.set("EntryPoint", "Windows.FullTrustApplication")
    return result


def payload(architecture="x64"):
    files = {"AppxManifest.xml": ET.tostring(manifest(architecture), encoding="utf-8"),
             "resources.pri": b"synthetic", "App.xbf": b"synthetic",
             "[Content_Types].xml": b"synthetic", "AppxBlockMap.xml": b"synthetic"}
    files.update({name: native_pe(machine=inspector.MACHINES[architecture]) for name in inspector.RUNTIME_FILES})
    files.update({"Assets/" + name: b"synthetic" for name in
                  ("StoreLogo.png", "Square150x150Logo.png", "Square44x44Logo.png", "Wide310x150Logo.png", "SplashScreen.png")})
    return files


class ReleasePackageTests(unittest.TestCase):
    def scan(self, files=None, architecture="x64", mutate=None, **expectations):
        files = payload(architecture) if files is None else files
        if mutate:
            doc = ET.fromstring(files["AppxManifest.xml"])
            mutate(doc)
            files["AppxManifest.xml"] = ET.tostring(doc, encoding="utf-8")
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "candidate.msix"
            with zipfile.ZipFile(path, "w", compression=zipfile.ZIP_DEFLATED) as package:
                for name, content in files.items():
                    package.writestr(name, content)
            return inspector.inspect_package(path, architecture, **expectations)

    def assert_failure(self, result, category=None):
        self.assertEqual("FAIL", result["status"])
        if category:
            self.assertIn(category, [f["category"] for f in result["findings"]])

    def test_both_architectures_pass_with_pending_policy_visible(self):
        for architecture in inspector.MACHINES:
            with self.subTest(architecture=architecture):
                result = self.scan(architecture=architecture)
                self.assertEqual("PASS", result["status"])
                self.assertEqual("PENDING", result["identity_expectations"])
                self.assertEqual("PENDING", result["payload_allowlist"])
                self.assertEqual(4, result["native_binaries"])
                self.assertEqual(64, len(result["package_sha256"]))

    def test_reviewed_identity_and_exact_payload(self):
        files = payload()
        result = self.scan(files, expected_identity="Synthetic.Test.Package", expected_publisher="CN=Synthetic Publisher",
                           expected_version=manifest().find("f:Identity", inspector.NS).get("Version"), approved_payload=list(files))
        self.assertEqual("PASS", result["status"])
        self.assertEqual("CHECKED", result["identity_expectations"])
        self.assertEqual("CHECKED", result["payload_allowlist"])

    def test_identity_mismatch_does_not_echo_policy(self):
        result = self.scan(expected_identity="private-owner-identity")
        self.assert_failure(result, "manifest_identity_mismatch")
        self.assertNotIn("private-owner-identity", json.dumps(result))

    def test_payload_allowlist_rejects_unapproved_file(self):
        files = payload()
        approved = list(files)
        files["unreviewed.json"] = b"{}"
        self.assert_failure(self.scan(files, approved_payload=approved), "payload_allowlist_mismatch")

    def test_wrong_native_or_managed_binary(self):
        for name in inspector.RUNTIME_FILES:
            for options in ({"machine": 0xAA64}, {"managed": True}):
                with self.subTest(name=name, options=options):
                    files = payload()
                    files[name] = native_pe(**options)
                    self.assert_failure(self.scan(files), "invalid_native_architecture")

    def test_mismatched_manifest_architecture(self):
        self.assert_failure(self.scan(mutate=lambda d: d.find("f:Identity", inspector.NS).set("ProcessorArchitecture", "arm64")), "manifest_architecture_mismatch")

    def test_missing_runtime_and_asset(self):
        for name in ("e_sqlite3.dll", "Assets/SplashScreen.png"):
            files = payload()
            del files[name]
            self.assert_failure(self.scan(files), "missing_manifest_assets" if name.startswith("Assets") else "missing_required_payload")

    def test_qualified_assets_and_vendor_arm64ec_resource_are_supported(self):
        files = payload()
        del files["App.xbf"]
        files["Assets/SplashScreen.scale-200.png"] = files.pop("Assets/SplashScreen.png")
        files["Microsoft.Windows.Workloads.Resources_ec.dll"] = native_pe(machine=0xA964)
        result = self.scan(files)
        self.assertEqual("PASS", result["status"])
        self.assertEqual(1, result["vendor_binaries_with_other_architectures"])

    def test_forbidden_sensitive_payload(self):
        for name in ("wintodo.db", "signing.pfx", "trace.etl", "fixtures/account.json", "attachments/file.txt", "database.db-wal"):
            with self.subTest(name=name):
                files = payload()
                files[name] = b"synthetic"
                self.assert_failure(self.scan(files), "forbidden_payload")

    def test_path_escape_and_case_duplicate(self):
        for name in ("../private-owner/file.txt", "C:/private-owner/file.txt", "/private-owner/file.txt", "WINTODO.EXE"):
            with self.subTest(name=name):
                files = payload()
                files[name] = b"synthetic"
                result = self.scan(files)
                self.assert_failure(result, "unreadable_or_invalid_package")
                self.assertNotIn("private-owner", json.dumps(result))

    def test_identical_duplicate_zip_entry_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "duplicate.msix"
            with warnings.catch_warnings(), zipfile.ZipFile(path, "w") as package:
                warnings.simplefilter("ignore", UserWarning)
                for _ in range(2):
                    package.writestr("AppxManifest.xml", b"synthetic")
            self.assert_failure(inspector.inspect_package(path, "x64"))

    def test_unreviewed_capability_and_duplicate_container(self):
        for duplicate in (False, True):
            def mutate(doc):
                node = doc.find("f:Capabilities", inspector.NS)
                if duplicate:
                    doc.append(copy.deepcopy(node))
                else:
                    ET.SubElement(node, "{" + inspector.NS["r"] + "}Capability", Name="broadFileSystemAccess")
            self.assert_failure(self.scan(mutate=mutate), "unreviewed_capabilities")

    def test_external_framework_dependency(self):
        self.assert_failure(self.scan(mutate=lambda d: ET.SubElement(d.find("f:Dependencies", inspector.NS),
                             "{" + inspector.NS["f"] + "}PackageDependency", Name="Synthetic.Runtime")), "external_package_dependency")

    def test_missing_or_unreviewed_extensions(self):
        def remove(doc):
            extensions = doc.find("f:Applications/f:Application/f:Extensions", inspector.NS)
            extensions.remove(extensions[0])
        self.assert_failure(self.scan(mutate=remove), "unreviewed_extensions")
        self.assert_failure(self.scan(mutate=lambda d: ET.SubElement(d, "{" + inspector.NS["d"] + "}Extension", Category="windows.protocol")), "unreviewed_extensions")

    def test_application_and_com_cardinality_are_required(self):
        def missing_id(doc):
            del doc.find("f:Applications/f:Application", inspector.NS).attrib["Id"]
        self.assert_failure(self.scan(mutate=missing_id), "invalid_application_activation")
        self.assert_failure(self.scan(mutate=lambda d: ET.SubElement(d, "{" + inspector.NS["f"] + "}Applications")))
        for query in ("f:Applications/f:Application/f:Extensions/c:Extension/c:ComServer",
                      "f:Applications/f:Application/f:Extensions/c:Extension/c:ComServer/c:ExeServer",
                      "f:Applications/f:Application/f:Extensions/c:Extension/c:ComServer/c:ExeServer/c:Class"):
            with self.subTest(query=query):
                def duplicate(doc):
                    doc.find(query.rsplit("/", 1)[0], inspector.NS).append(copy.deepcopy(doc.find(query, inspector.NS)))
                self.assert_failure(self.scan(mutate=duplicate), "invalid_toast_com_binding")

    def test_maximum_tested_os_version_is_valid_and_not_before_minimum(self):
        for maximum in (None, "invalid", "10.0.19041.0"):
            def mutate(doc):
                node = doc.find("f:Dependencies/f:TargetDeviceFamily", inspector.NS)
                if maximum is None:
                    del node.attrib["MaxVersionTested"]
                else:
                    node.set("MaxVersionTested", maximum)
            self.assert_failure(self.scan(mutate=mutate))

    def test_bad_com_binding_startup_and_share(self):
        changes = (
            ("f:Applications/f:Application/f:Extensions/c:Extension/c:ComServer/c:ExeServer", "Executable", "Other.exe", "unreviewed_executable_reference"),
            ("f:Applications/f:Application/f:Extensions/c:Extension/c:ComServer/c:ExeServer/c:Class", "Id", "different", "invalid_toast_com_binding"),
            ("f:Applications/f:Application/f:Extensions/d:Extension/d:StartupTask", "Enabled", "true", "invalid_startup_declaration"))
        for query, attribute, value, category in changes:
            with self.subTest(category=category):
                self.assert_failure(self.scan(mutate=lambda d: d.find(query, inspector.NS).set(attribute, value)), category)
        def share(doc):
            doc.find("f:Applications/f:Application/f:Extensions/u:Extension/u:ShareTarget/u:DataFormat", inspector.NS).text = "Bitmap"
        self.assert_failure(self.scan(mutate=share), "invalid_share_formats")

    def test_invalid_version_minimum_os_and_xml(self):
        for value in ("1.0.0", "1.0.0.65536", "0.1.0.0", "01.0.0.0"):
            self.assert_failure(self.scan(mutate=lambda d: d.find("f:Identity", inspector.NS).set("Version", value)))
        self.assert_failure(self.scan(mutate=lambda d: d.find("f:Dependencies/f:TargetDeviceFamily", inspector.NS).set("MinVersion", "10.0.19041.0")), "unsupported_device_family")
        for content in (b"<broken>", b'<!DOCTYPE Package [<!ENTITY x "unsafe">]><Package/>', '<!DOCTYPE Package><Package/>'.encode("utf-16")):
            files = payload()
            files["AppxManifest.xml"] = content
            self.assert_failure(self.scan(files))

    def test_archive_resource_bounds(self):
        original = zipfile.ZipFile.infolist
        def oversized(package):
            entries = original(package)
            entries[0].file_size = 1024 * 1024 * 1024 + 1
            return entries
        with patch.object(zipfile.ZipFile, "infolist", oversized):
            self.assert_failure(self.scan())
        files = payload()
        files["AppxManifest.xml"] = b" " * (1024 * 1024 + 1)
        self.assert_failure(self.scan(files))

    def test_existing_privacy_gate_is_required(self):
        files = payload()
        files["Wintodo.exe"] = native_pe(path="C:/build/private-owner/Wintodo.pdb")
        self.assert_failure(self.scan(files), "privacy_unsafe_first_party_codeview")

    def test_privacy_hash_mismatch_rejected(self):
        original = inspector.privacy.scan_inputs
        def mismatched(*args, **kwargs):
            result = original(*args, **kwargs)
            next(r for r in result["files"] if "package_sha256" in r)["package_sha256"] = "0" * 64
            return result
        with patch.object(inspector.privacy, "scan_inputs", mismatched):
            self.assert_failure(self.scan(), "package_changed_during_inspection")

    def test_cli_writes_redacted_report_and_exit_codes(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "candidate.msix"
            output = Path(directory) / "inspection.json"
            with zipfile.ZipFile(path, "w") as package:
                for name, content in payload().items():
                    package.writestr(name, content)
            with contextlib.redirect_stdout(io.StringIO()) as console:
                code = inspector.main([str(path), "--architecture", "x64", "--output", str(output)])
            self.assertEqual(0, code)
            self.assertEqual("PASS", json.loads(output.read_text())["status"])
            self.assertNotIn(directory, console.getvalue())
            with contextlib.redirect_stdout(io.StringIO()):
                code = inspector.main([str(path), "--architecture", "ARM64", "--output", str(output)])
            self.assertEqual(1, code)

    def test_cli_cannot_overwrite_candidate_or_policy_by_output_alias(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "candidate.msix"
            policy = Path(directory) / "approved.json"
            policy.write_text(json.dumps(list(payload())), encoding="utf-8")
            with zipfile.ZipFile(path, "w") as package:
                for name, content in payload().items():
                    package.writestr(name, content)
            original_package = path.read_bytes()
            original_policy = policy.read_bytes()
            alias = Path(directory) / "hard-link.json"
            os.link(path, alias)
            for output in (path, alias, policy):
                with self.subTest(output=output.name), contextlib.redirect_stdout(io.StringIO()):
                    code = inspector.main([str(path), "--architecture", "x64", "--approved-payload", str(policy), "--output", str(output)])
                self.assertEqual(1, code)
                self.assertEqual(original_package, path.read_bytes())
                self.assertEqual(original_policy, policy.read_bytes())

    def test_invalid_policy_rerun_replaces_prior_pass_report(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "candidate.msix"
            policy = Path(directory) / "approved.json"
            output = Path(directory) / "inspection.json"
            with zipfile.ZipFile(path, "w") as package:
                for name, content in payload().items():
                    package.writestr(name, content)
            with contextlib.redirect_stdout(io.StringIO()):
                self.assertEqual(0, inspector.main([str(path), "--architecture", "x64", "--output", str(output)]))
            self.assertEqual("PASS", json.loads(output.read_text())["status"])
            policy.write_text("{}", encoding="utf-8")
            with contextlib.redirect_stdout(io.StringIO()):
                self.assertEqual(1, inspector.main([str(path), "--architecture", "x64", "--approved-payload", str(policy), "--output", str(output)]))
            self.assertEqual("FAIL", json.loads(output.read_text())["status"])


if __name__ == "__main__":
    unittest.main()
