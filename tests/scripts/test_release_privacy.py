"""Synthetic artifact privacy regressions; no owner data or native build needed."""

import importlib.util
import contextlib
import io
import json
import struct
import tempfile
import unittest
from unittest.mock import patch
import zipfile
from pathlib import Path

spec = importlib.util.spec_from_file_location(
    "release_privacy", Path(__file__).parents[2] / "scripts" / "Test-ReleasePrivacy.py"
)
privacy = importlib.util.module_from_spec(spec)
spec.loader.exec_module(privacy)


def native_pe(path="Wintodo.pdb", machine=0x8664, managed=False, embedded=False):
    data = bytearray(4096)
    data[:2] = b"MZ"
    struct.pack_into("<I", data, 60, 128)
    data[128:132] = b"PE\0\0"
    struct.pack_into("<HH", data, 132, machine, 1)
    struct.pack_into("<H", data, 148, 240)
    optional = 152
    struct.pack_into("<H", data, optional, 0x20B)
    struct.pack_into("<II", data, optional + 112 + 6 * 8, 0x1200, 56 if embedded else 28)
    if managed:
        struct.pack_into("<II", data, optional + 112 + 14 * 8, 0x1300, 72)
    section = optional + 240
    struct.pack_into("<IIII", data, section + 8, 2048, 0x1000, 2048, 512)
    codeview = b"RSDS" + bytes(20) + path.encode("utf-8") + b"\0"
    struct.pack_into("<IIII", data, 1024 + 12, 2, len(codeview), 0x1300, 1280)
    data[1280:1280 + len(codeview)] = codeview
    if embedded:
        struct.pack_into("<IIII", data, 1052 + 12, 17, 4, 0x1400, 1536)
    return bytes(data)


