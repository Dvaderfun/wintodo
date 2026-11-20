"""Synthetic feed and pin checks; no network or machine state is needed in CI."""

import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

MODULE = Path(__file__).resolve().parents[2] / "scripts/Check-ProjectHealth.py"
SPEC = importlib.util.spec_from_file_location("project_health", MODULE)
health = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(health)


class HealthTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)

    def put(self, name, value):
        path = self.root / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(value if isinstance(value, str) else json.dumps(value), encoding="utf-8")

    def test_exact_listed_result_is_required_and_preview_is_not_accepted(self):
        source = {"totalHits": 1, "data": [{"id": "Example.part_1-x", "version": "1.0.10"}]}
        with patch.object(health, "data", return_value=source) as get:
            self.assertEqual(("1.0.2", "1.0.10", False), health.check_nuget("Example.part_1-x", "1.0.2"))
            self.assertIn("prerelease=false", get.call_args.args[0])
            source["data"][0]["version"] = "2.0.0-preview.1"
            with self.assertRaises(ValueError):
                health.check_nuget("Example.part_1-x", "1.0.2")
            source["data"][0] = {"id": "other", "version": "2.0.0"}
            with self.assertRaises(ValueError):
                health.check_nuget("Example.part_1-x", "1.0.2")

    def test_incomplete_release_never_masks_other_findings_or_discloses_errors(self):
        def offline():
            raise OSError("private user path")

        rows = [health.result("SDK", "Review pin.", lambda: ("10.0.1", "10.0.2", False))]
        self.assertEqual(1, health.exit_code(rows))
        rows.append(health.result("SQLite", "Review lock.", offline))
        self.assertEqual(2, health.exit_code(rows))
        self.assertNotIn("private", health.markdown(rows))

    def test_retagged_source_and_moved_origin_are_not_current_even_at_same_version(self):
        current = "1" * 40
        tag_line = "- Tag: `v0.8.0`\n"
        commit_line = f"- Commit: `{current}`\n"
        for origin, wanted, same in [
            ("microsoft/win-dev-skills", current, False),
            ("microsoft/WinAppCli", "2" * 40, False),
            ("microsoft/WinAppCli", current, True),
        ]:
            with self.subTest(origin=origin, wanted=wanted):
                self.put(".agents/skills/VENDORED.md",
                         f"- Source: https://github.com/{origin} (skills)\n" + tag_line + commit_line)
                with patch.object(health, "release", return_value="v0.8.0"), \
                     patch.object(health, "tag_commit", return_value=wanted):
                    self.assertEqual(same, health.check_skills(self.root)[2])

    def test_sqlite_cannot_be_current_from_only_one_architecture_or_one_shape(self):
        def write(arch, shape, ver):
            self.put(f"src/Wintodo.App/packages.{shape}{arch}.lock.json",
                     {"dependencies": {f"net10/{arch}": {"SQLite": {"resolved": ver}}}})

        for arch in ("win-x64", "win-arm64"):
            for shape in ("aot.", ""):
                write(arch, shape, "3.54.1")
        with patch.object(health, "fetch", return_value="PRODUCT,3.54.1,2026/sqlite-src-3540100.zip,99," + "a" * 64):
            self.assertTrue(health.check_sqlite(self.root)[2])
            write("win-arm64", "aot.", "3.53.3")
            row = health.result("SQLite", "Review locks.", health.check_sqlite, self.root)
            self.assertEqual("incomplete", row["status"])

    def test_release_digest_drift_is_not_current(self):
        self.put("scripts/gitleaks-pin.json", {"version": "8.30.1", "sha256": "a" * 64})
        checksums = "b" * 64 + "  gitleaks_8.30.1_windows_x64.zip\n"
        with patch.object(health, "release", return_value="v8.30.1"), patch.object(health, "fetch", return_value=checksums):
            self.assertFalse(health.check_scanner(self.root)[2])

    def test_git_refs_peel_an_exact_annotated_tag(self):
        refs = "001e# service=git-upload-pack\n" + "".join(
            "0042" + sha * 40 + " " + name + "\n" for name, sha in (
                ("refs/tags/v1.0.00", "1"), ("refs/heads/v1.0.0", "2"),
                ("refs/tags/v1.0.0", "3"), ("refs/tags/v1.0.0^{}", "4")))
        with patch.object(health, "fetch", return_value=refs):
            self.assertEqual("4" * 40, health.tag_commit("actions/checkout", "v1.0.0"))

    def test_pins_cover_repeated_action_use_and_reject_floating_tags(self):
        repo = "actions/checkout"
        pin = "a" * 40
        self.put(".github/workflows/ci.yml", "  - uses: actions/checkout@" + pin)
        self.put(".github/workflows/weekly.yml", "  - uses: actions/checkout@v7")
        with patch.object(health, "release", return_value="v7.0.1"), \
             patch.object(health, "tag_commit", return_value=pin):
            self.assertEqual("incomplete", health.result(repo, "Review.", health.check_action, self.root, repo)["status"])
            self.put(".github/workflows/weekly.yml", "  - uses: actions/checkout@" + pin)
            self.assertTrue(health.check_action(self.root, repo)[2])

    def test_sdk_uses_pinned_lts_channel_and_never_rolls_forward(self):
        self.put("global.json", {"sdk": {"version": "10.0.401", "rollForward": "disable", "allowPrerelease": False}})
        source = {"channel-version": "10.0", "release-type": "lts", "latest-sdk": "10.0.401", "support-phase": "active"}
        with patch.object(health, "data", return_value=source) as get:
            self.assertTrue(health.check_sdk(self.root)[2])
            self.assertIn("/10.0/", get.call_args.args[0])
            source["support-phase"] = "eol"
            self.assertFalse(health.check_sdk(self.root)[2])
            source["channel-version"] = "11.0"
            self.assertEqual("incomplete", health.result("SDK", "Review.", health.check_sdk, self.root)["status"])
