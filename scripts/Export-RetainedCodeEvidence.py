"""Export bounded positive native-PDB observations for an exact packaged EXE."""

import argparse
import hashlib
import json
import os
import re
import struct
import tempfile
import zipfile
import zlib
from pathlib import Path

MAGIC = b"Microsoft C/C++ MSF 7.00\r\n\x1aDS\0\0\0"
MACHINES = {"x64": 0x8664, "ARM64": 0xAA64}
MAX_FILE = 1024 * 1024 * 1024
MAX_STREAM = 128 * 1024 * 1024
MAX_EXE = 256 * 1024 * 1024
FAMILIES = ("Microsoft_Data_Sqlite", "SQLitePCLRaw_core", "SQLitePCLRaw_provider",
            "CommunityToolkit_Mvvm", "CommunityToolkit_WinUI", "Microsoft_Windows_SDK_NET",
            "WinRT_Runtime", "Rhp")
ERRORS = (OSError, ValueError, struct.error, zipfile.BadZipFile, RuntimeError,
          NotImplementedError, zlib.error)


class SafeArgumentParser(argparse.ArgumentParser):
    def error(self, message):
        raise ValueError("invalid_arguments")


def require(condition):
    if not condition:
        raise ValueError("invalid_input_or_output")


def digest(path):
    with Path(path).open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def part(data, offset, size):
    require(offset >= 0 and size >= 0 and offset + size <= len(data))
    return data[offset:offset + size]


class Msf:
    """Read only the MSF7 stream directory and requested bounded streams."""

    def __init__(self, stream):
        self.stream = stream
        size = os.fstat(stream.fileno()).st_size
        require(56 <= size <= MAX_FILE)
        require(self.read(0, 32) == MAGIC)
        block, _, count, directory_size, reserved, block_map = struct.unpack("<6I", self.read(32, 24))
        require(block in (512, 1024, 2048, 4096, 8192) and count * block == size
                and reserved == 0 and 4 <= directory_size <= MAX_STREAM)
        self.block = block
        self.count = count
        pages = (directory_size + block - 1) // block
        # This reader deliberately supports a single directory-block-map page.
        require(pages * 4 <= block and 0 < block_map < count)
        numbers = struct.unpack("<" + "I" * pages, self.read(block_map * block, pages * 4))
        directory = self.pages(numbers, directory_size)
        streams = struct.unpack_from("<I", directory)[0]
        require(4 <= streams <= 65536)
        sizes = struct.unpack("<" + "I" * streams, part(directory, 4, streams * 4))
        offset = 4 + streams * 4
        self.entries = []
        for length in sizes:
            require(length == 0xFFFFFFFF or length <= MAX_STREAM)
            pages = 0 if length == 0xFFFFFFFF else (length + block - 1) // block
            numbers = struct.unpack("<" + "I" * pages, part(directory, offset, pages * 4))
            require(len(set(numbers)) == len(numbers) and all(0 < number < count for number in numbers))
            self.entries.append((length, numbers))
            offset += pages * 4
        require(offset == len(directory))

    def read(self, offset, size):
        self.stream.seek(offset)
        value = self.stream.read(size)
        require(len(value) == size)
        return value

    def pages(self, numbers, length):
        require(len(set(numbers)) == len(numbers) and all(0 < number < self.count for number in numbers))
        return b"".join(self.read(number * self.block, self.block) for number in numbers)[:length]

    def get(self, index):
        require(0 <= index < len(self.entries))
        length, numbers = self.entries[index]
        require(length != 0xFFFFFFFF)
        return self.pages(numbers, length)


def read_executable(package):
    with zipfile.ZipFile(package) as archive:
        items = archive.infolist()
        require(len(items) <= 4096 and sum(item.file_size for item in items) <= MAX_FILE)
        names = set()
        executable = None
        for ordinal, item in enumerate(items):
            if item.is_dir():
                continue
            name = item.filename.replace("\\", "/").casefold()
            require(not name.startswith("/") and ":" not in name
                    and all(p not in ("", ".", "..") for p in name.split("/"))
                    and name not in names and not item.flag_bits & 1 and item.file_size <= MAX_EXE)
            names.add(name)
            if name == "wintodo.exe":
                with archive.open(item) as stream:
                    executable = (ordinal, stream.read(MAX_EXE + 1))
                require(len(executable[1]) == item.file_size)
        require(executable is not None)
        return executable


