"""Synthetic MSF7/PE binding and safe-output tests; no native build or UI."""

import contextlib
import importlib.util
import io
import json
import os
import struct
import tempfile
import unittest
import zipfile
from pathlib import Path
from unittest.mock import patch

spec = importlib.util.spec_from_file_location(
    "retained_code", Path(__file__).parents[2] / "scripts" / "Export-RetainedCodeEvidence.py")
retained = importlib.util.module_from_spec(spec)
spec.loader.exec_module(retained)
from test_release_privacy import native_pe

COMMIT = "a" * 40
GUID = bytes(range(16))


def pub(name=b"Microsoft_Data_Sqlite__synthetic_private", address=32, segment=1):
    body = struct.pack("<IIH", 2, address, segment) + name + b"\0"
    padding = b"\xf1" * (-(len(body) + 4) % 4)
    return struct.pack("<HH", len(body) + len(padding) + 2, 0x110E) + body + padding


def executable(machine=0x8664):
    data = bytearray(native_pe(machine=machine))
    data[1284:1300] = GUID
    struct.pack_into("<I", data, 1300, 1)
    struct.pack_into("<I", data, 392 + 36, 0x60000020)
    data[544:576] = bytes(range(32))
    return bytes(data)


def pdb_file(exe, symbols=None, guid=GUID, age=1, machine=0x8664, omap=False):
    info = struct.pack("<III", 20000404, 0, age) + guid
    dbi = bytearray(76)
    struct.pack_into("<iII", dbi, 0, -1, 19990903, age)
    struct.pack_into("<H", dbi, 20, 4)
    struct.pack_into("<I", dbi, 48, 12)
    struct.pack_into("<H", dbi, 58, machine)
    struct.pack_into("<6H", dbi, 64, 65535, 65535, 65535, 0 if omap else 65535, 65535, 5)
    streams = [b"", info, b"", bytes(dbi), pub() if symbols is None else symbols, exe[392:432]]
    pages = []
    next_page = 4
    for data in streams:
        numbers = list(range(next_page, next_page + (len(data) + 511) // 512))
        pages.append(numbers)
        next_page += len(numbers)
    directory = struct.pack("<I", len(streams)) + struct.pack("<6I", *(len(s) for s in streams))
    directory += b"".join(struct.pack("<" + "I" * len(p), *p) for p in pages)
    data = bytearray(next_page * 512)
    data[:32] = retained.MAGIC
    struct.pack_into("<6I", data, 32, 512, 1, next_page, len(directory), 0, 2)
    struct.pack_into("<I", data, 1024, 3)
    data[1536:1536 + len(directory)] = directory
    for value, numbers in zip(streams, pages):
        for index, page in enumerate(numbers):
            chunk = value[index * 512:(index + 1) * 512]
            data[page * 512:page * 512 + len(chunk)] = chunk
    return bytes(data)


class RetainedCodeTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.package = self.root / "input.msix"
        self.pdb = self.root / "private.pdb"
        self.output = self.root / "output.json"
        self.make_inputs()

    def make_inputs(self, exe=None, pdb=None):
        exe = executable() if exe is None else exe
        with zipfile.ZipFile(self.package, "w", zipfile.ZIP_DEFLATED) as archive:
            archive.writestr("Wintodo.exe", exe)
        self.pdb.write_bytes(pdb_file(exe) if pdb is None else pdb)

    def export(self, architecture="x64"):
        return retained.export_evidence(self.package, self.pdb, architecture, COMMIT,
                                        retained.digest(self.package), retained.digest(self.pdb))

    def run_main(self, output=None, package_hash=None):
        capture = io.StringIO()
        with contextlib.redirect_stdout(capture):
            code = retained.main([str(self.package), "--pdb", str(self.pdb), "--architecture", "x64",
                                  "--source-commit", COMMIT,
                                  "--expected-package-sha256", package_hash or retained.digest(self.package),
                                  "--expected-pdb-sha256", retained.digest(self.pdb),
                                  "--output", str(self.output if output is None else output)])
        return code, capture.getvalue()

    def test_positive_both_architectures_and_private_names_omitted(self):
        for architecture, machine in retained.MACHINES.items():
            exe = executable(machine)
            self.make_inputs(exe, pdb_file(exe, machine=machine))
            result = self.export(architecture)
            self.assertEqual("PASS", result["status"])
            self.assertEqual("INCOMPLETE", result["full_dependency_closure"])
            self.assertTrue(result["pdb_final_sections_match"])
            family = result["families"]["Microsoft_Data_Sqlite"]
            self.assertEqual(1, family["executable_symbol_records"])
            self.assertEqual(32, family["samples"][0]["code_bytes"])
            self.assertNotIn("synthetic_private", json.dumps(result))
            self.assertNotIn(str(self.root), json.dumps(result))

    def test_mismatched_guid_age_and_machine_rejected(self):
        exe = executable()
        for value in (pdb_file(exe, guid=b"x" * 16), pdb_file(exe, age=2), pdb_file(exe, machine=0xAA64)):
            self.make_inputs(exe, value)
            with self.assertRaises(ValueError):
                self.export()

    def test_requested_architecture_mismatch(self):
        with self.assertRaises(ValueError):
            self.export("ARM64")

    def test_omap_and_final_section_mismatch_rejected(self):
        exe = executable()
        self.make_inputs(exe, pdb_file(exe, omap=True))
        with self.assertRaises(ValueError):
            self.export()
        different = bytearray(exe)
        struct.pack_into("<I", different, 400, 2047)
        self.make_inputs(exe, pdb_file(bytes(different)))
        with self.assertRaises(ValueError):
            self.export()

    def test_unmapped_and_unknown_records_are_reported(self):
        exe = executable()
        symbols = pub() + pub(segment=7) + pub(address=999999) + struct.pack("<HH", 2, 0x9999)
        self.make_inputs(exe, pdb_file(exe, symbols))
        result = self.export()
        self.assertEqual(2, result["unmapped_public_symbol_records"])
        self.assertEqual(1, result["other_symbol_records"])
        self.assertEqual(1, result["executable_public_symbol_records"])

    def test_missing_family_is_not_removal_evidence(self):
        result = self.export()
        self.assertEqual(0, result["families"]["WinRT_Runtime"]["executable_symbol_records"])
        self.assertIn("does not prove removal", result["limitations"])
        self.assertEqual("PENDING", result["component_versions"])

    def test_truncated_symbol_and_missing_name_terminator_rejected(self):
        exe = executable()
        for symbols in (pub()[:-1], struct.pack("<HHIIH", 13, 0x110E, 2, 32, 1) + b"x"):
            self.make_inputs(exe, pdb_file(exe, symbols))
            with self.assertRaises((ValueError, struct.error)):
                self.export()

    def test_malformed_msf_directory_page_and_stream_bounds(self):
        exe = executable()
        for offset, value in ((32, 1), (40, 9999), (44, 0xFFFFFFFF), (1024, 9999), (1540 + 4, 0xFFFFFFFE)):
            data = bytearray(pdb_file(exe))
            struct.pack_into("<I", data, offset, value)
            self.make_inputs(exe, data)
            with self.assertRaises((ValueError, struct.error)):
                self.export()

    def test_input_size_limits(self):
        with patch.object(retained, "MAX_FILE", 32):
            with self.assertRaises(ValueError):
                self.export()
        with patch.object(retained, "MAX_EXE", 32):
            with self.assertRaises(ValueError):
                self.export()
        with patch.object(retained, "MAX_STREAM", 32):
            with self.assertRaises(ValueError):
                self.export()

    def test_oversized_pdb_rejected_before_hashing(self):
        bound = max(self.package.stat().st_size, self.pdb.stat().st_size)
        with self.pdb.open("ab") as stream:
            stream.truncate(bound + 1)
        with patch.object(retained, "MAX_FILE", bound), patch.object(retained, "digest") as digest:
            with self.assertRaises(ValueError):
                retained.export_evidence(self.package, self.pdb, "x64", COMMIT, "a" * 64, "b" * 64)
            digest.assert_not_called()

    def test_package_duplicate_and_unsafe_entry_rejected(self):
        for name in ("wintodo.EXE", "../private.exe"):
            self.make_inputs()
            with zipfile.ZipFile(self.package, "a") as archive:
                archive.writestr(name, executable())
            with self.assertRaises(ValueError):
                self.export()

    def test_success_and_stale_failure_are_atomic_sanitized_reports(self):
        self.assertEqual(0, self.run_main()[0])
        self.assertEqual("PASS", json.loads(self.output.read_text())["status"])
        code, diagnostics = self.run_main(package_hash="0" * 64)
        self.assertEqual(1, code)
        self.assertEqual("FAIL", json.loads(self.output.read_text())["status"])
        self.assertNotIn(str(self.root), diagnostics)
        self.assertNotIn("private.pdb", diagnostics)

    def test_alias_and_hardlink_inputs_preserved(self):
        before = self.pdb.read_bytes()
        self.assertEqual(1, self.run_main(output=self.pdb)[0])
        self.assertEqual(before, self.pdb.read_bytes())
        alias = self.root / "alias.json"
        os.link(self.pdb, alias)
        self.assertEqual(1, self.run_main(output=alias)[0])
        self.assertEqual(before, self.pdb.read_bytes())

    def test_late_input_mutation_rejected(self):
        original = retained.digest
        package_hash, pdb_hash = original(self.package), original(self.pdb)
        counts = {}
        def changed(path):
            key = Path(path)
            counts[key] = counts.get(key, 0) + 1
            return "0" * 64 if key == self.pdb and counts[key] > 1 else original(path)
        with patch.object(retained, "digest", side_effect=changed):
            with self.assertRaises(ValueError):
                retained.export_evidence(self.package, self.pdb, "x64", COMMIT, package_hash, pdb_hash)
        self.assertEqual(2, counts[self.package])
        self.assertEqual(2, counts[self.pdb])

    def test_failed_output_replace_cleans_temporary_file(self):
        for target, attribute in ((retained.os, "replace"), (retained.json, "dump")):
            with patch.object(target, attribute, side_effect=OSError("synthetic_private")):
                code, diagnostics = self.run_main()
            self.assertEqual(1, code)
            self.assertNotIn("synthetic_private", diagnostics)
            self.assertEqual({self.package, self.pdb}, set(self.root.iterdir()))

    def test_invalid_arguments_do_not_echo_private_values(self):
        capture = io.StringIO()
        with contextlib.redirect_stdout(capture), contextlib.redirect_stderr(capture):
            code = retained.main(["--architecture", "synthetic_private"])
        self.assertEqual(1, code)
        self.assertNotIn("synthetic_private", capture.getvalue())


if __name__ == "__main__":
    unittest.main()
