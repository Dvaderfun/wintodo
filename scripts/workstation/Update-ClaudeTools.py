"""Service the selected native Claude Code CLI and explicitly reviewed plugin sources."""
import argparse
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import urllib.request


TAGS = "https://registry.npmjs.org/-/package/@anthropic-ai/claude-code/dist-tags"
CATALOGS = {"claude-plugins-official": "anthropics/claude-plugins-official",
            "karpathy-skills": "forrestchang/andrej-karpathy-skills"}
SOURCES = {"andrej-karpathy-skills@karpathy-skills": "./",
           "claude-md-management@claude-plugins-official": "./plugins/claude-md-management",
           "figma@claude-plugins-official": "https://github.com/figma/mcp-server-guide.git",
           "security-guidance@claude-plugins-official": "./plugins/security-guidance"}


class Review(Exception):
    """A known condition that needs a person's decision. Only constant text is exposed."""


class StateChanged(RuntimeError):
    pass


class PublisherFailed(RuntimeError):
    pass


class Incomplete(RuntimeError):
    """Only reviewed stage identifiers and fixed reason codes reach the report."""


def step(stage, action, *args):
    try:
        return action(*args)
    except (Review, Incomplete):
        raise
    except (OSError, ValueError, KeyError, IndexError, TypeError, RuntimeError, subprocess.SubprocessError) as exc:
        if isinstance(exc, subprocess.TimeoutExpired):
            reason = "timeout"
        elif isinstance(exc, StateChanged):
            reason = "state-changed"
        elif isinstance(exc, PublisherFailed):
            reason = "publisher-error"
        elif isinstance(exc, OSError):
            reason = "io-error"
        elif isinstance(exc, json.JSONDecodeError):
            reason = "invalid-json"
        else:
            reason = "invalid-response"
        raise Incomplete(f"{stage}: {reason}.") from None


def run(argv, cwd=None, *, read_only=False):
    # Retry a read at most once on a timeout. A timed-out write can have already
    # changed the cache: give it a longer first attempt and never replay it.
    for attempt in range(2 if read_only else 1):
        try:
            return subprocess.run(argv, stdin=subprocess.DEVNULL, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                                  text=True, encoding="utf-8", errors="replace", timeout=90 if read_only else 300,
                                  cwd=cwd, env={**os.environ, "GIT_TERMINAL_PROMPT": "0"})
        except subprocess.TimeoutExpired:
            if not read_only or attempt:
                raise


def native(binary, args, repo):
    read_only = args in (("--version",), ("--bare", "plugin", "list", "--json"),
                         ("--bare", "plugin", "marketplace", "list", "--json"))
    return run([binary, *args], repo, read_only=read_only)


