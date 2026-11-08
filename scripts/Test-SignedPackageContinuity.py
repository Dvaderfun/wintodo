"""Compare hash-pinned MSIX payloads across signing; never verify trust or release."""

import argparse
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import re
import stat
import tempfile
import xml.etree.ElementTree as ET
import zipfile

spec = importlib.util.spec_from_file_location("release_package", Path(__file__).with_name("Test-ReleasePackage.py"))
inspector = importlib.util.module_from_spec(spec)
spec.loader.exec_module(inspector)

MAX_PACKAGE = 1024 * 1024 * 1024
MAX_ENTRY = 256 * 1024 * 1024
MAX_XML = 1024 * 1024
MAX_FOOTPRINT = 16 * 1024 * 1024
CT_NS = "http://schemas.openxmlformats.org/package/2006/content-types"
SIGNATURE = "AppxSignature.p7x"
CATALOG = "AppxMetadata/CodeIntegrity.cat"
SIGNING_TYPES = {SIGNATURE: "application/vnd.ms-appx.signature", CATALOG: "application/vnd.ms-pkiseccat"}


class InvalidContinuity(Exception):
    pass


def require(condition, category):
    if not condition:
        raise InvalidContinuity(category)


def valid_hash(value):
    return isinstance(value, str) and re.fullmatch(r"[0-9a-f]{64}", value) is not None


def safe_name(name):
    require(isinstance(name, str) and 0 < len(name) <= 1024
            and not any(ord(c) < 32 or c in '\\:<>"|?*' for c in name), "unsafe_package_name")
    parts = name.split("/")
    require(all(p and p not in (".", "..") and p == p.rstrip(" .") for p in parts), "unsafe_package_name")
    return name.casefold()


def stream_hash(stream):
    stream.seek(0)
    digest = hashlib.sha256()
    size = 0
    while block := stream.read(1024 * 1024):
        size += len(block)
        require(size <= MAX_PACKAGE, "oversized_package")
        digest.update(block)
    require(size > 0, "empty_package")
    return digest.hexdigest()


def entries(package):
    items = package.infolist()
    require(0 < len(items) <= 4096 and sum(i.file_size for i in items) <= MAX_PACKAGE, "package_bounds_exceeded")
    result = {}
    for item in items:
        require(item.orig_filename == item.filename, "unsafe_package_name")
        name = safe_name(item.filename)
        require(name not in result, "duplicate_package_name")
        mode = stat.S_IFMT(item.external_attr >> 16)
        require(not item.is_dir() and mode in (0, stat.S_IFREG), "unsupported_entry_type")
        require(0 <= item.file_size <= MAX_ENTRY and not item.flag_bits & 1
                and item.compress_type in (zipfile.ZIP_STORED, zipfile.ZIP_DEFLATED), "unsupported_or_oversized_entry")
        result[name] = item
    for name in ("AppxManifest.xml", "AppxBlockMap.xml", "[Content_Types].xml", "Wintodo.exe"):
        require(name.casefold() in result, "required_entry_missing")
    return result


def read_small(package, item, maximum):
    require(item.file_size <= maximum, "oversized_metadata")
    with package.open(item) as stream:
        data = stream.read(maximum + 1)
    require(len(data) == item.file_size and len(data) <= maximum, "entry_length_mismatch")
    return data


def xml_root(data):
    require(b"<!DOCTYPE" not in data.upper() and b"<!ENTITY" not in data.upper(), "unsupported_xml_declaration")
    # The supported MSIX footprint XML is UTF-8; reject alternate encodings that
    # could hide declarations from the bounded byte-level check.
    try:
        text = data.decode("utf-8-sig")
        require("\x00" not in text, "unsupported_xml_encoding")
        return ET.fromstring(text)
    except (UnicodeError, ET.ParseError):
        raise InvalidContinuity("invalid_xml") from None


