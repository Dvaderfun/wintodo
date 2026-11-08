"""Check hash-pinned unsigned x64/ARM64 inputs; never authorize signing or release."""

import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import struct
import tempfile
import xml.etree.ElementTree as ET
import zipfile
import zlib

spec = importlib.util.spec_from_file_location("candidate_package", Path(__file__).with_name("Test-ReleasePackage.py"))
inspector = importlib.util.module_from_spec(spec)
spec.loader.exec_module(inspector)

KINDS = ("package", "structural", "privacy", "toolchain", "dependencies")
ARCHITECTURES = ("x64", "ARM64")
JSON_LIMIT = 16 * 1024 * 1024
PENDING = ("production_identity_version_and_payload_approval", "tag_and_main_ancestry_policy",
           "source_to_package_execution_provenance", "audit_to_resolved_graph_binding",
           "native_tool_advisory_disposition", "complete_sbom_and_legal_review", "hosted_ci_and_protection",
           "production_signing_timestamp_and_trust", "final_signed_package_and_deployment_gates",
           "explicit_publication_authorization")


class InvalidCandidate(ValueError):
    def __init__(self, category, package=None, evidence=None):
        self.finding = {"category": category}
        if package is not None:
            self.finding["package"] = package
        if evidence is not None:
            self.finding["evidence"] = evidence


def require(condition, category, package=None, evidence=None):
    if not condition:
        raise InvalidCandidate(category, package, evidence)


def sha256(value):
    return isinstance(value, str) and re.fullmatch(r"[a-f0-9]{64}", value) is not None


def integer(value, minimum=0):
    return type(value) is int and value >= minimum


