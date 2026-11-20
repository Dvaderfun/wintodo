"""Prepare a reviewable skills/scanner update patch without writing the checkout or running upstream code."""

import argparse
from datetime import datetime, timezone
import difflib
from hashlib import sha256
from http.client import HTTPException
import json
import os
from pathlib import Path
import re
import runpy
import subprocess
import tempfile
from types import SimpleNamespace
from urllib.request import urlopen

H = SimpleNamespace(**runpy.run_path(str(Path(__file__).with_name("Check-ProjectHealth.py"))))
ROOT = Path(__file__).resolve().parents[1]
SKILLS = ("winui-setup", "winui-dev-workflow", "winui-design", "winui-code-review", "winui-ui-testing", "winui-packaging")
SOURCE = "plugins/winui/agent-plugin/skills"
MIRRORS = (".agents/skills", ".claude/skills")
MANIFEST = ".agents/skills/VENDORED.md"
UPDATER = "scripts/Update-AgentSkills.ps1"
PIN = "scripts/gitleaks-pin.json"
SCANNER = "gitleaks/gitleaks"


def git(root, *args, stdin=None, env=None):
    return subprocess.run(["git", "-C", str(root), *args], input=stdin, capture_output=True, check=True, env=env,
                          timeout=120).stdout


def text_blob(root, oid):
    H.require(int(git(root, "cat-file", "-s", oid)) <= 512 * 1024)
    value = git(root, "cat-file", "blob", oid).decode("utf-8")
    H.require("\0" not in value)
    return value


def tree(root, ref, prefix):
    paths = {}
    entries = git(root, "ls-tree", "-rz", ref, "--", *(prefix + "/" + skill for skill in SKILLS))
    for entry in entries.rstrip(b"\0").split(b"\0"):
        meta, path = entry.decode("utf-8").split("\t")
        mode, kind, oid = meta.split()
        path = path.removeprefix(prefix + "/")
        H.require(mode == "100644" and kind == "blob" and re.fullmatch(r"winui-[a-z-]+/(?:[a-zA-Z0-9_-]+/)*[a-zA-Z0-9_-]+\.md", path))
        H.require(path.split("/")[0] in SKILLS and path.casefold() not in (p.casefold() for p in paths))
        paths[path] = H.exact(H.SHA, oid)
    H.require(len(paths) <= 100 and all(skill + "/SKILL.md" in paths for skill in SKILLS))
    return paths


def public_tree(tmp, tag, expected):
    # Bare Git object reads only: no upstream checkout, hooks, filters or plugin/scanner installation.
    bare = tmp / tag
    git(tmp, "clone", "--quiet", "--bare", "--filter=blob:none", "--depth", "1", "--single-branch",
        "--branch", tag, "https://github.com/microsoft/WinAppCli.git", str(bare))
    actual = git(bare, "rev-parse", "refs/tags/" + tag + "^{commit}").decode().strip()
    H.require(actual == expected)
    return bare, tree(bare, actual, SOURCE)


def replace_once(text, old, new):
    H.require(text.count(old) == 1)
    return text.replace(old, new)


def skill_changes(root, tmp):
    manifest = text_blob(root, "HEAD:" + MANIFEST)
    H.require("- Source: https://github.com/microsoft/WinAppCli (" in manifest)
    old = re.search(r"(?m)^- Tag: `(v\d+\.\d+\.\d+)`\r?$", manifest)[1]
    sha = re.search(r"(?m)^- Commit: `([0-9a-f]{40})`\r?$", manifest)[1]
    tag = H.release(H.SKILLS)
    H.require(H.version(tag[1:]) >= H.version(old[1:]) and H.tag_commit(H.SKILLS, old) == sha)
    before, current = public_tree(tmp, old, sha)
    H.require(all(tree(root, "HEAD", m) == current for m in MIRRORS))
    updater = text_blob(root, "HEAD:" + UPDATER)
    tag_default, sha_default = f'$Tag = "{old}"', f'$ExpectedCommit = "{sha}"'
    H.require(updater.count(tag_default) == updater.count(sha_default) == 1)
    if old == tag:
        return {}
    wanted = H.tag_commit(H.SKILLS, tag)
    after, candidate = public_tree(tmp, tag, wanted)
    next_manifest = replace_once(replace_once(manifest, f"- Tag: `{old}`", f"- Tag: `{tag}`"),
                                 f"- Commit: `{sha}`", f"- Commit: `{wanted}`")
    date = re.search(r"(?m)^- Vendored: \d{4}-\d{2}-\d{2}(?=\r?$)", next_manifest)[0]
    next_manifest = replace_once(next_manifest, date, f"- Vendored: {datetime.now(timezone.utc).date()}")
    next_updater = updater.replace(tag_default, f'$Tag = "{tag}"').replace(sha_default, f'$ExpectedCommit = "{wanted}"')
    edits = {MANIFEST: (manifest, next_manifest), UPDATER: (updater, next_updater)}
    for name in sorted(current.keys() | candidate.keys()):
        if current.get(name) == candidate.get(name):
            continue
        # Only public publisher Markdown may enter the patch, including its removed lines.
        versions = (text_blob(before, current[name]) if name in current else None,
                    text_blob(after, candidate[name]) if name in candidate else None)
        for mirror in MIRRORS:
            edits[f"{mirror}/{name}"] = versions
    print(f"WinUI skills: {old} -> {tag}, upstream commit {wanted}.")
    return edits


