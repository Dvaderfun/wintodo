"""Exact-byte ownership and privacy regressions; no native build or installation."""

import base64
import contextlib
import copy
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import tempfile
import unittest
import zipfile

from test_release_package import payload

ROOT = Path(__file__).parents[2]
spec = importlib.util.spec_from_file_location("release_sbom", ROOT / "scripts" / "Export-ReleaseSbom.py")
sbom = importlib.util.module_from_spec(spec)
spec.loader.exec_module(sbom)
COMMIT = "1" * 40


def property_values(document):
    return {p["name"]: p["value"] for p in document["metadata"]["properties"]}


def input_inventory(document):
    return json.loads(property_values(document)["wintodo:unverified-input-inventory"])


class ReleaseSbomTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="synthetic-private-owner-")
        self.addCleanup(self.temp.cleanup)
        self.directory = Path(self.temp.name)
        self.package = self.directory / "candidate.msix"
        with zipfile.ZipFile(self.package, "w") as archive:
            for name, value in payload().items():
                archive.writestr(name, value)
        self.cache = self.directory / "private-user-cache"
        self.archive = self.cache / "synthetic.native" / "1.2.3" / "synthetic.native.1.2.3.nupkg"
        self.archive.parent.mkdir(parents=True)
        self.binary = payload()["Microsoft.UI.Xaml.dll"]
        self.create_source({"different-vendor-filename.dll": self.binary})
        self.assets = self.directory / "project.assets.json"
        self.graph = {"targets": {"net10.0/win-x64": {"Synthetic.Native/1.2.3": {"compile": {"lib/input.dll": {}}}}},
                      "libraries": {"Synthetic.Native/1.2.3": {"type": "package", "path": "synthetic.native/1.2.3",
                                                               "sha512": self.source_hash()}},
                      "packageFolders": {str(self.cache): {}}, "project": {"frameworks": {}}}
        self.write_graph()
        self.output = self.directory / "bom.json"

    def create_source(self, files, identifier="Synthetic.Native", version="1.2.3", archive=None):
        archive = archive or self.archive
        with zipfile.ZipFile(archive, "w") as value:
            value.writestr("source.nuspec", f"<package><metadata><id>{identifier}</id><version>{version}</version></metadata></package>")
            for name, content in files.items():
                value.writestr(name, content)

    def source_hash(self):
        return base64.b64encode(hashlib.sha512(self.archive.read_bytes()).digest()).decode()

    def write_graph(self):
        self.assets.write_text(json.dumps(self.graph), encoding="utf-8")

    def run_export(self, **overrides):
        values = {"package": str(self.package), "assets": str(self.assets), "architecture": "x64", "source-commit": COMMIT,
                  "expected-package-sha256": hashlib.sha256(self.package.read_bytes()).hexdigest(), "output": str(self.output)}
        values.update(overrides)
        arguments = [values.pop("package")]
        for key, value in values.items():
            if value is True:
                arguments.append("--" + key)
            else:
                arguments.extend(("--" + key, str(value)))
        stream = io.StringIO()
        with contextlib.redirect_stdout(stream):
            code = sbom.main(arguments)
        self.assertNotIn("synthetic-private-owner", stream.getvalue())
        self.assertNotIn(str(self.cache), stream.getvalue())
        return code

    def read_bom(self):
        return json.loads(self.output.read_text())

    def test_hash_ownership_uses_original_bytes_and_keeps_closure_incomplete(self):
        self.assertEqual(0, self.run_export())
        document = self.read_bom()
        self.assertEqual(("CycloneDX", "1.6"), (document["bomFormat"], document["specVersion"]))
        props = property_values(document)
        self.assertEqual("PASS", props["wintodo:binary-ownership"])
        self.assertEqual("INCOMPLETE", props["wintodo:full-dependency-closure"])
        self.assertEqual("incomplete", document["compositions"][0]["aggregate"])
        source = next(c for c in document["components"] if c["type"] == "library")
        self.assertEqual("pkg:nuget/Synthetic.Native@1.2.3", source["purl"])
        self.assertEqual(hashlib.sha256(self.archive.read_bytes()).hexdigest(), source["hashes"][0]["content"])
        self.assertIn({"name": "wintodo:package-hash-verification", "value": "assets-raw-sha512"}, source["properties"])
        self.assertNotIn("different-vendor-filename", json.dumps(document))
        self.assertNotIn("synthetic-private-owner", json.dumps(document))
        self.assertNotIn(str(self.cache), json.dumps(document))
        self.assertNotIn("project.assets.json", json.dumps(document))

    def test_same_filename_with_wrong_bytes_does_not_establish_ownership(self):
        self.create_source({"Microsoft.UI.Xaml.dll": b"different content"})
        self.graph["libraries"]["Synthetic.Native/1.2.3"]["sha512"] = self.source_hash()
        self.write_graph()
        self.assertEqual(0, self.run_export())
        props = property_values(self.read_bom())
        self.assertEqual("INCOMPLETE", props["wintodo:binary-ownership"])
        self.assertEqual("3", props["wintodo:unresolved-vendor-binaries"])
        self.assertFalse(any(c["type"] == "library" for c in self.read_bom()["components"]))
        source = input_inventory(self.read_bom())[0]
        self.assertNotIn("scope", source)
        self.assertIn({"name": "wintodo:role", "value": "resolved-input-retention-unverified"}, source["properties"])

    def test_nested_runtime_msix_matches_hashes(self):
        nested = io.BytesIO()
        with zipfile.ZipFile(nested, "w") as archive:
            archive.writestr("runtime/vendor.dll", self.binary)
        self.create_source({"tools/runtime.msix": nested.getvalue()})
        self.graph["libraries"]["Synthetic.Native/1.2.3"]["sha512"] = self.source_hash()
        self.write_graph()
        self.assertEqual(0, self.run_export())
        document = self.read_bom()
        self.assertEqual("PASS", property_values(document)["wintodo:binary-ownership"])
        source = next(c for c in document["components"] if c["type"] == "library")
        self.assertTrue(any(p["name"] == "wintodo:source-container" and p["value"].startswith("nupkg-entry-") for p in source["properties"]))

    def test_tampered_package_archive_rejects_and_invalidates_previous_bom(self):
        self.assertEqual(0, self.run_export())
        self.create_source({"vendor.dll": b"tampered"})
        self.assertEqual(1, self.run_export())
        self.assertEqual("FAIL", self.read_bom()["status"])

    def test_invalid_source_hash_rejects_without_echo(self):
        for value in (None, "private-owner/hash", "a" * 64, ["private-owner"]):
            self.graph["libraries"]["Synthetic.Native/1.2.3"]["sha512"] = value
            self.write_graph()
            self.assertEqual(1, self.run_export())
            self.assertNotIn("private-owner", self.output.read_text())

    def test_missing_source_archive_fails(self):
        self.archive.unlink()
        self.assertEqual(1, self.run_export())
        self.assertEqual("FAIL", self.read_bom()["status"])

    def test_source_nuspec_identity_must_match_hash_record(self):
        for identifier, version in (("Other.Source", "1.2.3"), ("Synthetic.Native", "9.9.9")):
            self.create_source({"vendor.dll": self.binary}, identifier, version)
            self.graph["libraries"]["Synthetic.Native/1.2.3"]["sha512"] = self.source_hash()
            self.write_graph()
            self.assertEqual(1, self.run_export())

    def test_source_metadata_dtd_is_rejected(self):
        with zipfile.ZipFile(self.archive, "w") as archive:
            archive.writestr("source.nuspec", '<!DOCTYPE package [<!ENTITY x "value">]><package/>')
        self.graph["libraries"]["Synthetic.Native/1.2.3"]["sha512"] = self.source_hash()
        self.write_graph()
        self.assertEqual(1, self.run_export())

    def test_source_cache_escape_is_rejected(self):
        self.graph["libraries"]["Synthetic.Native/1.2.3"]["path"] = "../private-owner/1.2.3"
        self.write_graph()
        self.assertEqual(1, self.run_export())
        self.assertNotIn("private-owner", self.output.read_text())

    def test_assets_target_mismatch_invalidates_previous_bom(self):
        self.assertEqual(0, self.run_export())
        self.graph["targets"] = {"net10.0/win-arm64": {}}
        self.write_graph()
        self.assertEqual(1, self.run_export())
        self.assertEqual("FAIL", self.read_bom()["status"])

    def test_malformed_graph_is_redacted(self):
        baseline = copy.deepcopy(self.graph)
        for key, value in (("libraries", []), ("project", None), ("targets", None)):
            self.graph = copy.deepcopy(baseline)
            self.graph[key] = value
            self.write_graph()
            self.assertEqual(1, self.run_export())
            self.assertEqual("FAIL", self.read_bom()["status"])

    def test_malformed_identifiers_and_source_commit_are_redacted(self):
        self.assertEqual(1, self.run_export(**{"source-commit": "private-owner-name"}))
        self.assertNotIn("private-owner-name", self.output.read_text())
        self.graph["libraries"] = {"C:/Users/private-owner": {"type": "package"}}
        self.write_graph()
        self.assertEqual(1, self.run_export())
        self.assertNotIn("private-owner", self.output.read_text())

    def test_expected_package_hash_is_required_and_exact(self):
        self.assertEqual(1, self.run_export(**{"expected-package-sha256": "0" * 64}))
        self.assertEqual(1, self.run_export(**{"expected-package-sha256": "bad"}))

    def test_output_aliases_and_hardlinks_preserve_inputs(self):
        for source in (self.package, self.assets, self.archive):
            original = source.read_bytes()
            self.assertEqual(1, self.run_export(output=source))
            self.assertEqual(original, source.read_bytes())
        linked = self.directory / "hardlinked.json"
        os.link(self.assets, linked)
        original = self.assets.read_bytes()
        self.assertEqual(1, self.run_export(output=linked))
        self.assertEqual(original, self.assets.read_bytes())
        self.assertEqual(original, linked.read_bytes())

    def test_output_inside_cache_is_preserved(self):
        output = self.cache / "existing.json"
        output.write_text("original cache data", encoding="utf-8")
        self.assertEqual(1, self.run_export(output=output))
        self.assertEqual("original cache data", output.read_text())
        self.graph["libraries"] = []
        self.write_graph()
        self.assertEqual(1, self.run_export(output=output))
        self.assertEqual("original cache data", output.read_text())

    def test_complete_closure_gate_fails_even_with_every_vendor_binary_mapped(self):
        self.assertEqual(1, self.run_export(**{"require-complete": True}))
        self.assertEqual("INCOMPLETE", property_values(self.read_bom())["wintodo:full-dependency-closure"])

    def test_build_asset_alone_does_not_prove_exclusion(self):
        self.graph["targets"]["net10.0/win-x64"]["Synthetic.Native/1.2.3"] = {"build": {"tools.props": {}}}
        self.create_source({"tools-only.dll": b"not shipped"})
        self.graph["libraries"]["Synthetic.Native/1.2.3"]["sha512"] = self.source_hash()
        self.write_graph()
        self.assertEqual(0, self.run_export())
        self.assertFalse(any(c["type"] == "library" for c in self.read_bom()["components"]))
        source = input_inventory(self.read_bom())[0]
        self.assertNotIn("scope", source)
        self.assertIn({"name": "wintodo:role", "value": "resolved-input-retention-unverified"}, source["properties"])

    def test_reviewed_build_tool_is_excluded(self):
        archive = self.cache / "microsoft.net.illink.tasks" / "1.2.3" / "microsoft.net.illink.tasks.1.2.3.nupkg"
        archive.parent.mkdir(parents=True)
        self.create_source({"tool-only.dll": b"not shipped"}, "Microsoft.NET.ILLink.Tasks", archive=archive)
        self.graph["libraries"] = {"Microsoft.NET.ILLink.Tasks/1.2.3": {"type": "package", "path": "microsoft.net.illink.tasks/1.2.3",
                                                                   "sha512": base64.b64encode(hashlib.sha512(archive.read_bytes()).digest()).decode()}}
        self.graph["targets"]["net10.0/win-x64"] = {"Microsoft.NET.ILLink.Tasks/1.2.3": {"build": {"tool.props": {}}}}
        self.write_graph()
        self.assertEqual(0, self.run_export())
        self.assertFalse(any(c["type"] == "library" for c in self.read_bom()["components"]))
        source = input_inventory(self.read_bom())[0]
        self.assertNotIn("scope", source)
        self.assertIn({"name": "wintodo:role", "value": "known-build-tool-input-not-shipped"}, source["properties"])

    def test_sdk_download_hash_records_disclose_sidecar_verification(self):
        archive = self.cache / "synthetic.runtime" / "1.2.3" / "synthetic.runtime.1.2.3.nupkg"
        archive.parent.mkdir(parents=True)
        self.create_source({"runtime-only.dll": b"input not retained"}, "Synthetic.Runtime", archive=archive)
        Path(str(archive) + ".sha512").write_text(base64.b64encode(hashlib.sha512(archive.read_bytes()).digest()).decode())
        self.graph["project"]["frameworks"] = {"net10.0": {"downloadDependencies": [{"name": "Synthetic.Runtime", "version": "[1.2.3, 1.2.3]"}]}}
        self.write_graph()
        self.assertEqual(0, self.run_export())
        self.assertFalse(any(c.get("name") == "Synthetic.Runtime" for c in self.read_bom()["components"]))
        source = next(c for c in input_inventory(self.read_bom()) if c.get("name") == "Synthetic.Runtime")
        self.assertNotIn("scope", source)
        self.assertIn({"name": "wintodo:package-hash-verification", "value": "raw-sha512-local-sidecar; sdk-download-content-hash-not-declared; not-independent-source-authentication"}, source["properties"])
        Path(str(archive) + ".sha512").write_text("bad")
        self.assertEqual(1, self.run_export())

    def signed_cache_fixture(self):
        self.create_source({"vendor.dll": self.binary, ".signature.p7s": b"synthetic signature, not authenticated by this exporter"})
        normalized = base64.b64encode(hashlib.sha512(b"synthetic unsigned package content").digest()).decode()
        self.assertNotEqual(normalized, self.source_hash())
        Path(str(self.archive) + ".sha512").write_text(self.source_hash())
        metadata = self.archive.parent / ".nupkg.metadata"
        record = {"version": 2, "contentHash": normalized, "source": "https://api.nuget.org/v3/index.json"}
        metadata.write_text(json.dumps(record))
        self.graph["libraries"]["Synthetic.Native/1.2.3"]["sha512"] = normalized
        self.write_graph()
        return metadata, record

    def test_signed_archive_raw_and_normalized_hashes_are_distinguished(self):
        metadata, _ = self.signed_cache_fixture()
        self.assertEqual(0, self.run_export())
        source = next(c for c in self.read_bom()["components"] if c["type"] == "library")
        self.assertIn({"name": "wintodo:package-hash-verification", "value": "raw-sha512-local-sidecar; assets-content-hash-via-local-cache-metadata; not-independent-source-authentication"}, source["properties"])
        self.assertIn({"name": "wintodo:cache-metadata-sha256", "value": hashlib.sha256(metadata.read_bytes()).hexdigest()}, source["properties"])
        self.assertIn({"alg": "SHA-512", "content": hashlib.sha512(self.archive.read_bytes()).hexdigest()}, source["hashes"])

    def test_signed_archive_mismatched_metadata_and_raw_sidecar_are_rejected(self):
        metadata, record = self.signed_cache_fixture()
        for key, value in (("contentHash", self.source_hash()), ("version", "2"), ("source", "https://private-owner.example/feed")):
            changed = dict(record)
            changed[key] = value
            metadata.write_text(json.dumps(changed))
            self.assertEqual(1, self.run_export())
            self.assertNotIn("private-owner", self.output.read_text())
        metadata.write_text(json.dumps(record))
        Path(str(self.archive) + ".sha512").write_text(base64.b64encode(b"x" * 64).decode())
        self.assertEqual(1, self.run_export())

    def test_signed_archive_missing_content_hash_binding_is_rejected(self):
        metadata, _ = self.signed_cache_fixture()
        metadata.unlink()
        self.assertEqual(1, self.run_export())


if __name__ == "__main__":
    unittest.main()
