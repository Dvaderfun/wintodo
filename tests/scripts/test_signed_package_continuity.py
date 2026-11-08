"""Real synthetic ZIP payload comparisons; no signing, installation or UI."""

import contextlib
import copy
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import struct
import tempfile
import unittest
from unittest.mock import patch
import xml.etree.ElementTree as ET
import zipfile

from test_release_package import payload

ROOT = Path(__file__).parents[2]
spec = importlib.util.spec_from_file_location("signed_continuity", ROOT / "scripts" / "Test-SignedPackageContinuity.py")
checker = importlib.util.module_from_spec(spec)
spec.loader.exec_module(checker)


def types_xml(catalog=False, signature=False):
    root = ET.Element("{" + checker.CT_NS + "}Types")
    ET.SubElement(root, "{" + checker.CT_NS + "}Default", Extension="xml", ContentType="application/vnd.ms-appx.manifest+xml")
    ET.SubElement(root, "{" + checker.CT_NS + "}Override", PartName="/AppxBlockMap.xml", ContentType="application/vnd.ms-appx.blockmap+xml")
    for name, enabled in ((checker.SIGNATURE, signature), (checker.CATALOG, catalog)):
        if enabled:
            ET.SubElement(root, "{" + checker.CT_NS + "}Override", PartName="/" + name, ContentType=checker.SIGNING_TYPES[name])
    return ET.tostring(root, encoding="utf-8")


class SignedPackageContinuityTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.unsigned = self.root / "unsigned.msix"
        self.signed = self.root / "signed.msix"
        self.original = payload()
        self.original["[Content_Types].xml"] = types_xml()
        self.final = copy.deepcopy(self.original)
        self.final[checker.SIGNATURE] = b"synthetic unauthenticated signature"
        self.final["[Content_Types].xml"] = types_xml(signature=True)

    def write(self, path, files):
        with zipfile.ZipFile(path, "w", compression=zipfile.ZIP_DEFLATED) as package:
            for name, data in files.items():
                package.writestr(name, data)

    def prepare(self):
        self.write(self.unsigned, self.original)
        self.write(self.signed, self.final)
        return hashlib.sha256(self.unsigned.read_bytes()).hexdigest(), hashlib.sha256(self.signed.read_bytes()).hexdigest()

    def inspect(self, architecture="x64"):
        hashes = self.prepare()
        return checker.inspect_continuity(self.unsigned, self.signed, architecture, *hashes)

    def rejected(self, category):
        with self.assertRaises(checker.InvalidContinuity) as caught:
            self.inspect()
        self.assertEqual(category, str(caught.exception))

    def test_both_architectures_preserve_payload_and_leave_trust_pending(self):
        for architecture in ("x64", "ARM64"):
            with self.subTest(architecture=architecture):
                self.original = payload(architecture)
                self.original["[Content_Types].xml"] = types_xml()
                self.final = copy.deepcopy(self.original)
                self.final[checker.SIGNATURE] = b"synthetic"
                self.final["[Content_Types].xml"] = types_xml(signature=True)
                report = self.inspect(architecture)
                self.assertEqual("PASS", report["status"])
                self.assertFalse(report["release_authorized"])
                self.assertEqual("PENDING", report["signature_trust"])
                self.assertEqual("PENDING", report["timestamp_policy"])
                self.assertEqual("PENDING", report["source_provenance"])
                self.assertEqual("PENDING", report["deployment_acceptance"])

    def test_only_optional_new_catalog_is_allowed(self):
        self.final[checker.CATALOG] = b"synthetic unauthenticated catalog"
        self.final["[Content_Types].xml"] = types_xml(catalog=True, signature=True)
        report = self.inspect()
        self.assertTrue(report["added_code_integrity_catalog"])
        self.final[checker.CATALOG] = b""
        self.rejected("invalid_signing_footprint")

    def test_existing_catalog_is_immutable(self):
        self.original[checker.CATALOG] = b"original catalog"
        self.final[checker.CATALOG] = b"original catalog"
        self.original["[Content_Types].xml"] = types_xml(catalog=True)
        self.final["[Content_Types].xml"] = types_xml(catalog=True, signature=True)
        self.assertFalse(self.inspect()["added_code_integrity_catalog"])
        self.final[checker.CATALOG] = b"changed catalog"
        self.rejected("original_entry_changed")

    def test_payload_manifest_blockmap_and_entry_case_are_immutable(self):
        for name in ("Wintodo.exe", "AppxManifest.xml", "AppxBlockMap.xml", "resources.pri"):
            with self.subTest(name=name):
                saved = self.final[name]
                self.final[name] = saved + b"tamper"
                self.rejected("original_entry_changed")
                self.final[name] = saved
        self.final["wintodo.exe"] = self.final.pop("Wintodo.exe")
        self.rejected("entry_name_changed")

    def test_missing_empty_or_unsigned_signature_fails(self):
        signature = self.final.pop(checker.SIGNATURE)
        self.rejected("missing_or_empty_signature")
        self.final[checker.SIGNATURE] = b""
        self.rejected("missing_or_empty_signature")
        self.final[checker.SIGNATURE] = signature
        self.original[checker.SIGNATURE] = signature
        self.rejected("unsigned_input_contains_signature")

    def test_other_additions_and_removals_fail(self):
        self.final["unreviewed.txt"] = b"unreviewed"
        self.rejected("unexpected_entry_set_change")
        del self.final["unreviewed.txt"]
        del self.final["Assets/SplashScreen.png"]
        self.rejected("unexpected_entry_set_change")

    def test_content_type_mapping_changes_removals_and_unknown_attributes_fail(self):
        root = ET.fromstring(types_xml(signature=True))
        for change in ("value", "remove", "root_attribute", "child_attribute", "child_element", "child_text", "namespace"):
            with self.subTest(change=change):
                doc = copy.deepcopy(root)
                if change == "value": doc[0].set("ContentType", "changed/private")
                elif change == "remove": doc.remove(doc[0])
                elif change == "root_attribute": doc.set("private", "value")
                elif change == "child_attribute": doc[0].set("private", "value")
                elif change == "child_element": ET.SubElement(doc[0], "{" + checker.CT_NS + "}Private")
                elif change == "child_text": doc[0].text = "private"
                elif change == "namespace": doc[0].tag = "{urn:private}Default"
                self.final["[Content_Types].xml"] = ET.tostring(doc, encoding="utf-8")
                with self.assertRaises(checker.InvalidContinuity): self.inspect()

    def test_duplicate_or_wrong_signing_declaration_fails(self):
        doc = ET.fromstring(types_xml(signature=True))
        doc.append(copy.deepcopy(doc[0]))
        self.final["[Content_Types].xml"] = ET.tostring(doc, encoding="utf-8")
        self.rejected("invalid_content_types")
        doc = ET.fromstring(types_xml(signature=True))
        doc[-1].set("ContentType", "wrong/signature")
        self.final["[Content_Types].xml"] = ET.tostring(doc, encoding="utf-8")
        self.rejected("content_types_changed_beyond_signing")

    def test_content_types_reserialization_and_reordering_are_semantically_equivalent(self):
        doc = ET.fromstring(types_xml(signature=True))
        doc[:] = list(reversed(list(doc)))
        ET.indent(doc)
        self.final["[Content_Types].xml"] = ET.tostring(doc, encoding="utf-8", xml_declaration=True)
        self.assertEqual("PASS", self.inspect()["status"])

    def test_unsafe_duplicate_and_symlink_entries_fail(self):
        with self.assertRaisesRegex(checker.InvalidContinuity, "unsafe_package_name"):
            checker.safe_name("bad\\path")
        for name in ("../private", "/absolute", "C:/private", "trail./entry", "nul\x01", "a//b"):
            with self.subTest(name=name):
                self.final[name] = b"bad"
                self.rejected("unsafe_package_name")
                del self.final[name]
        self.final["WINTODO.EXE"] = self.final["Wintodo.exe"]
        self.rejected("duplicate_package_name")
        del self.final["WINTODO.EXE"]
        hashes = self.prepare()
        info = zipfile.ZipInfo("private-link")
        info.create_system = 3
        info.external_attr = (0o120777 << 16)
        with zipfile.ZipFile(self.signed, "a") as package: package.writestr(info, "target")
        with self.assertRaisesRegex(checker.InvalidContinuity, "unsupported_entry_type"):
            checker.inspect_continuity(self.unsigned, self.signed, "x64", hashes[0], hashlib.sha256(self.signed.read_bytes()).hexdigest())

    def test_xml_doctype_and_alternate_encoding_fail(self):
        for data in (b'<!DOCTYPE Types [<!ENTITY x "private">]><Types/>', types_xml(signature=True).decode().encode("utf-16")):
            self.final["[Content_Types].xml"] = data
            with self.assertRaises(checker.InvalidContinuity): self.inspect()

    def test_raw_zip_null_and_backslash_names_are_rejected_before_normalization(self):
        for original, unsafe in ((b"unsafe0.bin", b"unsafe\x00.bin"), (b"bad/path", b"bad\\path")):
            with self.subTest(unsafe=unsafe):
                self.final[original.decode()] = b"unsafe"
                self.prepare()
                self.signed.write_bytes(self.signed.read_bytes().replace(original, unsafe))
                with self.assertRaisesRegex(checker.InvalidContinuity, "unsafe_package_name"):
                    checker.inspect_continuity(self.unsigned, self.signed, "x64", hashlib.sha256(self.unsigned.read_bytes()).hexdigest(), hashlib.sha256(self.signed.read_bytes()).hexdigest())
                del self.final[original.decode()]

    def test_manifest_multiple_identities_and_wrong_root_fail(self):
        saved = self.original["AppxManifest.xml"]
        for multiple in (True, False):
            doc = ET.fromstring(saved)
            if multiple: doc.append(copy.deepcopy(doc.find("f:Identity", checker.inspector.NS)))
            else: doc.tag = "{urn:unsupported}Package"
            self.original["AppxManifest.xml"] = ET.tostring(doc, encoding="utf-8")
            self.final["AppxManifest.xml"] = self.original["AppxManifest.xml"]
            self.rejected("invalid_manifest_identity")

    def test_wrong_architecture_and_hash_fail(self):
        hashes = self.prepare()
        with self.assertRaisesRegex(checker.InvalidContinuity, "manifest_architecture_mismatch"):
            checker.inspect_continuity(self.unsigned, self.signed, "ARM64", *hashes)
        with self.assertRaisesRegex(checker.InvalidContinuity, "input_hash_mismatch"):
            checker.inspect_continuity(self.unsigned, self.signed, "x64", "0" * 64, hashes[1])

    def test_bounds_are_enforced_without_large_files(self):
        hashes = self.prepare()
        with patch.object(checker, "MAX_ENTRY", 4), self.assertRaises(checker.InvalidContinuity):
            checker.inspect_continuity(self.unsigned, self.signed, "x64", *hashes)
        with patch.object(checker, "MAX_XML", 4), self.assertRaises(checker.InvalidContinuity):
            checker.inspect_continuity(self.unsigned, self.signed, "x64", *hashes)

    def test_corrupt_and_encrypted_entries_fail(self):
        for encrypted in (False, True):
            with self.subTest(encrypted=encrypted):
                self.prepare()
                raw = bytearray(self.signed.read_bytes())
                central = raw.find(b"PK\x01\x02")
                if encrypted:
                    flags = struct.unpack_from("<H", raw, central + 8)[0]
                    struct.pack_into("<H", raw, central + 8, flags | 1)
                else:
                    crc = struct.unpack_from("<I", raw, central + 16)[0]
                    struct.pack_into("<I", raw, central + 16, crc ^ 1)
                self.signed.write_bytes(raw)
                with self.assertRaises((checker.InvalidContinuity, zipfile.BadZipFile)):
                    checker.inspect_continuity(self.unsigned, self.signed, "x64", hashlib.sha256(self.unsigned.read_bytes()).hexdigest(), hashlib.sha256(raw).hexdigest())

    def test_cli_failed_rerun_replaces_stale_pass_without_private_details(self):
        hashes = self.prepare()
        output = self.root / "report.json"
        args = [str(self.unsigned), str(self.signed), "--architecture", "x64", "--unsigned-sha256", hashes[0], "--signed-sha256", hashes[1], "--output", str(output)]
        with contextlib.redirect_stdout(io.StringIO()): self.assertEqual(0, checker.main(args))
        self.assertEqual("PASS", json.loads(output.read_text())["status"])
        self.signed.write_bytes(b"private-owner-invalid-package")
        captured = io.StringIO()
        with contextlib.redirect_stdout(captured): self.assertEqual(1, checker.main(args))
        self.assertEqual("FAIL", json.loads(output.read_text())["status"])
        self.assertNotIn("private-owner", captured.getvalue())
        self.assertNotIn(str(self.root), captured.getvalue())

    def test_output_aliases_and_write_errors_fail_without_touching_inputs(self):
        hashes = self.prepare()
        for output in (self.unsigned, self.root / "hardlink.msix"):
            if output != self.unsigned: os.link(self.unsigned, output)
            args = [str(self.unsigned), str(self.signed), "--architecture", "x64", "--unsigned-sha256", hashes[0], "--signed-sha256", hashes[1], "--output", str(output)]
            with contextlib.redirect_stdout(io.StringIO()): self.assertEqual(1, checker.main(args))
            self.assertEqual(hashes[0], hashlib.sha256(self.unsigned.read_bytes()).hexdigest())
        with patch.object(checker.os, "replace", side_effect=PermissionError("private message")):
            with contextlib.redirect_stdout(io.StringIO()):
                self.assertEqual(1, checker.main(args[:-1] + [str(self.root / "report.json")]))

    def test_input_change_during_verification_is_rejected(self):
        hashes = self.prepare()
        original_hash = checker.stream_hash
        calls = 0
        def changed(stream):
            nonlocal calls
            calls += 1
            digest = original_hash(stream)
            return "0" * 64 if calls == 3 else digest
        with patch.object(checker, "stream_hash", side_effect=changed), self.assertRaisesRegex(checker.InvalidContinuity, "input_changed_during_verification"):
            checker.inspect_continuity(self.unsigned, self.signed, "x64", *hashes)


if __name__ == "__main__":
    unittest.main()
