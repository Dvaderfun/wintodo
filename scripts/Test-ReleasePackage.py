"""Inspect one supplied MSIX without extracting, installing, signing or running it."""

import argparse
import hashlib
import importlib.util
import json
import re
import struct
import sys
import xml.etree.ElementTree as ET
import zipfile
import zlib
from pathlib import Path

spec = importlib.util.spec_from_file_location("release_privacy", Path(__file__).with_name("Test-ReleasePrivacy.py"))
privacy = importlib.util.module_from_spec(spec)
spec.loader.exec_module(privacy)

NS = {"f": "http://schemas.microsoft.com/appx/manifest/foundation/windows10",
      "u": "http://schemas.microsoft.com/appx/manifest/uap/windows10",
      "d": "http://schemas.microsoft.com/appx/manifest/desktop/windows10",
      "c": "http://schemas.microsoft.com/appx/manifest/com/windows10",
      "r": "http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities"}
MACHINES = {"x64": 0x8664, "ARM64": 0xAA64}
RUNTIME_FILES = ("Wintodo.exe", "Microsoft.UI.Xaml.dll", "Microsoft.WindowsAppRuntime.dll", "e_sqlite3.dll")
REQUIRED_FILES = (*RUNTIME_FILES, "resources.pri", "[Content_Types].xml", "AppxBlockMap.xml")
FORBIDDEN_SUFFIXES = {".db", ".sqlite", ".sqlite3", ".pfx", ".p12", ".pem", ".key",
                      ".pdb", ".binlog", ".appxrecipe", ".log", ".trace", ".etl", ".trx", ".dmp"}


def package_name(value):
    value = value.replace("\\", "/")
    if not value or value.startswith("/") or ":" in value or any(p in ("", ".", "..") for p in value.split("/")):
        raise ValueError("unsafe_package_name")
    return value.casefold()


def version(value):
    if not re.fullmatch(r"(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)", value or ""):
        raise ValueError("invalid_version")
    parts = tuple(int(p) for p in value.split("."))
    if any(p > 65535 for p in parts) or not parts[0]:
        raise ValueError("invalid_version")
    return parts


def has_asset(reference, names):
    name = package_name(reference)
    if name in names:
        return True
    stem, separator, suffix = name.rpartition(".")
    # MSIX resource qualifiers can replace the logical unqualified asset file.
    return bool(separator) and any(re.fullmatch(re.escape(stem) + r"\.scale-(100|125|150|200|400)\." + re.escape(suffix), n) for n in names)


