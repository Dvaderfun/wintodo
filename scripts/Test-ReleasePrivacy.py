"""Scan AOT layouts/MSIX/public metadata without printing private matches.

Uses only Python's standard library. Private PDBs and build logs are deliberately
outside these inputs; this is an artifact gate, not a repository secret scanner.
"""

import argparse
import hashlib
import io
import json
import re
import struct
import sys
import zipfile
import zlib
from pathlib import Path

PROFILE_PATH = re.compile(r"(?:[a-z]:[\\/]+users[\\/]+|/home/|/users/)", re.IGNORECASE)
# Only this literal, case-sensitive ASCII sequence occurs in the reviewed signed DLLs.
VENDOR_PREFIX = b"C:\\Users\\ContainerAdministrator\\.nuget\\packages\\"
ARCHITECTURES = {0x8664: "x64", 0xAA64: "ARM64"}


def reviewed_vendor_entries():
    # The checked-in manifest binds each output name and digest to its exact signed
    # NuGet archive/version and source entry. Do not learn exceptions from the input.
    manifest = Path(__file__).with_name("windowsappsdk-2.7.0-vendor-profile.json")
    return {(name, arch, digest): (origin["package"], origin["version"])
            for origin in json.loads(manifest.read_text(encoding="utf-8"))
            for name, targets in origin["files"].items() for arch, digest in targets.items()}


def read_at(stream, offset, length):
    stream.seek(offset)
    value = stream.read(length)
    if len(value) != length:
        raise ValueError("truncated_pe")
    return value


def inspect_pe(stream):
    """Read actual PE debug-directory entries, rather than searching for RSDS."""
    if read_at(stream, 0, 2) != b"MZ":
        raise ValueError("invalid_pe")
    pe_offset = struct.unpack("<I", read_at(stream, 60, 4))[0]
    header = read_at(stream, pe_offset, 24)
    if header[:4] != b"PE\0\0":
        raise ValueError("invalid_pe")
    machine, sections = struct.unpack_from("<HH", header, 4)
    optional_length = struct.unpack_from("<H", header, 20)[0]
    optional = read_at(stream, pe_offset + 24, optional_length)
    magic = struct.unpack_from("<H", optional)[0]
    directories = {0x10B: 96, 0x20B: 112}.get(magic)
    if directories is None or len(optional) < directories + 15 * 8:
        raise ValueError("invalid_pe")
    debug_rva, debug_size = struct.unpack_from("<II", optional, directories + 6 * 8)
    clr_rva, clr_size = struct.unpack_from("<II", optional, directories + 14 * 8)
    section_table = read_at(stream, pe_offset + 24 + optional_length, sections * 40)

    def raw_offset(rva):
        for index in range(sections):
            virtual_size, virtual_address, raw_size, raw_pointer = struct.unpack_from(
                "<IIII", section_table, index * 40 + 8
            )
            if virtual_address <= rva < virtual_address + max(virtual_size, raw_size):
                return raw_pointer + rva - virtual_address
        raise ValueError("invalid_debug_rva")

    paths = []
    embedded_pdb = False
    if debug_size:
        if debug_size % 28 or debug_size > 28 * 1024:
            raise ValueError("invalid_debug_directory")
        debug = read_at(stream, raw_offset(debug_rva), debug_size)
        for index in range(0, debug_size, 28):
            kind, size, _, pointer = struct.unpack_from("<IIII", debug, index + 12)
            if kind == 17:
                embedded_pdb = True
            if kind != 2:
                continue
            if size < 25 or size > 65536:
                raise ValueError("invalid_codeview")
            data = read_at(stream, pointer, size)
            if data[:4] != b"RSDS" or data[-1] != 0:
                raise ValueError("unsupported_codeview")
            paths.append(data[24:-1].decode("utf-8", errors="strict"))
    return {
        "machine": machine,
        "managed": bool(clr_rva or clr_size),
        "codeview_paths": paths,
        "embedded_pdb": embedded_pdb,
    }


def scan_stream(stream, markers):
    digest = hashlib.sha256()
    count = 0
    categories = set()
    carry = b""
    # Preserve enough overlap for all tested markers, UTF-16 and path prefixes.
    overlap = max(4096, max((len(marker) * 4 for marker in markers), default=0))
    while block := stream.read(1024 * 1024):
        digest.update(block)
        count += len(block)
        data = carry + block
        texts = [data.decode("utf-8", errors="ignore")]
        for shift in (0, 1):
            texts.append(data[shift:].decode("utf-16-le", errors="ignore"))
            texts.append(data[shift:].decode("utf-16-be", errors="ignore"))
        for value in texts:
            value = value.casefold()
            if PROFILE_PATH.search(value):
                categories.add("identifying_profile_path")
            if any(marker in value for marker in markers):
                categories.add("private_marker")
        carry = data[-overlap:]
    return digest.hexdigest(), count, categories


def marker_variants(values):
    variants = set()
    for value in values:
        if not value:
            continue
        value = value.casefold()
        variants.update((value, value.replace("\\", "/"), value.replace("\\", "\\\\")))
    return sorted(variants)


