"""Export a qualified CycloneDX 1.6 BOM from exact MSIX and original NuGet bytes.

This proves file/package hash ownership, not Native AOT retained-code closure.
The caller supplies source attribution; this exporter does not attest a build.
"""

import argparse
import base64
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import re
import struct
import tempfile
from urllib.parse import quote
import xml.etree.ElementTree as ET
import zipfile
import zlib

spec = importlib.util.spec_from_file_location("release_package", Path(__file__).with_name("Test-ReleasePackage.py"))
inspector = importlib.util.module_from_spec(spec)
spec.loader.exec_module(inspector)

MAX_JSON = 32 * 1024 * 1024
MAX_ARCHIVE_BYTES = 2 * 1024 * 1024 * 1024
MAX_MEMBER_BYTES = 512 * 1024 * 1024
BUILD_TOOLS = {"microsoft.dotnet.ilcompiler", "microsoft.net.illink.tasks", "runtime.win-x64.microsoft.dotnet.ilcompiler",
               "runtime.win-arm64.microsoft.dotnet.ilcompiler", "microsoft.windows.sdk.buildtools",
               "microsoft.windows.sdk.buildtools.msix", "microsoft.windows.sdk.buildtools.winapp",
               "microsoft.windows.sdk.buildtools.winuiAnalyzer".lower()}
ERRORS = (OSError, ValueError, KeyError, TypeError, UnicodeError, ET.ParseError,
          zipfile.BadZipFile, RuntimeError, NotImplementedError, struct.error, zlib.error)


def digest(path, algorithm="sha256"):
    with Path(path).open("rb") as stream:
        return hashlib.file_digest(stream, algorithm).digest()


def properties(**values):
    return [{"name": "wintodo:" + key.replace("_", "-"), "value": str(value)}
            for key, value in values.items()]


def package_key(value):
    if not isinstance(value, str) or not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9_.-]{0,99}/[0-9]+(?:\.[0-9]+){1,3}(?:-[A-Za-z0-9.-]+)?(?:\+[A-Za-z0-9.-]+)?", value):
        raise ValueError("invalid_package_identifier")
    return tuple(value.split("/"))


def read_graph_and_roots(assets, roots=None):
    assets = Path(assets)
    with assets.open("rb") as stream:
        data = stream.read(MAX_JSON + 1)
    if len(data) > MAX_JSON:
        raise ValueError("oversized_assets")
    graph = json.loads(data.decode("utf-8-sig"))
    if not isinstance(graph, dict):
        raise ValueError("invalid_assets")
    if roots is None:
        folders = graph.get("packageFolders")
        if not isinstance(folders, dict):
            raise ValueError("missing_package_roots")
        roots = list(folders)
    if not 1 <= len(roots) <= 4:
        raise ValueError("invalid_package_roots")
    roots = [Path(root).resolve(strict=True) for root in roots]
    if any(not root.is_dir() for root in roots):
        raise ValueError("invalid_package_roots")
    return graph, roots, hashlib.sha256(data).hexdigest()