def content_types(data):
    root = xml_root(data)
    require(all(uri == CT_NS for _, (_, uri) in ET.iterparse(io.BytesIO(data), events=("start-ns",))), "invalid_content_types")
    require(root.tag == "{" + CT_NS + "}Types" and not root.attrib
            and not (root.text or "").strip(), "invalid_content_types")
    mapping = {}
    for child in root:
        if child.tag == "{" + CT_NS + "}Default":
            require(set(child.attrib) == {"Extension", "ContentType"}, "invalid_content_types")
            part = child.attrib["Extension"]
            require(re.fullmatch(r"[A-Za-z0-9]+", part) is not None, "invalid_content_types")
            key = ("Default", part.casefold())
        elif child.tag == "{" + CT_NS + "}Override":
            require(set(child.attrib) == {"PartName", "ContentType"}, "invalid_content_types")
            part = child.attrib["PartName"]
            require(part.startswith("/") and len(part) > 1, "invalid_content_types")
            key = ("Override", safe_name(part[1:]))
        else:
            raise InvalidContinuity("invalid_content_types")
        require(key not in mapping and len(child) == 0 and not (child.text or "").strip()
                and not (child.tail or "").strip() and child.attrib["ContentType"], "invalid_content_types")
        mapping[key] = dict(child.attrib)
    require(mapping, "invalid_content_types")
    return mapping


def entry_hash(package, item):
    digest = hashlib.sha256()
    count = 0
    with package.open(item) as stream:
        while block := stream.read(1024 * 1024):
            count += len(block)
            require(count <= item.file_size and count <= MAX_ENTRY, "entry_length_mismatch")
            digest.update(block)
    require(count == item.file_size, "entry_length_mismatch")
    return digest.hexdigest()


def inspect_continuity(unsigned, signed, architecture, unsigned_sha256, signed_sha256):
    require(architecture in inspector.MACHINES and valid_hash(unsigned_sha256) and valid_hash(signed_sha256), "invalid_inputs")
    unsigned, signed = Path(unsigned), Path(signed)
    require(unsigned.suffix.lower() == ".msix" and signed.suffix.lower() == ".msix", "invalid_package_extension")
    require(not os.path.samefile(unsigned, signed), "same_package_input")
    with unsigned.open("rb") as ustream, signed.open("rb") as sstream:
        identities = [(os.fstat(s.fileno()).st_dev, os.fstat(s.fileno()).st_ino) for s in (ustream, sstream)]
        require(stream_hash(ustream) == unsigned_sha256 and stream_hash(sstream) == signed_sha256, "input_hash_mismatch")
        with zipfile.ZipFile(ustream) as up, zipfile.ZipFile(sstream) as sp:
            ue, se = entries(up), entries(sp)
            require(SIGNATURE.casefold() not in ue, "unsigned_input_contains_signature")
            require(SIGNATURE.casefold() in se and 0 < se[SIGNATURE.casefold()].file_size <= MAX_FOOTPRINT, "missing_or_empty_signature")
            additions = set(se) - set(ue)
            require(not set(ue) - set(se) and additions <= {n.casefold() for n in SIGNING_TYPES}, "unexpected_entry_set_change")
            require(all(ue[n].filename == se[n].filename for n in ue), "entry_name_changed")
            for name in additions:
                require(se[name].filename in SIGNING_TYPES and 0 < se[name].file_size <= MAX_FOOTPRINT, "invalid_signing_footprint")
            for name, item in ue.items():
                if name != "[content_types].xml":
                    require(item.file_size == se[name].file_size and entry_hash(up, item) == entry_hash(sp, se[name]), "original_entry_changed")
            for package, index in ((up, ue), (sp, se)):
                manifest = xml_root(read_small(package, index["appxmanifest.xml"], MAX_XML))
                identities_xml = manifest.findall("f:Identity", inspector.NS)
                require(manifest.tag == "{" + inspector.NS["f"] + "}Package" and len(identities_xml) == 1, "invalid_manifest_identity")
                identity = identities_xml[0]
                require(identity.get("ProcessorArchitecture") == architecture.lower(), "manifest_architecture_mismatch")
                inspector.version(identity.get("Version"))
                require(identity.get("Name") and identity.get("Publisher"), "invalid_manifest_identity")
            original_types = content_types(read_small(up, ue["[content_types].xml"], MAX_XML))
            signed_types = content_types(read_small(sp, se["[content_types].xml"], MAX_XML))
            expected_types = dict(original_types)
            for name, value in SIGNING_TYPES.items():
                key = ("Override", name.casefold())
                declaration = {"PartName": "/" + name, "ContentType": value}
                if name.casefold() in se:
                    require(key not in expected_types or expected_types[key] == declaration, "invalid_signing_content_type")
                    expected_types[key] = declaration
            require(signed_types == expected_types, "content_types_changed_beyond_signing")
            for name in additions:
                entry_hash(sp, se[name])  # Read to EOF to check length, decompression and CRC.
            executable_hash = entry_hash(up, ue["wintodo.exe"])
        require(stream_hash(ustream) == unsigned_sha256 and stream_hash(sstream) == signed_sha256, "input_changed_during_verification")
        for path, observed in zip((unsigned, signed), identities):
            current = path.stat()
            require((current.st_dev, current.st_ino) == observed, "input_changed_during_verification")
    return {"status": "PASS", "scope": "signed_package_payload_continuity", "release_authorized": False,
            "architecture": architecture, "unsigned_package_sha256": unsigned_sha256,
            "signed_package_sha256": signed_sha256, "executable_sha256": executable_hash,
            "original_entries_unchanged": len(ue) - 1, "content_types": "SEMANTIC_PRESERVATION_WITH_SIGNING_OVERRIDES",
            "added_signature": True, "added_code_integrity_catalog": CATALOG.casefold() in additions,
            "signature_trust": "PENDING", "timestamp_policy": "PENDING", "source_provenance": "PENDING",
            "deployment_acceptance": "PENDING", "findings": []}