def executable_info(data, architecture):
    require(part(data, 0, 2) == b"MZ")
    header = struct.unpack("<I", part(data, 60, 4))[0]
    require(part(data, header, 4) == b"PE\0\0")
    machine, count = struct.unpack("<HH", part(data, header + 4, 4))
    length = struct.unpack("<H", part(data, header + 20, 2))[0]
    optional = part(data, header + 24, length)
    require(machine == MACHINES[architecture] and 0 < count <= 96
            and struct.unpack("<H", part(optional, 0, 2))[0] == 0x20B)
    require(struct.unpack("<II", part(optional, 112 + 14 * 8, 8)) == (0, 0))
    table = part(data, header + 24 + length, count * 40)
    sections = []
    for index in range(count):
        entry = table[index * 40:(index + 1) * 40]
        virtual_size, rva, raw_size, raw = struct.unpack_from("<4I", entry, 8)
        flags = struct.unpack_from("<I", entry, 36)[0]
        require(raw + raw_size <= len(data))
        sections.append((rva, virtual_size, raw, raw_size, flags))
    debug_rva, debug_size = struct.unpack("<II", part(optional, 112 + 6 * 8, 8))
    require(0 < debug_size <= 28 * 1024 and debug_size % 28 == 0)
    containing = [s for s in sections if s[0] <= debug_rva and debug_rva + debug_size <= s[0] + s[3]]
    require(len(containing) == 1)
    section = containing[0]
    debug = part(data, section[2] + debug_rva - section[0], debug_size)
    codeviews = []
    for index in range(0, debug_size, 28):
        kind, size, _, raw = struct.unpack_from("<4I", debug, index + 12)
        require(kind != 17)  # Embedded portable symbols are not the native-PDB input.
        if kind == 2:
            require(25 <= size <= 65536)
            value = part(data, raw, size)
            require(value[:4] == b"RSDS" and value[24:] == b"Wintodo.pdb\0")
            codeviews.append((value[4:20], struct.unpack_from("<I", value, 20)[0]))
    require(len(codeviews) == 1 and codeviews[0][1] > 0)
    return sections, codeviews[0]


def observe_symbols(symbols, sections, executable):
    result = {family: {"executable_symbol_records": 0, "samples": []} for family in FAMILIES}
    offset = 0
    records = public = executable_public = unmapped = 0
    while offset < len(symbols):
        length, kind = struct.unpack("<HH", part(symbols, offset, 4))
        require(length >= 2)
        record = part(symbols, offset, length + 2)
        records += 1
        require(records <= 4000000)
        if kind == 0x110E:  # S_PUB32: flags, section-relative offset, segment, NUL name.
            require(length >= 13)
            _, address, segment = struct.unpack_from("<IIH", record, 4)
            name = record[14:]
            end = name.find(b"\0")
            require(end >= 0)
            name = name[:end]
            public += 1
            if not 0 < segment <= len(sections):
                unmapped += 1
            else:
                rva, _, raw, size, flags = sections[segment - 1]
                if address >= size:
                    unmapped += 1
                if flags & 0x20000000 and address < size:
                    executable_public += 1
                    code = part(executable, raw + address, min(32, size - address))
                    for family in FAMILIES:
                        token = family.encode("ascii")
                        if (name.startswith(token) if family == "Rhp" else token in name):
                            entry = result[family]
                            entry["executable_symbol_records"] += 1
                            if len(entry["samples"]) < 2:
                                entry["samples"].append({"segment": segment, "offset": address,
                                                         "rva": rva + address, "code_bytes": len(code),
                                                         "code_sha256": hashlib.sha256(code).hexdigest()})
        offset += length + 2
    require(offset == len(symbols) and public > 0)
    return result, records, public, executable_public, unmapped


