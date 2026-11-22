"""Exercise release drift with real local Git objects; no publisher network or executable."""

import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

SCRIPT = Path(__file__).resolve().parents[2] / "scripts/Prepare-VendorUpdates.py"
SPEC = importlib.util.spec_from_file_location("proposal", SCRIPT)
P = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(P)
GIT = P.git


def put(repo, path, contents):
    file = repo / path
    file.parent.mkdir(parents=True, exist_ok=True)
    file.write_text(contents, encoding="utf-8")


def commit(repo):
    GIT(repo, "add", "-A")
    GIT(repo, "-c", "commit.gpgSign=false", "-c", "user.name=test", "-c", "user.email=test@example.invalid",
        "commit", "-qm", "fixture")
    return GIT(repo, "rev-parse", "HEAD").decode().strip()


class ProposalTests(unittest.TestCase):
    def setUp(self):
        scope = tempfile.TemporaryDirectory()
        self.addCleanup(scope.cleanup)
        self.base = Path(scope.name)
        self.root = self.base / "source"
        self.public = self.base / "vendor"
        for repo in (self.root, self.public):
            repo.mkdir()
            GIT(repo, "init", "--quiet")
            GIT(repo, "config", "core.autocrlf", "false")
        for skill in P.SKILLS:
            for repo, prefixes in [(self.root, P.MIRRORS), (self.public, [P.SOURCE])]:
                for prefix in prefixes:
                    put(repo, f"{prefix}/{skill}/SKILL.md", "original instructions\n")
        put(self.public, "LICENSE", "original upstream notice\n")
        for mirror in P.MIRRORS:
            put(self.root, mirror + "/LICENSE", "original upstream notice\n")
        self.old = commit(self.public)
        GIT(self.public, "tag", "v1.0.0")
        put(self.public, P.SOURCE + "/winui-design/SKILL.md", "next public instructions\n")
        put(self.public, P.SOURCE + "/winui-design/references/new.md", "public reference\n")
        put(self.public, "LICENSE", "updated upstream notice\n")
        self.new = commit(self.public)
        GIT(self.public, "tag", "v1.1.0")
        tick = chr(96)
        put(self.root, P.MANIFEST, f"- Source: https://github.com/microsoft/WinAppCli (skills)\n"
            f"- Tag: {tick}v1.0.0{tick}\n- Commit: {tick}{self.old}{tick}\n- Vendored: 2026-01-01\n")
        put(self.root, P.UPDATER, f'$Tag = "v1.0.0"\n$ExpectedCommit = "{self.old}"\n')
        self.prior = "1" * 64
        put(self.root, P.PIN, json.dumps({"version": "1.0.0", "sha256": self.prior}) + "\n")
        put(self.root, "private.txt", "must never enter the patch\n")
        commit(self.root)
        self.archive = self.base / "test.zip"
        self.archive.write_bytes(b"opaque zip fixture, never executed")
        self.digest = hashlib.sha256(self.archive.read_bytes()).hexdigest()
        self.output = self.base / "review.patch"
        self.latest = "v1.1.0"
        self.refs = {"v1.0.0": self.old, "v1.1.0": self.new}

        def local_git(root, *args, **kwargs):
            if args[0] == "clone":
                args = [self.public.as_uri() if a == "https://github.com/microsoft/WinAppCli.git" else a for a in args]
            return GIT(root, *args, **kwargs)

        for mock in (patch.object(P, "git", side_effect=local_git),
                     patch.object(P.H, "release", side_effect=lambda repo: self.latest),
                     patch.object(P.H, "tag_commit", side_effect=lambda repo, tag: self.refs[tag]),
                     patch.object(P, "checksum", side_effect=lambda tag: (
                         self.archive.as_uri(), self.prior if tag == "v1.0.0" else self.digest))):
            mock.start()
            self.addCleanup(mock.stop)

    def test_verified_update_is_an_applicable_patch_for_both_mirrors_and_scanner(self):
        P.prepare(self.root, self.output)
        self.assertEqual(b"", GIT(self.root, "status", "--porcelain"))
        patch_bytes = self.output.read_bytes()
        self.assertNotIn(b"must never enter", patch_bytes)
        GIT(self.root, "apply", "-", stdin=patch_bytes)
        for mirror in P.MIRRORS:
            self.assertEqual("next public instructions\n", (self.root / mirror / "winui-design/SKILL.md").read_text())
            self.assertEqual("public reference\n", (self.root / mirror / "winui-design/references/new.md").read_text())
            self.assertEqual(GIT(self.public, "cat-file", "blob", self.new + ":LICENSE"),
                             (self.root / mirror / "LICENSE").read_bytes())
        self.assertIn(self.new, (self.root / P.UPDATER).read_text())
        self.assertIn(self.new, (self.root / P.MANIFEST).read_text())
        self.assertEqual(self.digest, json.loads((self.root / P.PIN).read_text())["sha256"])

    def test_clean_is_noop_and_retag_or_mirror_drift_never_produces_an_artifact(self):
        self.latest = "v1.0.0"
        P.prepare(self.root, self.output)
        self.assertFalse(self.output.exists())
        self.refs["v1.0.0"] = self.new
        with self.assertRaises(ValueError):
            P.prepare(self.root, self.output)
        self.refs["v1.0.0"] = self.old
        put(self.root, P.MIRRORS[1] + "/winui-design/SKILL.md", "local override\n")
        commit(self.root)
        with self.assertRaises(ValueError):
            P.prepare(self.root, self.output)
        self.assertFalse(self.output.exists())

    def test_divergent_or_missing_license_blocks_even_a_noop(self):
        self.latest = "v1.0.0"
        license_file = self.root / P.MIRRORS[1] / "LICENSE"
        license_file.write_text("local-only license\n", encoding="utf-8")
        for state in ("divergent", "missing"):
            with self.subTest(state=state):
                if state == "missing":
                    license_file.unlink()
                commit(self.root)
                with self.assertRaises(ValueError):
                    P.prepare(self.root, self.output)
                self.assertFalse(self.output.exists())

    def test_asset_mismatch_prevents_even_a_valid_skill_update_being_written(self):
        self.archive.write_bytes(b"different")
        with self.assertRaises(ValueError):
            P.prepare(self.root, self.output)
        self.assertFalse(self.output.exists())