def safe_output(output, inputs):
    output = Path(output)
    require(output.parent.is_dir(), "output_parent_missing")
    for path in inputs:
        path = Path(path)
        require(output.resolve() != path.resolve(), "output_aliases_input")
        if output.exists() and path.exists():
            require(not os.path.samefile(output, path), "output_aliases_input")
    return output


def write_report(output, result, inputs):
    output = safe_output(output, inputs)
    temporary = None
    try:
        with tempfile.NamedTemporaryFile(mode="w", encoding="utf-8", dir=output.parent, delete=False) as stream:
            temporary = Path(stream.name)
            json.dump(result, stream, indent=2)
            stream.write("\n")
        safe_output(output, inputs)
        os.replace(temporary, output)
    finally:
        if temporary is not None and temporary.exists():
            temporary.unlink()


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("unsigned")
    parser.add_argument("signed")
    parser.add_argument("--architecture", required=True)
    parser.add_argument("--unsigned-sha256", required=True)
    parser.add_argument("--signed-sha256", required=True)
    parser.add_argument("--output")
    args = parser.parse_args(argv)
    inputs = (args.unsigned, args.signed)
    try:
        if args.output:
            safe_output(args.output, inputs)
        result = inspect_continuity(*inputs, args.architecture, args.unsigned_sha256, args.signed_sha256)
    except Exception as error:
        category = str(error) if isinstance(error, InvalidContinuity) else "unreadable_or_invalid_package"
        result = {"status": "FAIL", "scope": "signed_package_payload_continuity", "release_authorized": False,
                  "findings": [{"category": category}]}
    if args.output:
        try:
            write_report(args.output, result, inputs)
        except Exception:
            result = {"status": "FAIL", "scope": "signed_package_payload_continuity", "release_authorized": False,
                      "findings": [{"category": "report_output_rejected_or_unwritable"}]}
    print(json.dumps(result, indent=2))
    return 0 if result["status"] == "PASS" else 1


if __name__ == "__main__":
    raise SystemExit(main())