def file_hash(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def read_json(path):
    with path.open("rb") as stream:
        data = stream.read(JSON_LIMIT + 1)
    require(len(data) <= JSON_LIMIT, "oversized_json_input")

    def unique_object(pairs):
        result = {}
        for key, value in pairs:
            require(key not in result, "duplicate_json_key")
            result[key] = value
        return result

    return json.loads(data.decode("utf-8-sig"), object_pairs_hook=unique_object), hashlib.sha256(data).hexdigest()


def descriptor_inputs(document, base):
    """Collect even malformed records before deciding whether output can be replaced."""
    paths = []
    if isinstance(document, dict) and isinstance(document.get("packages"), list):
        for record in document["packages"]:
            if isinstance(record, dict):
                for kind in KINDS:
                    value = record.get(kind)
                    if isinstance(value, dict) and isinstance(value.get("path"), str):
                        paths.append(base / value["path"])
    return paths


def passing(report, package, kind):
    require(isinstance(report, dict) and report.get("status") == "PASS" and report.get("findings") == [],
            "gate_not_passed_or_malformed", package, kind)


def check_toolchain(report, commit, architecture, ordinal):
    require(isinstance(report, dict), "invalid_toolchain_evidence", ordinal, "toolchain")
    require(report.get("status", "PASS") == "PASS" and report.get("Status", "PASS") == "PASS"
            and report.get("findings", []) == [], "invalid_toolchain_evidence", ordinal, "toolchain")
    require(report.get("SourceCommit") == commit, "source_commit_mismatch", ordinal, "toolchain")
    require(isinstance(report.get("Architecture"), str) and report["Architecture"].casefold() == architecture.casefold(),
            "evidence_architecture_mismatch", ordinal, "toolchain")
    require(report.get("AotGraphVerified") is True, "aot_graph_not_verified", ordinal, "toolchain")
    for name, pattern in (("DotnetSdk", r"[0-9]+(?:\.[0-9]+){2}"), ("WinAppCli", r"[0-9]+(?:\.[0-9]+){2}"),
                          ("TargetRuntime", r"[0-9]+(?:\.[0-9]+){2}"), ("Msvc", r"[0-9]+(?:\.[0-9]+){2}"),
                          ("VisualStudio", r"[0-9]+(?:\.[0-9]+){2,3}"), ("WindowsSdkAvailable", r"[0-9]+(?:\.[0-9]+){3}")):
        require(isinstance(report.get(name), str) and re.fullmatch(pattern, report[name]) is not None,
                "invalid_toolchain_evidence", ordinal, "toolchain")
    packages = report.get("Packages")
    require(isinstance(packages, dict) and 0 < len(packages) <= 1024, "invalid_toolchain_evidence", ordinal, "toolchain")
    for name, value in packages.items():
        require(re.fullmatch(r"[A-Za-z0-9_.-]{1,256}", name) is not None and isinstance(value, str)
                and re.fullmatch(r"[0-9]+(?:\.[0-9]+){1,3}", value) is not None,
                "invalid_toolchain_evidence", ordinal, "toolchain")
    target_compiler = "runtime.win-" + architecture.casefold() + ".Microsoft.DotNet.ILCompiler"
    require(packages.get("Microsoft.DotNet.ILCompiler") == report["TargetRuntime"]
            and packages.get(target_compiler) == report["TargetRuntime"]
            and packages.get("Microsoft.Windows.SDK.BuildTools.WinApp") == report["WinAppCli"],
            "inconsistent_toolchain_graph", ordinal, "toolchain")
    return packages


def inspect_candidate(document, base):
    require(isinstance(document, dict) and set(document) == {"source_commit", "packages"}, "invalid_descriptor")
    commit = document["source_commit"]
    require(isinstance(commit, str) and re.fullmatch(r"[a-f0-9]{40}", commit) is not None and commit != "0" * 40,
            "invalid_source_commit")
    records = document["packages"]
    require(isinstance(records, list) and len(records) == 2, "required_architectures_missing_or_duplicated")
    require(all(isinstance(r, dict) and set(r) == {"architecture", *KINDS} for r in records), "invalid_descriptor")
    require({r["architecture"] for r in records if isinstance(r["architecture"], str)} == set(ARCHITECTURES),
            "required_architectures_missing_or_duplicated")
    records = sorted(records, key=lambda r: ARCHITECTURES.index(r["architecture"]))
    output = {"status": "PASS", "scope": "unsigned_candidate_input_consistency", "release_authorized": False,
              "source_commit": commit, "packages": [], "findings": [], "pending_gates": list(PENDING),
              "toolchain_scope": "reported_graph_and_tool_availability; execution_not_verified",
              "dependency_scope": "hash_pinned_summary; source_and_resolved_graph_not_recorded_by_audit"}
    checked_files = []
    identities = []
    toolchains = []
    for ordinal, record in enumerate(records):
        architecture = record["architecture"]
        paths = {}
        reports = {}
        evidence = {}
        for kind in KINDS:
            item = record[kind]
            require(isinstance(item, dict) and set(item) == {"path", "sha256"} and isinstance(item["path"], str)
                    and item["path"] and "\0" not in item["path"] and sha256(item["sha256"]),
                    "invalid_descriptor", ordinal, kind)
            path = base / item["path"]
            if kind == "package":
                digest = file_hash(path)
            else:
                reports[kind], digest = read_json(path)
            require(digest == item["sha256"], "input_hash_mismatch", ordinal, kind)
            checked_files.append((path, digest))
            paths[kind] = path
            evidence[kind] = digest

        fresh = inspector.inspect_package(paths["package"], architecture)
        passing(fresh, ordinal, "fresh_structural_privacy")
        require(fresh.get("package_sha256") == evidence["package"], "package_changed_during_inspection", ordinal)
        structural = reports["structural"]
        passing(structural, ordinal, "structural")
        require(structural.get("architecture") == architecture, "evidence_architecture_mismatch", ordinal, "structural")
        require(structural.get("package_sha256") == evidence["package"]
                and structural.get("manifest_sha256") == fresh.get("manifest_sha256")
                and structural.get("privacy_status") == "PASS"
                and integer(structural.get("payload_files"), 1)
                and structural["payload_files"] == fresh["payload_files"]
                and integer(structural.get("native_binaries"), 1)
                and structural["native_binaries"] == fresh["native_binaries"],
                "structural_evidence_mismatch", ordinal, "structural")

        with zipfile.ZipFile(paths["package"]) as package:
            # The path was reopened after inspection; retain the inspector's read
            # bounds before reading anything in case its bytes changed meanwhile.
            items = package.infolist()
            require(len(items) <= 4096 and sum(i.file_size for i in items) <= 1024 * 1024 * 1024,
                    "package_changed_during_inspection", ordinal)
            entries = {}
            for item in items:
                if item.is_dir():
                    continue
                name = inspector.package_name(item.filename)
                require(name not in entries and item.file_size <= 256 * 1024 * 1024 and not item.flag_bits & 1,
                        "package_changed_during_inspection", ordinal)
                entries[name] = item
            require(len(entries) == fresh["payload_files"] and entries["appxmanifest.xml"].file_size <= 1024 * 1024,
                    "package_changed_during_inspection", ordinal)
            require("appxsignature.p7x" not in entries, "signed_package_in_unsigned_handoff", ordinal)
            identity = ET.fromstring(package.read(entries["appxmanifest.xml"])).find("f:Identity", inspector.NS)
            require(identity is not None, "package_changed_during_inspection", ordinal)
            identities.append(tuple(identity.get(k) for k in ("Name", "Publisher", "Version")))
            actual_entries = {}
            executable_entry = None
            for entry_ordinal, item in enumerate(items):
                if item.is_dir():
                    continue
                digest = hashlib.sha256()
                size = 0
                with package.open(item) as stream:
                    while block := stream.read(1024 * 1024):
                        size += len(block)
                        digest.update(block)
                actual_entries[entry_ordinal] = (size, digest.hexdigest())
                if inspector.package_name(item.filename) == "wintodo.exe":
                    executable_entry = entry_ordinal
            require(executable_entry is not None, "package_changed_during_inspection", ordinal)
            executable_hash = actual_entries[executable_entry][1]

        privacy = reports["privacy"]
        passing(privacy, ordinal, "privacy")
        files = privacy.get("files")
        binaries = privacy.get("first_party_binaries")
        require(isinstance(files, list) and all(isinstance(f, dict) and integer(f.get("input"))
                and ("entry" not in f or (integer(f["entry"]) and sha256(f.get("sha256")) and integer(f.get("bytes")))) for f in files)
                and integer(privacy.get("files_scanned"), 1)
                and privacy["files_scanned"] == sum("entry" in f for f in files)
                and isinstance(binaries, list) and all(isinstance(b, dict) for b in binaries),
                "invalid_privacy_evidence", ordinal, "privacy")
        package_files = [f for f in files if "package_sha256" in f]
        require(len(package_files) == 1 and package_files[0].get("package_sha256") == evidence["package"]
                and integer(package_files[0].get("input")), "privacy_package_hash_mismatch", ordinal, "privacy")
        package_input = package_files[0]["input"]
        entries_scanned = [f for f in files if f.get("input") == package_input and "entry" in f]
        package_binaries = [b for b in binaries if type(b.get("input")) is int and b["input"] == package_input]
        require(len(entries_scanned) == fresh["payload_files"]
                and len({f["entry"] for f in entries_scanned if integer(f["entry"])}) == len(entries_scanned)
                and all(sha256(f.get("sha256")) and integer(f.get("bytes")) for f in entries_scanned)
                and {f["entry"]: (f["bytes"], f["sha256"]) for f in entries_scanned} == actual_entries
                and len(package_binaries) == 1 and package_binaries[0].get("architecture") == architecture
                and package_binaries[0].get("sha256") == executable_hash and type(package_binaries[0].get("entry")) is int
                and package_binaries[0]["entry"] == executable_entry and package_binaries[0].get("codeview_basename_only") is True,
                "privacy_evidence_mismatch", ordinal, "privacy")

        dependencies = reports["dependencies"]
        require(isinstance(dependencies, dict) and dependencies.get("status") == "PASS"
                and dependencies.get("scanner") == "NuGet" and type(dependencies.get("projects")) is int
                and dependencies["projects"] == 1
                and type(dependencies.get("vulnerable_packages")) is int and dependencies["vulnerable_packages"] == 0
                and type(dependencies.get("advisories")) is int and dependencies["advisories"] == 0
                and dependencies.get("findings", []) == [],
                "dependency_gate_not_passed_or_malformed", ordinal, "dependencies")
        toolchains.append((reports["toolchain"], check_toolchain(reports["toolchain"], commit, architecture, ordinal)))
        output["packages"].append({"architecture": architecture, "package_sha256": evidence["package"],
                                   "manifest_sha256": fresh["manifest_sha256"], "executable_sha256": executable_hash,
                                   "unsigned": True, "evidence_sha256": {k: evidence[k] for k in KINDS if k != "package"}})

    require(identities[0] == identities[1], "cross_architecture_identity_or_version_mismatch")
    require(all(toolchains[0][0][key] == toolchains[1][0][key] for key in ("DotnetSdk", "WinAppCli", "TargetRuntime")),
            "cross_architecture_toolchain_mismatch")
    def common_graph(graph):
        return {k: v for k, v in graph.items() if k not in {"runtime.win-x64.Microsoft.DotNet.ILCompiler",
                                                         "runtime.win-arm64.Microsoft.DotNet.ILCompiler"}}
    require(common_graph(toolchains[0][1]) == common_graph(toolchains[1][1]), "cross_architecture_dependency_graph_mismatch")
    for path, digest in checked_files:
        require(file_hash(path) == digest, "input_changed_during_verification")
    output["cross_architecture_identity_and_version"] = "MATCH; production_approval_pending"
    return output


def write_report(path, result):
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = None
    try:
        with tempfile.NamedTemporaryFile("w", encoding="utf-8", dir=path.parent, delete=False) as stream:
            temporary = Path(stream.name)
            json.dump(result, stream, indent=2)
            stream.write("\n")
        os.replace(temporary, path)
    finally:
        if temporary is not None:
            temporary.unlink(missing_ok=True)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("descriptor", help="Local JSON with one source commit and exact input paths/SHA-256 values.")
    parser.add_argument("--output", required=True)
    args = parser.parse_args(argv)
    descriptor = Path(args.descriptor)
    output = Path(args.output)
    safe_output = False
    try:
        # First protect the descriptor even if it cannot be parsed.
        require(output.resolve() != descriptor.resolve()
                and not (output.exists() and descriptor.exists() and output.samefile(descriptor)), "output_aliases_input")
        safe_output = True
        document, descriptor_hash = read_json(descriptor)
        safe_output = False
        inputs = descriptor_inputs(document, descriptor.parent)
        require(not any(output.resolve() == p.resolve() or (output.exists() and p.exists() and output.samefile(p)) for p in inputs),
                "output_aliases_input")
        safe_output = True
        result = inspect_candidate(document, descriptor.parent)
        require(file_hash(descriptor) == descriptor_hash, "descriptor_changed_during_verification")
        result["descriptor_sha256"] = descriptor_hash
        write_report(output, result)
    except (OSError, ValueError, UnicodeError, RuntimeError, TypeError, KeyError, IndexError,
            OverflowError, struct.error, zlib.error, zipfile.BadZipFile, ET.ParseError) as error:
        finding = error.finding if isinstance(error, InvalidCandidate) else {"category": "invalid_input_or_output"}
        result = {"status": "FAIL", "scope": "unsigned_candidate_input_consistency", "release_authorized": False,
                  "findings": [finding], "pending_gates": list(PENDING)}
        if safe_output:
            try:
                write_report(output, result)
            except OSError:
                # If the report is unwritable, consumers must honor the nonzero exit code.
                pass
        print(json.dumps(result))
        return 1
    print(json.dumps({"status": "PASS", "scope": result["scope"], "release_authorized": False,
                      "packages": len(result["packages"]), "pending_gates": len(PENDING)}))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
