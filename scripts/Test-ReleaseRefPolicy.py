"""Check local stable-tag/source/version eligibility; never authorize a release."""

import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import tempfile
import xml.etree.ElementTree as ET

MANIFEST = "src/Wintodo.App/Package.appxmanifest"
NS = "http://schemas.microsoft.com/appx/manifest/foundation/windows10"
MAIN_REFS = ("refs/heads/main", "refs/remotes/origin/main")
PENDING = ("owner_release_identity_and_first_version", "hosted_execution_and_ref_protection",
           "source_to_package_provenance", "vendor_advisory_and_legal_closure",
           "production_signing_timestamp_and_trust", "final_signed_deployment",
           "explicit_publication_authorization")
MAX_GIT_OUTPUT = 2 * 1024 * 1024
MAX_XML = 1024 * 1024


class InvalidPolicy(ValueError):
    pass


class SafeParser(argparse.ArgumentParser):
    def error(self, message):
        raise InvalidPolicy("invalid_arguments")


def require(condition, category):
    if not condition:
        raise InvalidPolicy(category)


def commit_pin(value):
    return isinstance(value, str) and re.fullmatch(r"[0-9a-f]{40}", value) is not None and value != "0" * 40


def package_version(value):
    require(isinstance(value, str) and len(value) <= 23 and re.fullmatch(
        r"(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)", value), "invalid_package_version")
    parts = tuple(int(p) for p in value.split("."))
    require(0 < parts[0] <= 65535 and all(p <= 65535 for p in parts), "invalid_package_version")
    return parts


def tag_version(ref):
    require(isinstance(ref, str) and len(ref) <= 32 and re.fullmatch(
        r"refs/tags/v(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)", ref), "invalid_release_ref")
    value = ref.removeprefix("refs/tags/v") + ".0"
    package_version(value)
    return value


class Git:
    def __init__(self, repository):
        self.repository = Path(repository).resolve(strict=True)
        require(self.repository.is_dir(), "invalid_repository")
        self.environment = {k: v for k, v in os.environ.items() if not k.upper().startswith("GIT_")}
        self.environment.update(GIT_CONFIG_NOSYSTEM="1", GIT_CONFIG_GLOBAL=os.devnull,
                                GIT_NO_REPLACE_OBJECTS="1", GIT_NO_LAZY_FETCH="1",
                                GIT_OPTIONAL_LOCKS="0", GIT_TERMINAL_PROMPT="0")

    def run(self, *arguments, allowed=(0,)):
        with tempfile.TemporaryFile() as out, tempfile.TemporaryFile() as error:
            result = subprocess.run(["git", "--no-optional-locks", "-c", "core.fsmonitor=false",
                                     "-c", "core.untrackedCache=false", "-C", str(self.repository), *arguments],
                                    env=self.environment, shell=False, stdin=subprocess.DEVNULL,
                                    stdout=out, stderr=error, timeout=20, check=False)
            out.seek(0)
            data = out.read(MAX_GIT_OUTPUT + 1)
        require(result.returncode in allowed and len(data) <= MAX_GIT_OUTPUT, "git_command_failed_or_unbounded")
        return result.returncode, data

    def text(self, *arguments):
        return self.run(*arguments)[1].decode("utf-8", errors="strict").strip()

    def path(self, *arguments):
        value = Path(self.text("rev-parse", *arguments))
        return (self.repository / value).resolve()

    def commit(self, value):
        result = self.text("rev-parse", "--verify", "--end-of-options", value + "^{commit}")
        require(commit_pin(result), "invalid_commit_object")
        return result


def assert_repository(git):
    require(git.text("rev-parse", "--is-inside-work-tree") == "true"
            and git.path("--show-toplevel") == git.repository, "invalid_repository")
    require(git.text("rev-parse", "--is-shallow-repository") == "false", "incomplete_or_substituted_history")
    require(not git.text("for-each-ref", "--format=%(refname)", "refs/replace")
            and not git.path("--git-path", "info/grafts").exists(), "incomplete_or_substituted_history")
    code, _ = git.run("config", "--includes", "--get", "extensions.partialclone", allowed=(0, 1))
    require(code == 1, "incomplete_or_substituted_history")


def snapshot(git, ref, main_ref):
    return (git.commit("HEAD"), git.commit(ref), git.commit(main_ref),
            git.run("status", "--porcelain=v1", "--untracked-files=all", "--ignore-submodules=none")[1],
            git.text("rev-parse", "--verify", "--end-of-options", ref),
            git.run("ls-files", "-v", "-z")[1], git.run("ls-files", "--stage", "-z")[1])


def read_manifest(path):
    with path.open("rb") as stream:
        data = stream.read(MAX_XML + 1)
    require(len(data) <= MAX_XML, "invalid_manifest")
    return data