def load_sources(graph, architecture, roots):
    targets = graph.get("targets")
    libraries = graph.get("libraries")
    if not isinstance(targets, dict) or not isinstance(libraries, dict) or len(libraries) > 512:
        raise ValueError("invalid_assets")
    matching = [target for name, target in targets.items() if name.endswith("/win-" + architecture.lower())]
    if len(matching) != 1 or not isinstance(matching[0], dict):
        raise ValueError("assets_target_mismatch")
    target = matching[0]
    sources = []
    for key, library in libraries.items():
        if not isinstance(library, dict):
            raise ValueError("invalid_library")
        if library.get("type") == "project":
            continue
        if library.get("type") != "package":
            raise ValueError("invalid_library_type")
        name, version = package_key(key)
        relative = library.get("path")
        if relative != (name + "/" + version).lower():
            raise ValueError("invalid_package_cache_path")
        if not isinstance(library.get("sha512"), str):
            raise ValueError("missing_assets_package_hash")
        asset = target.get(key, {})
        if not isinstance(asset, dict):
            raise ValueError("invalid_target_library")
        role = "known-build-tool-input-not-shipped" if name.lower() in BUILD_TOOLS else "resolved-input-retention-unverified"
        sources.append({"name": name, "version": version, "sha512": library.get("sha512"), "role": role})
    project = graph.get("project", {})
    if not isinstance(project, dict):
        raise ValueError("invalid_assets_project")
    frameworks = project.get("frameworks", {})
    if not isinstance(frameworks, dict) or len(frameworks) > 16:
        raise ValueError("invalid_frameworks")
    known = {(s["name"].lower(), s["version"].lower()) for s in sources}
    for framework in frameworks.values():
        if not isinstance(framework, dict):
            raise ValueError("invalid_framework")
        downloads = framework.get("downloadDependencies", [])
        if not isinstance(downloads, list) or len(downloads) > 64:
            raise ValueError("invalid_download_dependencies")
        for download in downloads:
            if not isinstance(download, dict):
                raise ValueError("invalid_download_dependency")
            value = download.get("version", "")
            match = re.fullmatch(r"\[([^,\[\]]+), ([^,\[\]]+)\]", value)
            if match is None or match[1] != match[2]:
                raise ValueError("unresolved_download_version")
            name, version = package_key(download.get("name", "") + "/" + match[1])
            key = (name.lower(), version.lower())
            if key not in known:
                known.add(key)
                sources.append({"name": name, "version": version, "sha512": None,
                                "role": "sdk-download-input-retention-unverified"})
    if len(sources) > 512:
        raise ValueError("too_many_sources")
    for source in sources:
        relative = Path(source["name"].lower()) / source["version"].lower()
        candidates = [(root / relative / (source["name"].lower() + "." + source["version"].lower() + ".nupkg")).resolve() for root in roots]
        if any(not path.is_relative_to(root) for path, root in zip(candidates, roots)):
            raise ValueError("source_escapes_cache")
        matches = [path for path in candidates if path.is_file()]
        if len(matches) != 1:
            raise ValueError("missing_or_ambiguous_package_archive")
        source["path"] = matches[0]
    return sources


def archive_entries(archive):
    items = archive.infolist()
    if len(items) > 16384 or sum(item.file_size for item in items) > MAX_ARCHIVE_BYTES:
        raise ValueError("oversized_source_archive")
    names = set()
    for ordinal, item in enumerate(items):
        if item.is_dir():
            continue
        name = inspector.package_name(item.filename)
        if name in names or item.file_size > MAX_MEMBER_BYTES or item.flag_bits & 1:
            raise ValueError("invalid_source_archive_entry")
        names.add(name)
        yield ordinal, item, name


def read_small(path, limit):
    with Path(path).open("rb") as stream:
        data = stream.read(limit + 1)
    if len(data) > limit:
        raise ValueError("oversized_source_metadata")
    return data


def decode_sha512(value):
    try:
        result = base64.b64decode(value, validate=True)
    except (TypeError, ValueError):
        raise ValueError("invalid_source_hash") from None
    if len(result) != 64:
        raise ValueError("invalid_source_hash")
    return result