def check_pdb_sections(msf, dbi, sections):
    offset = 64 + sum(struct.unpack_from("<I", dbi, p)[0] for p in (24, 28, 32, 36, 40, 52))
    size = struct.unpack_from("<I", dbi, 48)[0]
    require(12 <= size <= 64 and size % 2 == 0 and offset + size == len(dbi))
    indices = struct.unpack("<" + "H" * (size // 2), part(dbi, offset, size))
    require(indices[3] == indices[4] == 0xFFFF and indices[5] != 0xFFFF)
    table = msf.get(indices[5])
    require(len(table) == len(sections) * 40)
    for index, section in enumerate(sections):
        entry = table[index * 40:(index + 1) * 40]
        virtual, rva, raw_size, raw = struct.unpack_from("<4I", entry, 8)
        flags = struct.unpack_from("<I", entry, 36)[0]
        require(section == (rva, virtual, raw, raw_size, flags))


def export_evidence(package, pdb, architecture, source_commit, package_hash, pdb_hash):
    require(architecture in MACHINES and re.fullmatch(r"[a-f0-9]{40}", source_commit or "")
            and source_commit != "0" * 40 and re.fullmatch(r"[a-f0-9]{64}", package_hash or "")
            and re.fullmatch(r"[a-f0-9]{64}", pdb_hash or ""))
    require(Path(package).stat().st_size <= MAX_FILE and 56 <= Path(pdb).stat().st_size <= MAX_FILE)
    require(digest(package) == package_hash and digest(pdb) == pdb_hash)
    ordinal, executable = read_executable(package)
    sections, (guid, age) = executable_info(executable, architecture)
    with Path(pdb).open("rb") as stream:
        msf = Msf(stream)
        info = msf.get(1)
        require(len(info) >= 28 and struct.unpack_from("<I", info)[0] == 20000404
                and struct.unpack_from("<I", info, 8)[0] == age and info[12:28] == guid)
        dbi = msf.get(3)
        require(len(dbi) >= 64 and struct.unpack_from("<i", dbi)[0] == -1
                and struct.unpack_from("<I", dbi, 4)[0] == 19990903
                and struct.unpack_from("<I", dbi, 8)[0] == age
                and struct.unpack_from("<H", dbi, 58)[0] == MACHINES[architecture])
        symbol_stream = struct.unpack_from("<H", dbi, 20)[0]
        require(symbol_stream not in (0, 1, 3, 0xFFFF))
        check_pdb_sections(msf, dbi, sections)
        families, records, public, executable_public, unmapped = observe_symbols(msf.get(symbol_stream), sections, executable)
    require(digest(package) == package_hash and digest(pdb) == pdb_hash)
    return {"status": "PASS", "scope": "positive_native_public_symbol_observations", "findings": [],
            "source_commit": source_commit, "source_binding": "caller_declared; not build attestation",
            "architecture": architecture, "package_sha256": package_hash, "package_executable_entry": ordinal,
            "executable_sha256": hashlib.sha256(executable).hexdigest(), "pdb_sha256": pdb_hash,
            "codeview_pdb_match": True, "codeview_guid_bytes": guid.hex(), "codeview_age": age,
            "symbol_stream_complete": True, "parsed_symbol_records": records, "public_symbol_records": public,
            "supported_symbol_kind": "S_PUB32", "other_symbol_records": records - public,
            "pdb_final_sections_match": True, "address_mapping": "direct_final_sections; no OMAP",
            "unmapped_public_symbol_records": unmapped,
            "executable_public_symbol_records": executable_public, "families": families,
            "full_dependency_closure": "INCOMPLETE", "component_versions": "PENDING",
            "vendor_embedded_components": "PENDING", "build_attestation": "PENDING",
            "limitations": "Fixed token observations in address-bearing public symbols. Counts may overlap and include compiler-generated code; they are not unique methods or code sizes. No observation does not prove removal. PDB binding does not authenticate inputs, versions, source ownership or tool execution. Full retained-code, vendor, licence and deployment closure remains separate."}


def write_report(output, report):
    output.parent.mkdir(parents=True, exist_ok=True)
    temporary = None
    try:
        with tempfile.NamedTemporaryFile(mode="w", encoding="utf-8", dir=output.parent, delete=False) as stream:
            temporary = Path(stream.name)
            json.dump(report, stream, indent=2)
            stream.write("\n")
        os.replace(temporary, output)
    finally:
        if temporary is not None:
            temporary.unlink(missing_ok=True)


def main(argv=None):
    parser = SafeArgumentParser(prog="Export-RetainedCodeEvidence.py", description=__doc__)
    parser.add_argument("package")
    parser.add_argument("--pdb", required=True)
    parser.add_argument("--architecture", required=True, choices=tuple(MACHINES))
    parser.add_argument("--source-commit", required=True)
    parser.add_argument("--expected-package-sha256", required=True)
    parser.add_argument("--expected-pdb-sha256", required=True)
    parser.add_argument("--output", required=True)
    try:
        args = parser.parse_args(argv)
    except ValueError:
        print(json.dumps({"status": "FAIL", "category": "invalid_arguments"}))
        return 1
    output = Path(args.output)
    safe_output = False
    try:
        require(output.suffix.lower() == ".json" and not (output.exists() and output.stat().st_nlink > 1))
        require(all(output.resolve() != Path(p).resolve()
                    and not (output.exists() and Path(p).exists() and output.samefile(p)) for p in (args.package, args.pdb)))
        safe_output = True
        report = export_evidence(args.package, args.pdb, args.architecture, args.source_commit,
                                 args.expected_package_sha256, args.expected_pdb_sha256)
        write_report(output, report)
    except ERRORS:
        if safe_output:
            try:
                write_report(output, {"status": "FAIL", "findings": [{"category": "invalid_input_or_output"}]})
            except ERRORS:
                pass
        print(json.dumps({"status": "FAIL", "category": "invalid_input_or_output"}))
        return 1
    print(json.dumps({"status": "PASS", "full_dependency_closure": "INCOMPLETE",
                      "observed_families": sum(f["executable_symbol_records"] > 0 for f in report["families"].values())}))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