def inspect_package(path, architecture, expected_identity=None, expected_publisher=None,
                    expected_version=None, approved_payload=None):
    findings = []
    result = {"status": "FAIL", "architecture": architecture, "findings": findings,
              "identity_expectations": "CHECKED" if all((expected_identity, expected_publisher, expected_version)) else "PENDING",
              "payload_allowlist": "CHECKED" if approved_payload is not None else "PENDING",
              "limits": "Structural/privacy checks only. Identity approval, complete payload approval, SBOM/provenance, signatures/trust, clean-source equivalence and installation/runtime acceptance remain separate gates."}

    def fail(category, entry=None):
        finding = {"category": category}
        if entry is not None:
            finding["entry"] = entry
        findings.append(finding)

    try:
        if architecture not in MACHINES or Path(path).suffix.lower() != ".msix":
            raise ValueError("invalid_input")
        with Path(path).open("rb") as stream:
            result["package_sha256"] = hashlib.file_digest(stream, "sha256").hexdigest()
        with zipfile.ZipFile(path) as package:
            entries = {}
            if len(package.infolist()) > 4096 or sum(e.file_size for e in package.infolist()) > 1024 * 1024 * 1024:
                raise ValueError("oversized_archive")
            for ordinal, item in enumerate(package.infolist()):
                if item.is_dir():
                    continue
                if item.file_size > 256 * 1024 * 1024 or item.flag_bits & 1:
                    raise ValueError("oversized_or_encrypted_entry")
                name = package_name(item.filename)
                if name in entries:
                    raise ValueError("duplicate_package_name")
                entries[name] = (ordinal, item)
                parts = name.split("/")
                if (Path(name).suffix in FORBIDDEN_SUFFIXES or name.endswith(("-wal", "-shm"))
                        or any(p in {"fixtures", "import-staging", "attachments", "traces", ".git", ".codegraph"} for p in parts)):
                    fail("forbidden_payload", ordinal)
            result["payload_files"] = len(entries)
            if approved_payload is not None:
                approved = {package_name(n) for n in approved_payload}
                if set(entries) != approved:
                    fail("payload_allowlist_mismatch")
            for name in REQUIRED_FILES:
                if name.casefold() not in entries:
                    fail("missing_required_payload")
            manifest_entry = entries.get("appxmanifest.xml")
            if manifest_entry is None or manifest_entry[1].file_size > 1024 * 1024:
                raise ValueError("missing_or_oversized_manifest")
            manifest_bytes = package.read(manifest_entry[1])
            # DTDs/entities are not needed for an AppxManifest; reject them before parsing.
            manifest_text = manifest_bytes.decode("utf-8-sig")
            if "\0" in manifest_text or "<!DOCTYPE" in manifest_text.upper() or "<!ENTITY" in manifest_text.upper():
                raise ValueError("unsupported_manifest_dtd")
            manifest = ET.fromstring(manifest_bytes)
            if manifest.tag != "{" + NS["f"] + "}Package":
                raise ValueError("invalid_manifest_namespace")
            result["manifest_sha256"] = hashlib.sha256(manifest_bytes).hexdigest()
            identities = manifest.findall("f:Identity", NS)
            if len(identities) != 1:
                raise ValueError("invalid_manifest_identity")
            identity = identities[0]
            version(identity.get("Version"))
            if identity.get("ProcessorArchitecture", "").casefold() != architecture.casefold():
                fail("manifest_architecture_mismatch")
            for attribute, expected in (("Name", expected_identity), ("Publisher", expected_publisher), ("Version", expected_version)):
                if not identity.get(attribute) or (expected is not None and identity.get(attribute) != expected):
                    fail("manifest_identity_mismatch")
            capabilities = manifest.find("f:Capabilities", NS)
            if len(manifest.findall("f:Capabilities", NS)) != 1 or capabilities is None or [(e.tag, e.get("Name")) for e in capabilities] != [("{" + NS["r"] + "}Capability", "runFullTrust")]:
                fail("unreviewed_capabilities")
            dependencies = manifest.find("f:Dependencies", NS)
            if len(manifest.findall("f:Dependencies", NS)) != 1 or dependencies is None or not list(dependencies):
                fail("invalid_dependencies")
            else:
                families = []
                for item in dependencies:
                    if item.tag != "{" + NS["f"] + "}TargetDeviceFamily":
                        fail("external_package_dependency")
                        continue
                    families.append(item.get("Name"))
                    minimum = version(item.get("MinVersion"))
                    maximum = version(item.get("MaxVersionTested"))
                    if item.get("Name") not in ("Windows.Desktop", "Windows.Universal") or minimum < (10, 0, 26100, 0) or maximum < minimum:
                        fail("unsupported_device_family")
                if "Windows.Desktop" not in families or len(families) != len(set(families)):
                    fail("invalid_device_families")
            applications = manifest.findall("f:Applications/f:Application", NS)
            if len(manifest.findall("f:Applications", NS)) != 1 or len(applications) != 1:
                raise ValueError("invalid_applications")
            app = applications[0]
            if not app.get("Id") or app.get("Executable") != "Wintodo.exe" or app.get("EntryPoint") != "Windows.FullTrustApplication":
                fail("invalid_application_activation")
            extensions = app.find("f:Extensions", NS)
            expected_extensions = {(NS["d"], "windows.toastNotificationActivation"), (NS["c"], "windows.comServer"),
                                   (NS["d"], "windows.startupTask"), (NS["u"], "windows.shareTarget")}
            actual_extensions = [] if extensions is None else [(e.tag, e.get("Category")) for e in extensions]
            all_extensions = [e for e in manifest.iter() if e.tag.rsplit("}", 1)[-1] == "Extension"]
            if len(app.findall("f:Extensions", NS)) != 1 or len(actual_extensions) != 4 or len(all_extensions) != 4 or set(actual_extensions) != {("{" + n + "}Extension", c) for n, c in expected_extensions}:
                fail("unreviewed_extensions")
            if any(e.get("Executable") not in (None, "Wintodo.exe") for e in manifest.iter()):
                fail("unreviewed_executable_reference")
            toast = app.find("f:Extensions/d:Extension/d:ToastNotificationActivation", NS)
            com_extension = app.find("f:Extensions/c:Extension", NS)
            com_servers = [] if com_extension is None else com_extension.findall("c:ComServer", NS)
            exe_servers = [] if len(com_servers) != 1 else com_servers[0].findall("c:ExeServer", NS)
            server = app.find("f:Extensions/c:Extension/c:ComServer/c:ExeServer", NS)
            classes = [] if server is None else server.findall("c:Class", NS)
            if toast is None or len(com_servers) != 1 or len(exe_servers) != 1 or server is None or len(classes) != 1 or server.get("Executable") != "Wintodo.exe" or server.get("Arguments") != "----AppNotificationActivated:" or not toast.get("ToastActivatorCLSID") or classes[0].get("Id", "").casefold() != toast.get("ToastActivatorCLSID", "").casefold():
                fail("invalid_toast_com_binding")
            startup = app.find("f:Extensions/d:Extension[@Category='windows.startupTask']", NS)
            task = None if startup is None else startup.find("d:StartupTask", NS)
            if startup is None or startup.get("Executable") != "Wintodo.exe" or startup.get("EntryPoint") != "Windows.FullTrustApplication" or task is None or task.get("Enabled") != "false" or not task.get("TaskId"):
                fail("invalid_startup_declaration")
            share = app.find("f:Extensions/u:Extension/u:ShareTarget", NS)
            if share is None or sorted((e.tag, e.text) for e in share) != sorted(("{" + NS["u"] + "}DataFormat", n) for n in ("Text", "WebLink")):
                fail("invalid_share_formats")
            asset_refs = []
            logo = manifest.find("f:Properties/f:Logo", NS)
            if logo is not None and logo.text:
                asset_refs.append(logo.text)
            for node in app.iter():
                for attribute in ("Square150x150Logo", "Square44x44Logo", "Wide310x150Logo", "Image"):
                    if node.get(attribute):
                        asset_refs.append(node.get(attribute))
            if len(asset_refs) < 5 or any(not has_asset(n, entries) for n in asset_refs):
                fail("missing_manifest_assets")
            result["native_binaries"] = 0
            result["vendor_binaries_with_other_architectures"] = 0
            for name, (ordinal, item) in entries.items():
                if Path(name).suffix not in (".exe", ".dll"):
                    continue
                with package.open(item) as stream:
                    binary = privacy.inspect_pe(stream)
                result["native_binaries"] += 1
                if name in {n.casefold() for n in RUNTIME_FILES} and (binary["machine"] != MACHINES[architecture] or binary["managed"]):
                    fail("invalid_native_architecture", ordinal)
                elif binary["machine"] != MACHINES[architecture]:
                    # Vendor resource/interop payloads can legitimately be ARM64EC.
                    # Their reviewed closure is checked separately against the allowlist/SBOM.
                    result["vendor_binaries_with_other_architectures"] += 1
        privacy_result = privacy.scan_inputs([path], required_architectures=[architecture])
        result["privacy_status"] = privacy_result["status"]
        privacy_hash = next((r["package_sha256"] for r in privacy_result["files"] if "package_sha256" in r), None)
        with Path(path).open("rb") as stream:
            final_hash = hashlib.file_digest(stream, "sha256").hexdigest()
        if privacy_hash != result["package_sha256"] or final_hash != result["package_sha256"]:
            fail("package_changed_during_inspection")
        findings.extend({"category": "privacy_" + f["category"], **({"entry": f["entry"]} if "entry" in f else {})} for f in privacy_result["findings"])
    except (OSError, ValueError, ET.ParseError, zipfile.BadZipFile, RuntimeError, NotImplementedError, struct.error, OverflowError, UnicodeError, zlib.error):
        fail("unreadable_or_invalid_package")
    result["status"] = "FAIL" if findings else "PASS"
    return result


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("package")
    parser.add_argument("--architecture", required=True, choices=tuple(MACHINES))
    parser.add_argument("--output", required=True)
    parser.add_argument("--expected-identity")
    parser.add_argument("--expected-publisher")
    parser.add_argument("--expected-version")
    parser.add_argument("--approved-payload", help="Reviewed JSON array of every exact package-relative file, including packaging/signature metadata.")
    args = parser.parse_args(argv)
    safe_output = False
    try:
        output = Path(args.output)
        inputs = [Path(args.package)] + ([Path(args.approved_payload)] if args.approved_payload else [])
        if any(output.resolve() == item.resolve() or (output.exists() and item.exists() and output.samefile(item)) for item in inputs):
            raise ValueError("output_aliases_input")
        safe_output = True
        payload = None
        if args.approved_payload:
            payload = json.loads(Path(args.approved_payload).read_text(encoding="utf-8-sig"))
            if not isinstance(payload, list) or not all(isinstance(p, str) for p in payload):
                raise ValueError("invalid_allowlist")
        result = inspect_package(args.package, args.architecture, args.expected_identity, args.expected_publisher, args.expected_version, payload)
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    except (OSError, ValueError):
        failure = {"status": "FAIL", "findings": [{"category": "invalid_policy_or_output"}]}
        if safe_output:
            try:
                output.parent.mkdir(parents=True, exist_ok=True)
                output.write_text(json.dumps(failure, indent=2) + "\n", encoding="utf-8")
            except OSError:
                # An unwritable output cannot be invalidated; callers must honor exit 1.
                pass
        print(json.dumps({"status": "FAIL", "category": "invalid_policy_or_output"}))
        return 1
    print(json.dumps({"status": result["status"], "findings": len(result["findings"])}))
    return 0 if result["status"] == "PASS" else 1


if __name__ == "__main__":
    sys.exit(main())
