"""Report stable dependency drift. No packages, credentials or private reports are read from the host."""

import argparse
from concurrent.futures import ThreadPoolExecutor
from functools import cache
from http.client import HTTPException
import json
from pathlib import Path
import re
from urllib.request import Request, urlopen
from xml.etree import ElementTree

ROOT = Path(__file__).resolve().parents[1]
STAMP = r"[0-9]+\.[0-9]+\.[0-9]+(?:\.[0-9]+)?"
SHA = r"[0-9a-f]{40}"
PACKAGE_ID = r"[A-Za-z0-9][A-Za-z0-9._-]*"
LIMIT = 8 * 1024 * 1024
SKILLS = "microsoft/WinAppCli"
ACTIONS = ("actions/checkout", "actions/setup-dotnet", "actions/setup-python", "actions/upload-artifact")


def require(condition):
    if not condition:
        raise ValueError("inconsistent evidence")


def exact(pattern, value):
    require(isinstance(value, str) and re.fullmatch(pattern, value))
    return value


def version(value):
    return tuple((list(map(int, exact(STAMP, value).split("."))) + [0])[:4])


@cache
def fetch(url):
    # All origins are constants; package IDs and tag/ref segments are validated.
    with urlopen(Request(url, headers={"User-Agent": "wintodo-project-health"}), timeout=15) as response:
        body = response.read(LIMIT + 1)
    require(len(body) <= LIMIT)
    return body.decode("utf-8-sig")


def data(url):
    return json.loads(fetch(url))