def checksum(tag):
    asset = f"gitleaks_{tag[1:]}_windows_x64.zip"
    base = f"https://github.com/{SCANNER}/releases/download/{tag}/"
    manifest = H.fetch(base + f"gitleaks_{tag[1:]}_checksums.txt")
    digests = re.findall(r"(?m)^([a-f0-9]{64}) +\*?" + re.escape(asset) + r"\r?$", manifest)
    H.require(len(digests) == 1)
    return base + asset, digests[0]


def verify_archive(url, expected):
    digest = sha256()
    count = 0
    with urlopen(url, timeout=20) as response:
        while chunk := response.read(65536):
            count += len(chunk)
            H.require(count <= 64 * 1024 * 1024)
            digest.update(chunk)
    H.require(count > 0 and digest.hexdigest() == expected)


def scanner_changes(root):
    content = text_blob(root, "HEAD:" + PIN)
    pin = json.loads(content)
    H.require(set(pin) == {"version", "sha256"})
    old = "v" + H.exact(r"\d+\.\d+\.\d+", pin["version"])
    H.require(checksum(old)[1] == H.exact(r"[0-9a-f]{64}", pin["sha256"]))
    tag = H.release(SCANNER)
    H.require(H.version(tag[1:]) >= H.version(old[1:]))
    if tag == old:
        return {}
    commit = H.tag_commit(SCANNER, tag)
    url, digest = checksum(tag)
    verify_archive(url, digest)
    print(f"Gitleaks: {old} -> {tag}, upstream commit {commit}, Windows x64 ZIP SHA-256 {digest}.")
    return {PIN: (content, json.dumps({"version": tag[1:], "sha256": digest}, indent=2) + "\n")}


def make_patch(changes):
    patch = []
    for path, (a, b) in sorted(changes.items()):
        patch.append(f"diff --git a/{path} b/{path}\n")
        if a is None or b is None:
            patch.append("new file mode 100644\n" if a is None else "deleted file mode 100644\n")
        diff = difflib.unified_diff((a or "").splitlines(True), (b or "").splitlines(True),
                                    fromfile="/dev/null" if a is None else "a/" + path,
                                    tofile="/dev/null" if b is None else "b/" + path)
        for line in diff:
            patch.append(line if line.endswith("\n") else line + "\n\\ No newline at end of file\n")
    result = "".join(patch).encode("utf-8")
    H.require(len(result) <= 4 * 1024 * 1024)
    return result


def prepare(root, output):
    H.require(not output.exists())
    with tempfile.TemporaryDirectory() as folder:
        tmp = Path(folder)
        changes = skill_changes(root, tmp) | scanner_changes(root)
        if not changes:
            print("Both pins and WinUI mirrors match; no patch.")
            return
        patch = make_patch(changes)
        # Check against the committed tree without touching the real index or working files.
        env = {**os.environ, "GIT_INDEX_FILE": str(tmp / "proof-index")}
        git(root, "read-tree", "HEAD", env=env)
        git(root, "apply", "--check", "--cached", "-", stdin=patch, env=env)
        with output.open("xb") as stream:
            stream.write(patch)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True, help="new file in an existing output directory")
    try:
        prepare(ROOT, parser.parse_args().output)
    except (OSError, HTTPException, TypeError, ValueError, IndexError, KeyError, subprocess.SubprocessError):
        raise SystemExit("Could not verify the release, current mirrors, or clean patch. Inspect both publishers before updating.")