def read_json(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def version(value):
    if not isinstance(value, str) or not re.fullmatch(r"\d+\.\d+\.\d+", value):
        raise ValueError("Unexpected CLI version")
    return tuple(map(int, value.split(".")))


def service_cli(cli, profile, apply):
    channel = read_json(profile / "settings.json").get("autoUpdatesChannel", "latest")
    if channel not in ("stable", "latest"):
        raise Review("Unrecognized update channel.")
    def installed():
        result = cli("--version")
        match = re.fullmatch(r"(\d+\.\d+\.\d+) \(Claude Code\)", result.stdout.strip())
        if result.returncode or not match:
            raise ValueError("Unrecognized version response")
        return match[1]
    before = installed()
    with urllib.request.urlopen(TAGS, timeout=25) as feed:
        if feed.geturl() != TAGS:
            raise ValueError("Unexpected package feed redirect")
        latest = json.loads(feed.read(16384))[channel]
    if version(before) >= version(latest):
        return "current", f"{before} ({channel}: {latest})"
    if not apply:
        return "review", f"{before} -> {latest} ({channel})"
    if cli("--bare", "--settings", json.dumps({"autoUpdatesChannel": channel}), "update").returncode:
        raise RuntimeError("Native updater failed")
    after = installed()
    if version(after) < version(latest):
        raise RuntimeError("Native update not verified")
    return "updated", f"{before} -> {after} ({channel})"


def listing(cli, *args):
    result = cli("--bare", "plugin", *args, "list", "--json")
    if result.returncode:
        raise PublisherFailed()
    values = json.loads(result.stdout)
    if not isinstance(values, list) or not all(isinstance(x, dict) for x in values):
        raise ValueError("Invalid CLI inventory")
    return values


def inventory(cli):
    records = {}
    for item in listing(cli):
        key = (item["id"], item["scope"], item.get("projectPath"))
        if key in records or not isinstance(item["enabled"], bool):
            raise ValueError("Ambiguous plugin inventory")
        records[key] = (item["enabled"], item.get("projectEnabled"), item["version"])
    return records


def preserve(before, after, selected=None):
    if (before.keys() != after.keys()
            or any(v[:2] != after[k][:2] or k != selected and v[2] != after[k][2] for k, v in before.items())):
        raise StateChanged("Unexpected plugin state change")


def trusted_market(profile, cli, name):
    matches = [m for m in listing(cli, "marketplace") if m.get("name") == name]
    folder = profile / "plugins/marketplaces" / name
    repo = CATALOGS[name]
    if (len(matches) != 1 or (matches[0].get("source"), matches[0].get("repo")) != ("github", repo)
            or Path(matches[0]["installLocation"]) != folder or folder.resolve(strict=True) != folder):
        raise Review("Marketplace identity or installation path requires review.")
    if (folder / ".git").exists():
        origin = run(["git", "remote", "get-url", "origin"], folder, read_only=True)
        clean = run(["git", "-c", "core.fsmonitor=false", "status", "--porcelain"], folder, read_only=True)
        if origin.returncode or origin.stdout.strip() != f"https://github.com/{repo}.git" or clean.returncode or clean.stdout.strip():
            raise Review("Marketplace origin or local changes require review.")
    elif name != "claude-plugins-official":
        raise Review("The expected Git checkout is missing.")
    return folder


def approved_source(folder, plugin):
    doc = read_json(folder / ".claude-plugin/marketplace.json")
    name, market = plugin.split("@")
    entries = [p for p in doc["plugins"] if p.get("name") == name]
    if doc["name"] != market or len(entries) != 1:
        raise Review("Catalog no longer contains the reviewed plugin identity.")
    src = entries[0].get("source")
    expected = SOURCES[plugin]
    if expected.startswith("./") and src == expected:
        local = folder / src
        if local.is_dir() and local.resolve() == local:
            return
    elif (isinstance(src, dict) and set(src) == {"source", "url", "sha"} and src["source"] == "url"
          and src["url"] == expected and re.fullmatch(r"[0-9a-f]{40}", src["sha"])):
        return
    raise Review("Changed or command-based plugin source; review it interactively.")


def update(cli, *args):
    # Neither -y nor --accept-command is used, including on a retry. Never log command text.
    res = cli("--bare", "plugin", *args)
    rows = res.stdout.strip().splitlines()
    if not rows and res.returncode:
        raise PublisherFailed()
    obj = json.loads(rows[-1]) if rows else None
    if not isinstance(obj, dict):
        raise ValueError("Invalid update result")
    if obj.get("shownCommand"):
        raise Review("Publisher command requires a person's review; no command was accepted.")
    if res.returncode or obj.get("success") is False or obj.get("error"):
        raise PublisherFailed()
    return obj


def service_plugins(cli, profile, apply, add):
    before = step("inventory/initial", inventory, cli)
    add("Claude plugin inventory", "checked", f"{len(set(k[0] for k in before))} IDs; "
        f"{len(before)} scoped installations; {sum(not v[0] for v in before.values())} disabled. "
        "Disabled and non-user installations are inventory only.")
    selected = [(k, v) for k, v in before.items() if k[1:] == ("user", None) and v[0]]
    if any(k[0] not in SOURCES for k, _ in selected):
        add("Additional enabled Claude plugins", "review", "Unrecognized installed identities need a source review.")
    previous = before
    for market in sorted({k[0].split("@")[1] for k, _ in selected if k[0] in SOURCES}):
        try:
            folder = step(f"{market}/identity", trusted_market, profile, cli, market)
            if apply:
                try:
                    step(f"{market}/refresh", update, cli, "marketplace", "update", "--json", market)
                finally:
                    step(f"{market}/verify-state", lambda: preserve(previous, inventory(cli)))
                folder = step(f"{market}/refreshed-identity", trusted_market, profile, cli, market)
        except Review as exc:
            add(market, "review", str(exc))
            continue
        for key, initial in selected:
            plugin = key[0]
            if plugin not in SOURCES or plugin.split("@")[1] != market:
                continue
            try:
                step(f"{plugin}/source", approved_source, folder, plugin)
                if not apply:
                    add(plugin, "cached", "Installed " + display(initial[2]) + "; remote freshness not checked.")
                    continue
                try:
                    step(f"{plugin}/refresh", update, cli, "update", "--scope", "user", "--json", plugin)
                finally:
                    after = step(f"{plugin}/readback", inventory, cli)
                    step(f"{plugin}/verify-state", preserve, previous, after, key)
                    previous = after
                old, new = display(initial[2]), display(after[key][2])
                add(plugin, "checked" if old == new else "updated", old if old == new else f"{old} -> {new}")
            except Review as exc:
                add(plugin, "review", str(exc))
    step("inventory/final", lambda: preserve(previous, inventory(cli)))


def display(value):
    return value if isinstance(value, str) and re.fullmatch(r"[0-9A-Za-z][0-9A-Za-z.+_-]{0,64}", value) else "(unversioned)"


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--apply", action="store_true")
    parser.add_argument("--repo", type=Path, required=True)
    args = parser.parse_args()
    rows = []
    def add(name, state, detail):
        rows.append(dict(name=name, state=state, detail=detail))
    try:
        home = Path.home()
        profile = home / ".claude"
        binary = home / ".local/bin/claude.exe"
        selected = shutil.which("claude")
        if (not selected or Path(selected) != binary or not binary.is_file()
                or Path(os.environ.get("CLAUDE_CONFIG_DIR") or profile) != profile):
            raise Review("The selected CLI or profile does not match the reviewed native installation.")
        def cli(*argv):
            return native(binary, argv, args.repo)
        try:
            add("Claude Code CLI", *service_cli(cli, profile, args.apply))
        except Review as exc:
            add("Claude Code CLI", "review", str(exc))
        except (OSError, ValueError, KeyError, RuntimeError, subprocess.SubprocessError):
            add("Claude Code CLI", "incomplete", "Could not verify the native installation or channel feed.")
        service_plugins(cli, profile, args.apply, add)
    except Review as exc:
        add("Claude Code", "review", str(exc))
    except Incomplete as exc:
        add("Claude plugins", "incomplete", str(exc))
    except (OSError, ValueError, KeyError, IndexError, TypeError, RuntimeError, subprocess.SubprocessError):
        add("Claude plugins", "incomplete", "Could not verify the publisher operation or unchanged install/enable states.")
    states = {r["state"] for r in rows}
    code = 2 if "incomplete" in states else 1 if "review" in states else 0
    print(json.dumps({"exitCode": code, "components": rows}, indent=2))
    return code


if __name__ == "__main__":
    raise SystemExit(main())