def source_hash_binding(source):
    path = source["path"]
    if path.stat().st_size > MAX_ARCHIVE_BYTES:
        raise ValueError("oversized_source_archive")
    actual = digest(path, "sha512")
    expected = decode_sha512(source["sha512"]) if source["sha512"] is not None else None
    sidecar = Path(str(path) + ".sha512")
    metadata = path.parent / ".nupkg.metadata"
    retained = []
    binding = {}
    if sidecar.exists():
        data = read_small(sidecar, 4096)
        if decode_sha512(data.decode("utf-8-sig").strip()) != actual:
            raise ValueError("source_raw_sidecar_hash_mismatch")
        retained.append((sidecar, data))
        binding["raw_sidecar_sha256"] = hashlib.sha256(data).hexdigest()
    elif expected != actual:
        raise ValueError("missing_raw_source_sidecar")
    if metadata.exists():
        data = read_small(metadata, 65536)
        record = json.loads(data.decode("utf-8-sig"))
        if (not isinstance(record, dict) or type(record.get("version")) is not int or record.get("version") != 2
                or record.get("source") != "https://api.nuget.org/v3/index.json"):
            raise ValueError("invalid_nuget_cache_metadata")
        normalized = decode_sha512(record.get("contentHash"))
        if expected is not None and normalized != expected:
            raise ValueError("assets_cache_content_hash_mismatch")
        retained.append((metadata, data))
        binding["cache_metadata_sha256"] = hashlib.sha256(data).hexdigest()
    elif expected is not None and expected != actual:
        raise ValueError("missing_normalized_content_hash_binding")
    if expected is None:
        verification = "raw-sha512-local-sidecar; sdk-download-content-hash-not-declared; not-independent-source-authentication"
    elif expected == actual:
        verification = "assets-raw-sha512"
    else:
        # Signed NuGet assets use the signature's package content hash, rather
        # than SHA-512 of the signed archive. Bind it via local restore metadata;
        # do not claim to recompute signing normalization or authenticate NuGet.
        verification = "raw-sha512-local-sidecar; assets-content-hash-via-local-cache-metadata; not-independent-source-authentication"
    binding["package_hash_verification"] = verification
    if expected is not None:
        binding["assets_content_sha512"] = base64.b64encode(expected).decode("ascii")
    return actual.hex(), binding, retained


def scan_source(source, wanted):
    path = source["path"]
    raw_sha512, binding, retained = source_hash_binding(source)
    archive_sha256 = digest(path).hex()
    matches = {}

    def binaries(archive, container=None):
        for ordinal, item, name in archive_entries(archive):
            if Path(name).suffix not in (".dll", ".exe") or item.file_size not in wanted:
                continue
            with archive.open(item) as stream:
                value = hashlib.file_digest(stream, "sha256").hexdigest()
            if value in wanted[item.file_size]:
                matches.setdefault(value, []).append((container, ordinal))

    with zipfile.ZipFile(path) as archive:
        entries = list(archive_entries(archive))
        if ("assets-content-hash-via-local-cache-metadata" in binding["package_hash_verification"]
                and not any(name == ".signature.p7s" for _, _, name in entries)):
            raise ValueError("normalized_hash_without_package_signature")
        binaries(archive)
        nuspecs = [item for _, item, name in entries if name.endswith(".nuspec")]
        if len(nuspecs) != 1 or nuspecs[0].file_size > 1024 * 1024:
            raise ValueError("invalid_source_metadata")
        data = archive.read(nuspecs[0]).decode("utf-8-sig")
        if "<!DOCTYPE" in data.upper() or "<!ENTITY" in data.upper() or "\0" in data:
            raise ValueError("invalid_source_metadata")
        metadata = ET.fromstring(data)
        ids = [item.text for item in metadata.iter() if item.tag.rsplit("}", 1)[-1] == "id"]
        versions = [item.text for item in metadata.iter() if item.tag.rsplit("}", 1)[-1] == "version"]
        if len(ids) != 1 or not ids[0] or ids[0].lower() != source["name"].lower():
            raise ValueError("source_package_identity_mismatch")
        if versions != [source["version"]]:
            raise ValueError("source_package_version_mismatch")
        for ordinal, item, name in entries:
            if name.endswith(".msix"):
                with zipfile.ZipFile(io.BytesIO(archive.read(item))) as nested:
                    binaries(nested, ordinal)
    if digest(path).hex() != archive_sha256 or digest(path, "sha512").hex() != raw_sha512:
        raise ValueError("source_changed_during_export")
    if any(read_small(item, len(data)) != data for item, data in retained):
        raise ValueError("source_metadata_changed_during_export")
    return archive_sha256, raw_sha512, binding, matches


