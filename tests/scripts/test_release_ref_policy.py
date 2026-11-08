"""Exercise release eligibility against disposable real Git histories, without network or signing."""

import contextlib
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location("release_ref_policy", Path(__file__).parents[2] / "scripts" / "Test-ReleaseRefPolicy.py")
policy = importlib.util.module_from_spec(spec)
spec.loader.exec_module(policy)
XML = '<Package xmlns="' + policy.NS + '"><Identity Name="Synthetic.App" Publisher="CN=Synthetic" Version="1.0.1.0"/></Package>\n'


class ReleaseRefPolicyTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.repo = self.root / "repository"
        self.repo.mkdir()
        self.environment = {k: v for k, v in os.environ.items() if not k.upper().startswith("GIT_")}
        self.environment.update(GIT_CONFIG_NOSYSTEM="1", GIT_CONFIG_GLOBAL=os.devnull)
        self.git("init", "--initial-branch=main")
        self.git("config", "user.name", "Synthetic")
        self.git("config", "user.email", "synthetic@example.invalid")
        self.git("config", "core.autocrlf", "false")
        self.manifest = self.repo / policy.MANIFEST
        self.manifest.parent.mkdir(parents=True)
        self.manifest.write_text(XML, encoding="utf-8", newline="\n")
        (self.repo / ".gitignore").write_text("artifacts/\nSafeTestResults/\n", encoding="utf-8")
        self.git("add", ".")
        self.git("commit", "-m", "synthetic source")
        self.source = self.git("rev-parse", "HEAD")
        self.git("tag", "v1.0.1")
        self.git("update-ref", "refs/remotes/origin/main", self.source)
        self.inputs = {"repository": self.repo, "event_name": "push", "ref": "refs/tags/v1.0.1",
                       "source_commit": self.source, "reviewed_main_commit": self.source}

    def git(self, *arguments, input=None):
        result = subprocess.run(["git", "-C", str(self.repo), *arguments], input=input,
                                env=self.environment, capture_output=True, check=False)
        self.assertEqual(0, result.returncode, result.stderr.decode(errors="replace"))
        return result.stdout.decode().strip()

    def inspect(self, **changes):
        return policy.inspect_policy(**(self.inputs | changes))

    def reject(self, category, **changes):
        with self.assertRaises(policy.InvalidPolicy) as caught:
            self.inspect(**changes)
        self.assertEqual(category, str(caught.exception))

    def new_commit(self):
        (self.repo / "synthetic.txt").write_text("newer source\n", encoding="utf-8")
        self.git("add", ".")
        self.git("commit", "-m", "newer reviewed main")
        return self.git("rev-parse", "HEAD")

    def args(self, output=None):
        args = ["--repository", str(self.repo), "--event-name", "push", "--ref", "refs/tags/v1.0.1",
                "--source-commit", self.source, "--reviewed-main-commit", self.source]
        return args + (["--output", str(output)] if output is not None else [])

    def cli(self, args):
        capture = io.StringIO()
        with contextlib.redirect_stdout(capture):
            code = policy.main(args)
        output = capture.getvalue()
        self.assertNotIn(str(self.root), output)
        self.assertNotIn("private-owner", output)
        return code, json.loads(output)

    def test_lightweight_tag_exact_manifest_and_pending_authority(self):
        report = self.inspect()
        self.assertEqual("PASS", report["status"])
        self.assertEqual("1.0.1.0", report["package_version"])
        self.assertEqual(hashlib.sha256(self.manifest.read_bytes()).hexdigest(), report["manifest_sha256"])
        self.assertFalse(report["release_authorized"])
        self.assertEqual("PENDING", report["previous_version_check"])
        self.assertIn("hosted_execution_and_ref_protection", report["pending_gates"])
        self.assertIn("owner_release_identity_and_first_version", report["pending_gates"])

    def test_annotated_tag_is_supported_without_claiming_tag_signature(self):
        self.git("tag", "-d", "v1.0.1")
        self.git("tag", "-a", "v1.0.1", "-m", "synthetic annotated tag")
        self.assertEqual("PASS", self.inspect()["status"])

    def test_source_ancestor_of_pinned_main_is_allowed(self):
        newer = self.new_commit()
        self.git("update-ref", "refs/remotes/origin/main", newer)
        self.git("checkout", "--detach", self.source)
        self.assertEqual("PASS", self.inspect(reviewed_main_commit=newer)["status"])

    def test_fixed_local_main_ref_is_supported(self):
        self.assertEqual("PASS", self.inspect(main_ref="refs/heads/main")["status"])
        self.reject("invalid_main_ref", main_ref="refs/heads/private-owner")

    def test_nonproduction_events_are_ineligible(self):
        for event in ("pull_request", "pull_request_target", "workflow_dispatch", "schedule", "private-owner", ""):
            with self.subTest(event=event): self.reject("ineligible_event", event_name=event)

    def test_noncanonical_and_injected_refs_are_rejected(self):
        for ref in ("v1.0.1", "refs/heads/main", "refs/tags/v01.0.1", "refs/tags/v1.00.1",
                    "refs/tags/v1.0.1-rc.1", "refs/tags/v1.0.1+build", "refs/tags/v1.0.1.0",
                    "refs/tags/v1.0.1\nprivate-owner", "refs/tags/v1.0.1;touch private-owner",
                    "--help", "refs/tags/v1.0.1^{commit}"):
            with self.subTest(ref=ref): self.reject("invalid_release_ref", ref=ref)

    def test_msix_component_limits_and_nonzero_major(self):
        self.assertEqual("65535.65535.65535.0", policy.tag_version("refs/tags/v65535.65535.65535"))
        for ref in ("refs/tags/v0.1.0", "refs/tags/v65536.0.0", "refs/tags/v1.65536.0", "refs/tags/v1.0.65536"):
            with self.subTest(ref=ref), self.assertRaises(policy.InvalidPolicy): policy.tag_version(ref)

    def test_pins_require_full_lowercase_commit_ids(self):
        for source in (self.source[:12], "0" * 40, "G" * 40, "private-owner", "f" * 64):
            self.reject("invalid_source_pin", source_commit=source)
        self.reject("invalid_source_pin", reviewed_main_commit="")

    def test_tag_main_or_head_mismatch_rejects(self):
        newer = self.new_commit()
        self.reject("source_or_ref_binding_mismatch")
        self.git("checkout", "--detach", self.source)
        self.git("tag", "-f", "v1.0.1", newer)
        self.reject("source_or_ref_binding_mismatch")
        self.git("tag", "-f", "v1.0.1", self.source)
        self.git("update-ref", "refs/remotes/origin/main", newer)
        self.reject("source_or_ref_binding_mismatch")

    def test_unrelated_reviewed_main_rejects(self):
        unrelated = self.git("commit-tree", self.source + "^{tree}", input=b"unrelated root\n")
        self.git("update-ref", "refs/remotes/origin/main", unrelated)
        self.reject("source_not_on_reviewed_main", reviewed_main_commit=unrelated)

    def test_missing_tag_or_main_objects_fail(self):
        self.git("tag", "-d", "v1.0.1")
        self.reject("git_command_failed_or_unbounded")
        self.git("tag", "v1.0.1")
        self.git("update-ref", "-d", "refs/remotes/origin/main")
        self.reject("git_command_failed_or_unbounded")

    def test_dirty_tracked_untracked_and_ignored_outputs(self):
        self.manifest.write_text(XML + " ", encoding="utf-8")
        self.reject("dirty_worktree")
        self.git("checkout", "--", policy.MANIFEST)
        extra = self.repo / "unreviewed.txt"
        extra.write_text("unreviewed", encoding="utf-8")
        self.reject("dirty_worktree")
        extra.unlink()
        (self.repo / "artifacts").mkdir()
        (self.repo / "artifacts/evidence.json").write_text("{}", encoding="utf-8")
        self.assertEqual("PASS", self.inspect()["status"])

    def test_crlf_modified_manifest_is_rejected(self):
        (self.repo / ".git/info/attributes").write_text(policy.MANIFEST + " text\n", encoding="utf-8")
        self.manifest.write_bytes(self.manifest.read_bytes().replace(b"\n", b"\r\n"))
        with self.assertRaises(policy.InvalidPolicy) as caught: self.inspect()
        self.assertIn(str(caught.exception), ("dirty_worktree", "manifest_worktree_bytes_mismatch"))

    def test_explicit_lf_attribute_preserves_real_autocrlf_checkout(self):
        (self.repo / ".gitattributes").write_text("*.appxmanifest text eol=lf\n", encoding="utf-8")
        self.git("add", ".")
        self.git("commit", "-m", "preserve manifest source bytes")
        source = self.git("rev-parse", "HEAD")
        self.git("tag", "-f", "v1.0.1", source)
        self.git("update-ref", "refs/remotes/origin/main", source)
        self.git("config", "core.autocrlf", "true")
        self.manifest.unlink()
        self.git("checkout", "--", policy.MANIFEST)
        self.assertEqual(XML.encode(), self.manifest.read_bytes())
        self.assertEqual("PASS", self.inspect(source_commit=source, reviewed_main_commit=source)["status"])

    def test_committed_manifest_version_must_match_tag(self):
        self.manifest.write_text(XML.replace("1.0.1.0", "1.0.1.4"), encoding="utf-8")
        self.git("add", ".")
        self.git("commit", "-m", "synthetic version mismatch")
        source = self.git("rev-parse", "HEAD")
        self.git("tag", "-f", "v1.0.1", source)
        self.git("update-ref", "refs/remotes/origin/main", source)
        self.reject("manifest_version_mismatch", source_commit=source, reviewed_main_commit=source)

    def test_monotonicity_uses_explicit_previous_version(self):
        self.assertEqual("PASS", self.inspect(previous_version="1.0.0.5")["previous_version_check"])
        for previous in ("1.0.1.0", "1.0.2.0", "2.0.0.0"):
            self.reject("version_not_monotonic", previous_version=previous)
        self.reject("invalid_package_version", previous_version="private-owner")

    def test_assume_unchanged_and_skip_worktree_cannot_hide_edits(self):
        for flag, undo in (("--assume-unchanged", "--no-assume-unchanged"), ("--skip-worktree", "--no-skip-worktree")):
            with self.subTest(flag=flag):
                self.git("update-index", flag, "--", policy.MANIFEST)
                self.manifest.write_bytes(b"private-owner concealed modification")
                self.reject("hidden_or_incomplete_index")
                self.git("update-index", undo, "--", policy.MANIFEST)
                self.git("checkout", "--", policy.MANIFEST)

    def test_manifest_reads_are_bounded(self):
        with patch.object(policy, "MAX_XML", 8), self.assertRaisesRegex(policy.InvalidPolicy, "invalid_manifest"):
            policy.read_manifest(self.manifest)

    def test_submodule_gitlink_index_is_unsupported(self):
        self.git("update-index", "--add", "--cacheinfo", "160000," + self.source + ",synthetic-submodule")
        self.git("commit", "-m", "synthetic gitlink")
        source = self.git("rev-parse", "HEAD")
        self.git("tag", "-f", "v1.0.1", source)
        self.git("update-ref", "refs/remotes/origin/main", source)
        self.reject("hidden_or_incomplete_index", source_commit=source, reviewed_main_commit=source)

    def test_shallow_grafted_replaced_and_partial_history_rejects(self):
        metadata = self.repo / ".git"
        (metadata / "shallow").write_text(self.source + "\n", encoding="ascii")
        self.reject("incomplete_or_substituted_history")
        (metadata / "shallow").unlink()
        (metadata / "info/grafts").write_text(self.source + "\n", encoding="ascii")
        self.reject("incomplete_or_substituted_history")
        (metadata / "info/grafts").unlink()
        newer = self.new_commit()
        self.git("checkout", "--detach", self.source)
        self.git("replace", self.source, newer)
        self.reject("incomplete_or_substituted_history")
        self.git("replace", "-d", self.source)
        self.git("config", "extensions.partialclone", "origin")
        self.reject("incomplete_or_substituted_history")

    def test_inherited_git_environment_is_discarded(self):
        with patch.dict(os.environ, {"GIT_DIR": "private-owner", "GIT_WORK_TREE": "private-owner",
                                    "GIT_INDEX_FILE": "private-owner", "GIT_CONFIG_COUNT": "1",
                                    "GIT_CONFIG_KEY_0": "core.worktree", "GIT_CONFIG_VALUE_0": "private-owner"}):
            self.assertEqual("PASS", self.inspect()["status"])

    def test_included_partial_clone_config_is_rejected(self):
        included = self.repo / ".git/included.config"
        included.write_text("[extensions]\npartialclone = origin\n", encoding="utf-8")
        self.git("config", "include.path", str(included))
        self.reject("incomplete_or_substituted_history")

    def test_repo_fsmonitor_hook_is_disabled(self):
        self.git("config", "core.fsmonitor", "private-owner-nonexistent-hook")
        self.assertEqual("PASS", self.inspect()["status"])

    def test_ref_movement_during_inspection_rejects(self):
        newer = self.new_commit()
        self.git("checkout", "--detach", self.source)
        original = policy.snapshot
        calls = 0
        def moved(git, ref, main_ref):
            nonlocal calls
            calls += 1
            if calls == 2: self.git("tag", "-f", "v1.0.1", newer)
            return original(git, ref, main_ref)
        with patch.object(policy, "snapshot", side_effect=moved):
            self.reject("repository_changed_during_verification")

    def test_annotated_tag_object_change_with_same_commit_is_detected(self):
        original = policy.snapshot
        calls = 0
        def moved(git, ref, main_ref):
            nonlocal calls
            calls += 1
            if calls == 2: self.git("tag", "-f", "-a", "v1.0.1", "-m", "replacement same source")
            return original(git, ref, main_ref)
        with patch.object(policy, "snapshot", side_effect=moved):
            self.reject("repository_changed_during_verification")

    def test_safe_report_and_stale_pass_failure(self):
        output = self.repo / "SafeTestResults/policy.json"
        output.parent.mkdir()
        code, report = self.cli(self.args(output))
        self.assertEqual(0, code)
        self.assertEqual("PASS", report["status"])
        self.assertEqual("PASS", json.loads(output.read_text())["status"])
        args = self.args(output)
        args[args.index("--event-name") + 1] = "private-owner"
        code, report = self.cli(args)
        self.assertEqual(1, code)
        self.assertEqual("FAIL", json.loads(output.read_text())["status"])

    def test_output_cannot_touch_tracked_files_hardlinks_or_outside_paths(self):
        before = self.manifest.read_bytes()
        (self.repo / "artifacts").mkdir()
        hardlink = self.repo / "artifacts/alias.json"
        os.link(self.manifest, hardlink)
        for output in (self.manifest, hardlink, self.root / "outside.json", self.repo / "artifacts/../outside.json"):
            with self.subTest(output=output):
                code, _ = self.cli(self.args(output))
                self.assertEqual(1, code)
                self.assertEqual(before, self.manifest.read_bytes())

    def test_ignored_but_force_tracked_report_cannot_be_overwritten(self):
        folder = self.repo / "artifacts"
        folder.mkdir()
        report = folder / "tracked.json"
        report.write_bytes(b"synthetic tracked evidence")
        self.git("add", "-f", "artifacts/tracked.json")
        self.git("commit", "-m", "synthetic tracked ignored path")
        code, _ = self.cli(self.args(report))
        self.assertEqual(1, code)
        self.assertEqual(b"synthetic tracked evidence", report.read_bytes())

    def test_output_symlink_is_rejected(self):
        folder = self.repo / "artifacts"
        folder.mkdir()
        link = folder / "alias.json"
        try: link.symlink_to(self.manifest)
        except OSError: self.skipTest("Symlink creation unavailable; hard-link alias protection remains covered.")
        code, _ = self.cli(self.args(link))
        self.assertEqual(1, code)
        self.assertEqual(XML.encode(), self.manifest.read_bytes())

    def test_failed_atomic_output_preserves_old_report_and_fails(self):
        folder = self.repo / "artifacts"
        folder.mkdir()
        output = folder / "policy.json"
        output.write_text('{"old": true}', encoding="utf-8")
        with patch.object(policy.os, "replace", side_effect=PermissionError("private-owner")):
            code, report = self.cli(self.args(output))
        self.assertEqual(1, code)
        self.assertEqual("FAIL", report["status"])
        self.assertEqual('{"old": true}', output.read_text())
        self.assertEqual([output], list(folder.iterdir()))

    def test_git_failure_deadline_and_invalid_args_are_sanitized(self):
        with patch.object(policy.subprocess, "run", side_effect=subprocess.TimeoutExpired("private-owner", 20)):
            code, report = self.cli(self.args())
        self.assertEqual(1, code)
        self.assertEqual("invalid_repository_or_input", report["findings"][0]["category"])
        code, report = self.cli(["--private-owner"])
        self.assertEqual(1, code)
        self.assertEqual("invalid_arguments", report["findings"][0]["category"])


if __name__ == "__main__":
    unittest.main()