def local(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def release(repo):
    # GitHub's latest published stable release, without consuming the anonymous
    # REST API quota shared by local processes or the hosted runner's address.
    prefix = f"https://github.com/{repo}/releases/"
    request = Request(prefix + "latest", method="HEAD", headers={"User-Agent": "wintodo-project-health"})
    with urlopen(request, timeout=15) as response:
        match = re.fullmatch(re.escape(prefix) + r"tag/(v" + STAMP + ")", response.geturl(), flags=re.I)
    require(match is not None)
    return match[1]


def tag_commit(repo, tag):
    # The read-only Git smart HTTP advertisement includes peeled annotated tags.
    # Match the complete ref name; v1.2.3 must not match v1.2.30 or a branch.
    refs = fetch(f"https://github.com/{repo}.git/info/refs?service=git-upload-pack")
    require(refs.startswith("001e# service=git-upload-pack\n"))
    hits = re.findall("(" + SHA + ") refs/tags/" + re.escape(tag) + r"(\^\{\})?\n", refs)
    direct = [sha for sha, suffix in hits if not suffix]
    peeled = [sha for sha, suffix in hits if suffix]
    require(len(direct) == 1 and len(peeled) <= 1)
    return (peeled or direct)[0]


def check_sdk(root):
    pin = local(root / "global.json")["sdk"]
    require(pin["rollForward"] == "disable" and pin["allowPrerelease"] is False)
    current = exact(STAMP, pin["version"])
    channel = ".".join(current.split(".")[:2])
    source = data(f"https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/{channel}/releases.json")
    require(source["channel-version"] == channel and source["release-type"].lower() == "lts")
    latest = exact(re.escape(channel) + r"\.[0-9]+", source["latest-sdk"])
    return current, latest, current == latest and source["support-phase"] in ("active", "maintenance")


def check_skills(root):
    manifest = (root / ".agents/skills/VENDORED.md").read_text(encoding="utf-8-sig")
    tag = re.search(r"(?m)^- Tag: `(v" + STAMP + r")`\s*$", manifest)[1]
    sha = exact(SHA, re.search(r"(?m)^- Commit: `([a-f0-9]+)`\s*$", manifest)[1])
    origin = re.search(r"(?m)^- Source: https://github\.com/([^ \s(]+)", manifest)[1].rstrip("/")
    latest = release(SKILLS)
    wanted = tag_commit(SKILLS, latest)
    return f"{tag} {sha[:12]}", f"{latest} {wanted[:12]}", (origin.lower() == SKILLS.lower()
                                                            and tag == latest and sha == wanted)


def check_sqlite(root):
    versions = set()
    for arch in ("win-x64", "win-arm64"):
        for shape in ("", "aot."):
            graph = local(root / f"src/Wintodo.App/packages.{shape}{arch}.lock.json")["dependencies"]
            require(any(tfm.endswith("/" + arch) for tfm in graph))
            for packages in graph.values():
                versions.add(exact(STAMP, packages["SQLite"]["resolved"]))
    require(len(versions) == 1)
    current, = versions
    rows = re.findall(r"(?m)^PRODUCT,(" + STAMP + r"),\d{4}/sqlite-src-\d+\.zip,\d+,[a-f0-9]{64}\s*$",
                      fetch("https://www.sqlite.org/download.html"))
    require(len(rows) == 1)
    return current, rows[0], version(current) == version(rows[0])


def check_scanner(root):
    pin_file = root / "scripts/gitleaks-pin.json"
    if pin_file.exists():
        pin = local(pin_file)
    else:
        # The already-shipped baseline keeps its exact URL and hash in PowerShell.
        # A workstation can monitor that checkout before these changes are merged.
        # Read literal data only; modern scripts without the JSON fail closed.
        legacy = (root / "scripts/Invoke-CiSecretScan.ps1").read_text(encoding="utf-8-sig")
        urls = re.findall(r"(?m)^\s*Invoke-WebRequest -Uri 'https://github\.com/gitleaks/gitleaks/releases/download/v("
                          + STAMP + r")/gitleaks_\1_windows_x64\.zip' -OutFile \$archive\s*$", legacy)
        hashes = re.findall(r"(?m)^\$expectedHash = '([A-Fa-f0-9]{64})'\s*$", legacy)
        require(len(urls) == len(hashes) == 1)
        pin = {"version": urls[0], "sha256": hashes[0].lower()}
    tag = release("gitleaks/gitleaks")
    current = exact(STAMP, pin["version"])
    sha = exact(r"[a-f0-9]{64}", pin["sha256"])
    asset = f"gitleaks_{tag[1:]}_windows_x64.zip"
    checksums = fetch(f"https://github.com/gitleaks/gitleaks/releases/download/{tag}/gitleaks_{tag[1:]}_checksums.txt")
    digests = re.findall(r"(?m)^([a-f0-9]{64}) +\*?" + re.escape(asset) + r"\r?$", checksums)
    require(len(digests) == 1)
    return f"{current} {sha[:12]}", f"{tag[1:]} {digests[0][:12]}", current == tag[1:] and digests[0] == sha


def check_action(root, repo):
    # Full SHA comparisons also detect an upstream release tag moving without a version change.
    files = (root / ".github/workflows").glob("*.y*ml")
    pins = {pin for path in files for pin in re.findall(
        r"(?m)^\s*-\s+uses:\s*" + re.escape(repo) + r"@([^\s#]+)", path.read_text("utf-8-sig"))}
    require(len(pins) == 1)
    current = exact(SHA, pins.pop())
    tag = release(repo)
    wanted = tag_commit(repo, tag)
    return current[:12], f"{tag} {wanted[:12]}", current == wanted


def check_nuget(name, current, sdk_major=None):
    exact(PACKAGE_ID, name)
    version(current)
    # The flat-container version index includes unlisted/withdrawn releases.
    # Exact-ID NuGet.org search with prerelease=false supplies the listed stable release.
    source = data(f"https://azuresearch-usnc.nuget.org/query?q=packageid:{name}&prerelease=false&semVerLevel=2.0.0&take=2")
    require(source["totalHits"] == 1 and len(source["data"]) == 1)
    package = source["data"][0]
    require(package["id"].lower() == name.lower())
    latest = exact(STAMP, package["version"])
    if name.lower() == "microsoft.data.sqlite" and sdk_major is not None:
        # Stay on the selected .NET LTS provider line. Show a newer major without
        # permanently failing the weekly job or hiding servicing updates on LTS.
        listed = [item["version"] for item in package["versions"] if re.fullmatch(STAMP, item["version"])]
        compatible = max((s for s in listed if version(s)[0] == sdk_major), key=version)
        label = compatible if latest == compatible else f"{compatible} on LTS; {latest} overall"
        return current, label, version(current) == version(compatible)
    return current, latest, version(current) == version(latest)


def result(name, action, function, *args):
    try:
        current, latest, same = function(*args)
        return dict(component=name, status="current" if same else "review",
                    current=current, latest=latest, action=action)
    except (OSError, HTTPException, ValueError, TypeError, KeyError, IndexError, AttributeError):
        # Avoid machine paths or untrusted upstream body text in reports / Actions summaries.
        return dict(component=name, status="incomplete", current="?", latest="?",
                    action="Retry; if persistent, inspect the checked-in pin and official feed. " + action)


def checks(root):
    pending = [
        (".NET SDK (pinned LTS)", "Review global.json, locks and runtime notices together (ADR-020).", check_sdk, root),
        ("SQLite native", "Review SQLite changes/advisories, package and both target binaries.", check_sqlite, root),
        ("Vendored WinUI skills", "Review this stable WinAppCli tag/commit and refresh both skill mirrors.", check_skills, root),
        ("Gitleaks (Windows x64 ZIP)", "Verify the publisher release and archive SHA-256 before repinning.", check_scanner, root),
    ]
    pending += [(repo, "Review the Dependabot Actions PR and immutable commit.", check_action, root, repo)
                for repo in ACTIONS]
    try:
        props = ElementTree.parse(root / "Directory.Packages.props")
        entries = props.findall(".//PackageVersion")
        require(len(entries) > 0)
        seen = set()
        sdk_major = version(local(root / "global.json")["sdk"]["version"])[0]
        for entry in entries:
            name = exact(PACKAGE_ID, entry.attrib["Include"])
            require(name.lower() not in seen)
            seen.add(name.lower())
            pending.append((name, "Review the NuGet PR, framework compatibility and all affected locks.",
                            check_nuget, name, entry.attrib["Version"], sdk_major))
    except (ElementTree.ParseError, OSError, KeyError, ValueError):
        pending.append(("NuGet manifest", "Check central package declarations.", require, False))
    with ThreadPoolExecutor(max_workers=5) as pool:
        return list(pool.map(lambda args: result(*args), pending))


def exit_code(rows):
    return 2 if any(r["status"] == "incomplete" for r in rows) else int(any(r["status"] == "review" for r in rows))


def markdown(rows):
    lines = ["## Wintodo project health", "", "| Component | State | Pinned | Latest stable |",
             "|---|---|---|---|"]
    for row in rows:
        lines.append("| {component} | {status} | {current} | {latest} |".format(**row))
    actions = sorted({"- " + r["action"] for r in rows if r["status"] != "current"})
    lines.extend(["", "Review actions:", "", *actions] if actions else ["", "All reported pins match."])
    lines += ["", "NuGet major upgrades need compatibility review; a newer release is not an advisory.",
              "The SQLite comparison uses resolved package versions, not binary attestation."]
    return "\n".join(lines) + "\n"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo", type=Path, default=ROOT, help="read version pins from this checkout; never execute its code")
    parser.add_argument("--json", type=Path, help="write a version-only JSON report")
    parser.add_argument("--markdown", type=Path, help="write the human-readable table")
    args = parser.parse_args()
    rows = checks(args.repo.resolve())
    report = markdown(rows)
    print(report, end="")
    if args.markdown:
        args.markdown.write_text(report, encoding="utf-8")
    if args.json:
        args.json.write_text(json.dumps({"schema": 1, "checks": rows}, indent=2) + "\n", encoding="utf-8")
    return exit_code(rows)


if __name__ == "__main__":
    raise SystemExit(main())
