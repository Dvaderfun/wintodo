"""Service an existing Windows x64 standalone CodeGraph; never initialize an index."""
import argparse
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import shutil
import subprocess
import sys
import tempfile
import urllib.request
import uuid
import zipfile


ORIGIN = "https://github.com/colbymchenry/codegraph/releases"
ASSET = "codegraph-win32-x64.zip"


def response(url):
    return urllib.request.urlopen(urllib.request.Request(url, headers={"User-Agent": "Wintodo-tool-health"}), timeout=120)


def owned(parent, child):
    if child.is_symlink() or child.is_junction() or child.resolve().parent != parent.resolve():
        raise ValueError("Unexpected CodeGraph install directory")
    return child


def installed_version(folder):
    return json.loads((folder / "lib/package.json").read_text(encoding="utf-8"))["version"]


def main():
    args = argparse.ArgumentParser()
    args.add_argument("--apply", action="store_true")
    options = args.parse_args()
    root = Path(os.environ["LOCALAPPDATA"]) / "codegraph"
    current = owned(root, root / "current")
    if sys.platform != "win32" or not (current / "bin/codegraph.cmd").is_file():
        raise RuntimeError("No supported Windows standalone installation")
    with response(ORIGIN + "/latest") as page:
        match = re.fullmatch(re.escape(ORIGIN) + r"/tag/v(\d+\.\d+\.\d+)", page.geturl(), re.I)
    if not match:
        raise RuntimeError("Unexpected stable release tag")
    version = match[1]
    before = installed_version(current)
    if not re.fullmatch(r"\d+\.\d+\.\d+", before):
        raise RuntimeError("Non-stable installed version")
    if tuple(map(int, before.split("."))) >= tuple(map(int, version.split("."))):
        print("CodeGraph", before, "(stable", version + ")")
        return 0
    if not options.apply:
        print("CodeGraph", before, "->", version)
        return 1
    url = ORIGIN + "/download/v" + version + "/"
    with response(url + "SHA256SUMS") as sums:
        hashes = re.findall(r"^([0-9a-fA-F]{64})\s+\*?" + re.escape(ASSET) + r"\s*$", sums.read(65536).decode(), re.M)
    if len(hashes) != 1:
        raise RuntimeError("Missing unambiguous publisher checksum")
    with tempfile.TemporaryDirectory(prefix=".wintodo-stage-", dir=root) as temporary:
        stage = owned(root, Path(temporary))
        archive = stage / "release.zip"
        with response(url + ASSET) as download, archive.open("xb") as output:
            shutil.copyfileobj(download, output)
        with archive.open("rb") as data:
            if hashlib.file_digest(data, "sha256").hexdigest() != hashes[0].lower():
                raise RuntimeError("Release checksum mismatch")
        with zipfile.ZipFile(archive) as package:
            seen = set()
            if sum(e.file_size for e in package.infolist()) > 1024**3:
                raise RuntimeError("Unexpected unpacked size")
            for entry in package.infolist():
                path = PurePosixPath(entry.filename)
                key = str(path).casefold()
                if (not path.parts or path.parts[0] != "codegraph-win32-x64" or ".." in path.parts
                        or "\\" in entry.filename or ":" in entry.filename or key in seen
                        or (entry.external_attr >> 16) & 0o170000 == 0o120000):
                    raise RuntimeError("Unexpected release archive member")
                seen.add(key)
            package.extractall(stage)
        candidate = owned(stage, stage / "codegraph-win32-x64")
        if installed_version(candidate) != version or not (candidate / "bin/codegraph.cmd").is_file():
            raise RuntimeError("Unexpected staged layout/version")
        observed = subprocess.check_output([candidate / "node.exe", "--liftoff-only", "--disable-warning=ExperimentalWarning",
                                            candidate / "lib/dist/bin/codegraph.js", "--version"], text=True, timeout=30).strip()
        if observed != version:
            raise RuntimeError("Staged CLI failed its version check")
        previous = owned(root, root / (".wintodo-previous-" + uuid.uuid4().hex))
        current.rename(previous)
        try:
            candidate.rename(current)
        except OSError:
            previous.rename(current)
            raise
        # Retain the immediately previous runtime. An in-use older directory can stay until a later update.
        for other in root.glob(".wintodo-previous-*"):
            if other != previous and re.fullmatch(r"\.wintodo-previous-[0-9a-f]{32}", other.name):
                shutil.rmtree(owned(root, other), ignore_errors=True)
    print("CodeGraph", before, "->", installed_version(current))
    return 0


if __name__ == "__main__":
    sys.exit(main())