def inspect_policy(repository, event_name, ref, source_commit, reviewed_main_commit,
                   main_ref="refs/remotes/origin/main", previous_version=None):
    require(event_name == "push", "ineligible_event")
    mapped_version = tag_version(ref)
    require(commit_pin(source_commit) and commit_pin(reviewed_main_commit), "invalid_source_pin")
    require(main_ref in MAIN_REFS, "invalid_main_ref")
    previous = package_version(previous_version) if previous_version is not None else None
    require(previous is None or package_version(mapped_version) > previous, "version_not_monotonic")
    git = Git(repository)
    assert_repository(git)
    # Requiring the supplied hashes themselves to resolve unchanged excludes
    # abbreviated/object-name substitutions even when the tag points correctly.
    require(git.commit(source_commit) == source_commit and git.commit(reviewed_main_commit) == reviewed_main_commit,
            "source_pin_not_exact_commit")
    before = snapshot(git, ref, main_ref)
    require(before[:3] == (source_commit, source_commit, reviewed_main_commit), "source_or_ref_binding_mismatch")
    require(all(record.startswith(b"H ") for record in before[5].split(b"\0") if record), "hidden_or_incomplete_index")
    require(all(record.startswith((b"100644 ", b"100755 ")) and record.split(b"\t", 1)[0].endswith(b" 0")
                for record in before[6].split(b"\0") if record), "hidden_or_incomplete_index")
    require(not before[3], "dirty_worktree")
    code, _ = git.run("merge-base", "--is-ancestor", source_commit, reviewed_main_commit, allowed=(0, 1))
    require(code == 0, "source_not_on_reviewed_main")
    blob = git.run("show", source_commit + ":" + MANIFEST)[1]
    require(0 < len(blob) <= MAX_XML and b"<!DOCTYPE" not in blob.upper()
            and b"<!ENTITY" not in blob.upper(), "invalid_manifest")
    text = blob.decode("utf-8-sig")
    require("\0" not in text, "invalid_manifest")
    manifest = ET.fromstring(text)
    identities = manifest.findall("{" + NS + "}Identity")
    require(manifest.tag == "{" + NS + "}Package" and len(identities) == 1, "invalid_manifest")
    require(package_version(identities[0].get("Version")) == package_version(mapped_version), "manifest_version_mismatch")
    manifest_path = git.repository / MANIFEST
    require(read_manifest(manifest_path) == blob, "manifest_worktree_bytes_mismatch")
    require(snapshot(git, ref, main_ref) == before, "repository_changed_during_verification")
    assert_repository(git)
    require(read_manifest(manifest_path) == blob, "repository_changed_during_verification")
    return {"status": "PASS", "scope": "local_release_ref_eligibility", "release_authorized": False,
            "event": "push", "version_mapping": "vMAJOR.MINOR.PATCH_to_MAJOR.MINOR.PATCH.0",
            "package_version": mapped_version, "source_commit": source_commit,
            "reviewed_main_commit": reviewed_main_commit, "manifest_sha256": hashlib.sha256(blob).hexdigest(),
            "tag_to_source_binding": "MATCH", "reviewed_main_ancestry": "MATCH",
            "source_worktree": "CLEAN_WITH_EXACT_MANIFEST_BYTES", "previous_version_check": "PASS" if previous else "PENDING",
            "pending_gates": list(PENDING), "findings": []}


def safe_output(output, repository):
    output = Path(output).absolute()
    git = Git(repository)
    require(output.suffix.lower() == ".json" and output.parent.is_dir(), "unsafe_report_output")
    try:
        relative = output.relative_to(git.repository)
    except ValueError:
        raise InvalidPolicy("unsafe_report_output") from None
    require(relative.parts and ".." not in relative.parts, "unsafe_report_output")
    for path in (output, *output.parents):
        require(not path.is_symlink() and not path.is_junction(), "unsafe_report_output")
        if path == git.repository:
            break
    require(not output.exists() or (output.is_file() and output.stat().st_nlink == 1), "unsafe_report_output")
    require(git.run("check-ignore", "--quiet", "--", relative.as_posix(), allowed=(0, 1))[0] == 0, "unsafe_report_output")
    return output


def write_report(output, result, repository):
    output = safe_output(output, repository)
    temporary = None
    try:
        with tempfile.NamedTemporaryFile("w", encoding="utf-8", dir=output.parent, delete=False) as stream:
            temporary = Path(stream.name)
            json.dump(result, stream, indent=2)
            stream.write("\n")
        safe_output(output, repository)
        os.replace(temporary, output)
    finally:
        if temporary is not None:
            temporary.unlink(missing_ok=True)


def failure(category):
    return {"status": "FAIL", "scope": "local_release_ref_eligibility", "release_authorized": False,
            "pending_gates": list(PENDING), "findings": [{"category": category}]}


def main(argv=None):
    parser = SafeParser(prog="Test-ReleaseRefPolicy.py", description=__doc__)
    parser.add_argument("--repository", required=True)
    parser.add_argument("--event-name", required=True)
    parser.add_argument("--ref", required=True)
    parser.add_argument("--source-commit", required=True)
    parser.add_argument("--reviewed-main-commit", required=True)
    parser.add_argument("--main-ref", default=MAIN_REFS[1])
    parser.add_argument("--previous-version")
    parser.add_argument("--output")
    try:
        args = parser.parse_args(argv)
    except InvalidPolicy:
        print(json.dumps(failure("invalid_arguments")))
        return 1
    try:
        if args.output:
            safe_output(args.output, args.repository)
        result = inspect_policy(args.repository, args.event_name, args.ref, args.source_commit,
                                args.reviewed_main_commit, args.main_ref, args.previous_version)
    except Exception as error:
        result = failure(str(error) if isinstance(error, InvalidPolicy) else "invalid_repository_or_input")
    if args.output:
        try:
            write_report(args.output, result, args.repository)
        except Exception:
            result = failure("report_output_rejected_or_unwritable")
    print(json.dumps(result))
    return 0 if result["status"] == "PASS" else 1


if __name__ == "__main__":
    raise SystemExit(main())