def export_bom(package, assets, architecture, source_commit, expected_hash, sources, assets_hash):
    if not re.fullmatch(r"[0-9a-f]{40}", source_commit) or not re.fullmatch(r"[0-9a-f]{64}", expected_hash):
        raise ValueError("invalid_source_or_package_hash")
    if digest(assets).hex() != assets_hash:
        raise ValueError("assets_changed_during_export")
    inspection = inspector.inspect_package(package, architecture)
    if inspection["status"] != "PASS" or inspection["package_sha256"] != expected_hash:
        raise ValueError("package_inspection_or_hash_failed")
    components = []
    unverified_inputs = []
    wanted = {}
    binary_records = []
    with zipfile.ZipFile(package) as archive:
        for ordinal, item, name in archive_entries(archive):
            if Path(name).suffix not in (".dll", ".exe"):
                continue
            with archive.open(item) as stream:
                value = hashlib.file_digest(stream, "sha256").hexdigest()
                pe = inspector.privacy.inspect_pe(stream)
            first_party = name == "wintodo.exe"
            record = {"ordinal": ordinal, "sha256": value, "bytes": item.file_size,
                      "first_party": first_party, "managed": pe["managed"], "machine": pe["machine"], "owners": []}
            binary_records.append(record)
            if not first_party:
                wanted.setdefault(item.file_size, set()).add(value)
    for source in sorted(sources, key=lambda s: (s["name"].lower(), s["version"])):
        sha256, raw_sha512, binding, matches = scan_source(source, wanted)
        purl = "pkg:nuget/" + source["name"] + "@" + quote(source["version"], safe=".-")
        role = "source-package-for-shipped-binary" if matches else source["role"]
        component = {"type": "library", "bom-ref": purl, "name": source["name"], "version": source["version"],
                     "purl": purl,
                     "hashes": [{"alg": "SHA-256", "content": sha256}, {"alg": "SHA-512", "content": raw_sha512}],
                     "properties": properties(role=role, **binding,
                                              licence_review="PENDING; inherited vendor texts reviewed separately")}
        if matches:
            component["scope"] = "required"
            components.append(component)
        else:
            # Omitting CycloneDX scope defaults to required. Keep uncertain
            # restored inputs outside product components, as build evidence.
            unverified_inputs.append(component)
        for binary in binary_records:
            if binary["first_party"] or binary["sha256"] not in matches:
                continue
            binary["owners"].append(purl)
            for container, ordinal in matches[binary["sha256"]]:
                component["properties"].extend(properties(matched_binary_sha256=binary["sha256"], source_entry=ordinal,
                                                         source_container="nupkg" if container is None else "nupkg-entry-" + str(container)))
    unresolved = 0
    binary_refs = []
    for binary in binary_records:
        ref = "urn:wintodo:binary:" + str(binary["ordinal"])
        binary_refs.append(ref)
        if not binary["first_party"] and not binary["owners"]:
            unresolved += 1
        component = {"type": "file", "bom-ref": ref, "name": "Wintodo.exe" if binary["first_party"] else "binary-" + str(binary["ordinal"]),
                     "hashes": [{"alg": "SHA-256", "content": binary["sha256"]}],
                     "properties": properties(package_entry=binary["ordinal"], bytes=binary["bytes"],
                                              binary_kind="managed" if binary["managed"] else "native", pe_machine=hex(binary["machine"]),
                                              ownership="first-party" if binary["first_party"] else "HASH_MATCHED" if binary["owners"] else "UNRESOLVED")}
        for owner in binary["owners"]:
            component["properties"].extend(properties(source_package=owner))
        components.append(component)
    if digest(package).hex() != expected_hash or digest(assets).hex() != assets_hash:
        raise ValueError("inputs_changed_during_export")
    ref = "urn:wintodo:msix:" + expected_hash
    return {"$schema": "https://cyclonedx.org/schema/bom-1.6.schema.json", "bomFormat": "CycloneDX", "specVersion": "1.6", "version": 1,
            "metadata": {"component": {"type": "application", "bom-ref": ref, "name": "Wintodo MSIX",
                                       "hashes": [{"alg": "SHA-256", "content": expected_hash}],
                                       "properties": properties(source_commit=source_commit, source_binding="caller-declared; not a build attestation",
                                                                architecture=architecture, assets_sha256=assets_hash)},
                         "properties": properties(export_status="PASS", binary_ownership="INCOMPLETE" if unresolved else "PASS",
                                                  unresolved_vendor_binaries=unresolved, payload_binaries=len(binary_records),
                                                  unverified_input_inventory=json.dumps(unverified_inputs, separators=(",", ":")),
                                                  full_dependency_closure="INCOMPLETE", linked_code_ownership="PENDING",
                                                  vendor_embedded_components="PENDING", projection_ownership="PENDING",
                                                  limitations="Hash-matched package ownership only. Unmatched restored/download/build input records are metadata evidence, not product components. Resolved graphs do not prove retained AOT code. Licence, source/build attestation, signature and deployment acceptance remain separate.")},
            "components": components, "dependencies": [{"ref": ref, "dependsOn": binary_refs}],
            "compositions": [{"aggregate": "incomplete", "assemblies": [ref]}]}


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("package")
    parser.add_argument("--assets", required=True, help="Matching target-specific AOT project.assets.json; remains private.")
    parser.add_argument("--architecture", required=True, choices=tuple(inspector.MACHINES))
    parser.add_argument("--source-commit", required=True)
    parser.add_argument("--expected-package-sha256", required=True)
    parser.add_argument("--package-root", action="append", help="Override assets' package-cache roots (at most four).")
    parser.add_argument("--output", required=True)
    parser.add_argument("--require-complete", action="store_true", help="Fail until vendor/linked-code/projection closure is established.")
    args = parser.parse_args(argv)
    output = Path(args.output)
    safe_output = False
    try:
        inputs = [Path(args.package), Path(args.assets)]
        if output.suffix.lower() != ".json":
            raise ValueError("output_must_be_json")
        if output.exists() and output.stat().st_nlink > 1:
            raise ValueError("output_is_hardlinked")
        if any(output.resolve() == item.resolve() or (output.exists() and item.exists() and output.samefile(item)) for item in inputs):
            raise ValueError("output_aliases_input")
        graph, roots, assets_hash = read_graph_and_roots(args.assets, args.package_root)
        if any(output.resolve().is_relative_to(root) for root in roots):
            raise ValueError("output_inside_package_cache")
        safe_output = True
        sources = load_sources(graph, args.architecture, roots)
        bom = export_bom(args.package, args.assets, args.architecture, args.source_commit,
                         args.expected_package_sha256, sources, assets_hash)
        output.parent.mkdir(parents=True, exist_ok=True)
        with tempfile.NamedTemporaryFile(mode="w", encoding="utf-8", dir=output.parent, delete=False) as stream:
            temporary = Path(stream.name)
            try:
                json.dump(bom, stream, indent=2)
                stream.write("\n")
                stream.close()
                os.replace(temporary, output)
            finally:
                temporary.unlink(missing_ok=True)
        complete = False  # No retained-code/vendor/projection proof input exists yet.
        print(json.dumps({"status": "PASS" if not args.require_complete or complete else "FAIL",
                          "export_status": "PASS", "full_dependency_closure": "INCOMPLETE",
                          "components": len(bom["components"])}))
        return 1 if args.require_complete and not complete else 0
    except ERRORS:
        if safe_output:
            try:
                output.parent.mkdir(parents=True, exist_ok=True)
                output.write_text(json.dumps({"status": "FAIL", "findings": [{"category": "invalid_input_or_output"}]}) + "\n", encoding="utf-8")
            except OSError:
                pass
        print(json.dumps({"status": "FAIL", "category": "invalid_input_or_output"}))
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