class ReleasePrivacyTests(unittest.TestCase):
    def scan(self, content, name="Wintodo.exe", markers=(), required=()):
        with tempfile.TemporaryDirectory() as directory:
            item = Path(directory) / name
            item.write_bytes(content)
            return privacy.scan_inputs([item], markers, required)

    def test_x64_and_arm64_actual_debug_directory(self):
        for machine, architecture in ((0x8664, "x64"), (0xAA64, "ARM64")):
            with self.subTest(architecture=architecture):
                result = self.scan(native_pe(machine=machine), required=[architecture])
                self.assertEqual("PASS", result["status"])
                self.assertEqual(architecture, result["first_party_binaries"][0]["architecture"])

    def test_codeview_absolute_path_rejected_even_without_pdb_file(self):
        result = self.scan(native_pe(r"C:\build\private-checkout\Wintodo.pdb"))
        self.assertEqual("FAIL", result["status"])
        self.assertIn("unsafe_first_party_codeview", [item["category"] for item in result["findings"]])
        self.assertNotIn("private-checkout", json.dumps(result))

    def test_codeview_relative_directory_rejected(self):
        self.assertEqual("FAIL", self.scan(native_pe(r"obj\Wintodo.pdb"))["status"])

    def test_managed_first_party_binary_rejected(self):
        self.assertEqual("FAIL", self.scan(native_pe(managed=True))["status"])

    def test_embedded_portable_pdb_rejected(self):
        self.assertEqual("FAIL", self.scan(native_pe(embedded=True))["status"])

    def test_malformed_pe_rejected(self):
        self.assertEqual("FAIL", self.scan(b"MZ")["status"])

    def test_unreferenced_rsds_cannot_substitute_for_debug_entry(self):
        content = bytearray(native_pe())
        struct.pack_into("<II", content, 152 + 112 + 6 * 8, 0, 0)
        self.assertEqual("FAIL", self.scan(bytes(content))["status"])

    def test_loose_symbols_and_build_recipe_rejected(self):
        for filename in ("Wintodo.pdb", "Wintodo.build.appxrecipe", "build.binlog"):
            with self.subTest(filename=filename):
                self.assertEqual("FAIL", self.scan(b"private", filename)["status"])

    def test_profile_paths_detected_in_utf8_utf16_and_json(self):
        for text in (r"C:\Users\synthetic-private\repo", "C:/Users/synthetic-private/repo", "/home/synthetic-private/repo"):
            for encoding in ("utf-8", "utf-16-le", "utf-16-be"):
                with self.subTest(text=text, encoding=encoding):
                    self.assertEqual("FAIL", self.scan(text.encode(encoding), "resources.pri")["status"])
            self.assertEqual("FAIL", self.scan(json.dumps({"path": text}).encode(), "summary.json")["status"])

    def test_private_marker_detected_across_stream_block_boundary(self):
        marker = "synthetic-private-marker"
        content = b"x" * (1024 * 1024 - 5) + marker.encode()
        self.assertEqual("FAIL", self.scan(content, "resources.pri", [marker])["status"])

    def test_vendor_build_paths_counted_without_identifying_false_claim(self):
        result = self.scan(native_pe(r"D:\a\_work\1\s\vendor.pdb"), "vendor.dll")
        self.assertEqual("PASS", result["status"])
        self.assertEqual(1, result["vendor_codeview_path_records"])

    def test_packaged_resources_and_symbols_scanned_without_extracting(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "test.msix"
            with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED) as package:
                package.writestr("Wintodo.exe", native_pe())
                package.writestr("Assets/private.pri", b"synthetic-sensitive-text")
                package.writestr("private.pdb", b"symbols")
            result = privacy.scan_inputs([path], ["synthetic-sensitive-text"])
            self.assertEqual("FAIL", result["status"])
            self.assertEqual(3, result["files_scanned"])
            self.assertNotIn("synthetic-sensitive-text", json.dumps(result))

    def test_archive_private_name_not_printed(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "test.msix"
            with zipfile.ZipFile(path, "w") as package:
                package.writestr("C:/Users/synthetic-private/private.txt", b"ordinary")
            result = privacy.scan_inputs([path])
            self.assertEqual("FAIL", result["status"])
            self.assertNotIn("synthetic-private", json.dumps(result))

    def test_missing_required_architecture_rejected(self):
        self.assertEqual("FAIL", self.scan(native_pe(), required=["ARM64"])["status"])

    def test_relative_public_summary_passes(self):
        content = json.dumps({"PublishDirectory": "win-x64"}).encode()
        self.assertEqual("PASS", self.scan(content, "summary.json")["status"])

    def test_output_write_failure_prints_only_safe_category(self):
        with tempfile.TemporaryDirectory() as directory:
            item = Path(directory) / "summary.json"
            item.write_text("{}")
            output = io.StringIO()
            with patch.object(Path, "write_text", side_effect=OSError(r"C:\Users\synthetic-private\blocked")):
                with contextlib.redirect_stdout(output):
                    result = privacy.main([str(item), "--output", str(Path(directory) / "result.json")])
            self.assertEqual(1, result)
            self.assertIn("output_write_failed", output.getvalue())
            self.assertNotIn("synthetic-private", output.getvalue())

    def test_encrypted_package_prints_only_safe_category(self):
        with tempfile.TemporaryDirectory() as directory:
            item = Path(directory) / "synthetic-private.msix"
            with zipfile.ZipFile(item, "w") as package:
                package.writestr("synthetic-private.txt", b"ordinary")
            data = bytearray(item.read_bytes())
            struct.pack_into("<H", data, data.index(b"PK\x03\x04") + 6, 1)
            struct.pack_into("<H", data, data.index(b"PK\x01\x02") + 8, 1)
            item.write_bytes(data)
            output = io.StringIO()
            result_path = Path(directory) / "result.json"
            with contextlib.redirect_stdout(output):
                result = privacy.main([str(item), "--output", str(result_path)])
            self.assertEqual(1, result)
            self.assertIn("unreadable_input", result_path.read_text())
            self.assertNotIn("synthetic-private", output.getvalue() + result_path.read_text())

    def test_corrupt_compression_prints_only_safe_category(self):
        with tempfile.TemporaryDirectory() as directory:
            item = Path(directory) / "synthetic-private.msix"
            name = "synthetic-private.txt"
            with zipfile.ZipFile(item, "w", zipfile.ZIP_DEFLATED) as package:
                package.writestr(name, b"ordinary" * 1024)
            data = bytearray(item.read_bytes())
            # BTYPE=3 is an invalid DEFLATE block, exercising zlib's real error.
            data[30 + len(name)] = 0x07
            item.write_bytes(data)
            output = io.StringIO()
            result_path = Path(directory) / "result.json"
            with contextlib.redirect_stdout(output):
                result = privacy.main([str(item), "--output", str(result_path)])
            self.assertEqual(1, result)
            self.assertIn("unreadable_input", result_path.read_text())
            self.assertNotIn("synthetic-private", output.getvalue() + result_path.read_text())


if __name__ == "__main__":
    unittest.main()