def scan_inputs(paths, private_markers=(), required_architectures=()):
    markers = marker_variants(private_markers)
    reviewed = reviewed_vendor_entries()
    findings = []
    records = []
    first_party = []
    vendor_path_records = 0
    informational = []

    def inspect(stream, name, input_index, entry_index):
        nonlocal vendor_path_records
        sha256, size, categories = scan_stream(stream, markers)
        suffix = Path(name).suffix.lower()
        if suffix in (".pdb", ".appxrecipe", ".binlog"):
            categories.add("private_build_artifact")
        if suffix in (".dll", ".exe"):
            try:
                pe = inspect_pe(stream)
                if Path(name).name.casefold() == "wintodo.exe":
                    if pe["machine"] not in (0x8664, 0xAA64) or pe["managed"]:
                        categories.add("invalid_first_party_native_binary")
                    if pe["codeview_paths"] != ["Wintodo.pdb"]:
                        categories.add("unsafe_first_party_codeview")
                    if pe["embedded_pdb"]:
                        categories.add("embedded_first_party_symbols")
                    first_party.append({
                        "input": input_index,
                        "entry": entry_index,
                        "architecture": {0x8664: "x64", 0xAA64: "ARM64"}.get(pe["machine"], "unsupported"),
                        "sha256": sha256,
                        "codeview_basename_only": pe["codeview_paths"] == ["Wintodo.pdb"],
                    })
                else:
                    vendor_path_records += sum(
                        "/" in value or "\\" in value for value in pe["codeview_paths"]
                    )
                    arch = ARCHITECTURES.get(pe["machine"], "unsupported")
                    source = reviewed.get((name, arch.lower(), sha256))
                    if (source and "identifying_profile_path" in categories
                            and (not required_architectures or arch in required_architectures)):
                        # All hash-pinned files are <4 MiB. Mask only the known bytes
                        # in a separate profile check. Never mask supplied markers.
                        original = read_at(stream, 0, size)
                        if VENDOR_PREFIX in original and hashlib.sha256(original).hexdigest() == sha256:
                            masked = original.replace(VENDOR_PREFIX, bytes(len(VENDOR_PREFIX)))
                            _, _, remaining = scan_stream(io.BytesIO(masked), ())
                            informational.append({"input": input_index, "entry": entry_index,
                                                  "category": "verified_vendor_build_profile", "architecture": arch,
                                                  "package": source[0], "version": source[1]})
                            if "identifying_profile_path" not in remaining:
                                categories.remove("identifying_profile_path")
            except (ValueError, UnicodeError, struct.error, OverflowError):
                categories.add("unreadable_pe")
        records.append({"input": input_index, "entry": entry_index, "bytes": size, "sha256": sha256})
        # Ordinal locations keep malicious/private filenames out of diagnostics.
        for category in sorted(categories):
            findings.append({"input": input_index, "entry": entry_index, "category": category})

    for input_index, path in enumerate(paths):
        path = Path(path)
        if path.is_dir():
            files = sorted(item for item in path.rglob("*") if item.is_file())
            for entry_index, item in enumerate(files):
                with item.open("rb") as stream:
                    inspect(stream, item.relative_to(path).as_posix(), input_index, entry_index)
        elif path.suffix.lower() in (".msix", ".appx"):
            with zipfile.ZipFile(path) as package:
                for entry_index, item in enumerate(package.infolist()):
                    if item.is_dir():
                        continue
                    if PROFILE_PATH.search(item.filename) or any(marker in item.filename.casefold() for marker in markers):
                        findings.append({"input": input_index, "entry": entry_index, "category": "private_archive_name"})
                    with package.open(item) as stream:
                        inspect(stream, item.filename.replace("\\", "/"), input_index, entry_index)
            with path.open("rb") as stream:
                records.append({"input": input_index, "package_sha256": hashlib.file_digest(stream, "sha256").hexdigest()})
        else:
            with path.open("rb") as stream:
                inspect(stream, path.name, input_index, 0)
    for architecture in required_architectures:
        if not any(record["architecture"] == architecture for record in first_party):
            findings.append({"category": "missing_required_architecture", "architecture": architecture})
    return {
        "status": "FAIL" if findings else "PASS",
        "files_scanned": sum("entry" in record for record in records),
        "findings": findings,
        "informational": informational,
        "first_party_binaries": first_party,
        "vendor_codeview_path_records": vendor_path_records,
        "files": records,
        "limits": "Identifying profile paths and supplied markers; exact reviewed vendor build profiles and vendor CodeView paths reported separately. Private symbols/logs are excluded inputs. Final signed packages must be scanned separately.",
    }


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("inputs", nargs="+")
    parser.add_argument("--output", required=True)
    parser.add_argument("--private-root", action="append", default=[])
    parser.add_argument("--marker-file", help="Private JSON array of synthetic markers; values are never printed.")
    parser.add_argument("--require-architecture", action="append", choices=("x64", "ARM64"), default=[])
    args = parser.parse_args(argv)
    try:
        markers = args.private_root
        if args.marker_file:
            values = json.loads(Path(args.marker_file).read_text(encoding="utf-8-sig"))
            if not isinstance(values, list) or not all(isinstance(value, str) for value in values):
                raise ValueError("invalid_markers")
            markers += values
        result = scan_inputs(args.inputs, markers, args.require_architecture)
    except (OSError, ValueError, zipfile.BadZipFile, RuntimeError, NotImplementedError, zlib.error):
        result = {"status": "FAIL", "findings": [{"category": "unreadable_input"}]}
    try:
        output = Path(args.output)
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    except OSError:
        print(json.dumps({"status": "FAIL", "category": "output_write_failed"}))
        return 1
    print(json.dumps({"status": result["status"], "files_scanned": result.get("files_scanned", 0), "findings": len(result["findings"])}))
    return 0 if result["status"] == "PASS" else 1


if __name__ == "__main__":
    sys.exit(main())
