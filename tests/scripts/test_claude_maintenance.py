"""Verify selection, source and state preservation without invoking a real agent or network."""
from copy import deepcopy
import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest import mock


SCRIPT = Path(__file__).resolve().parents[2] / "scripts/workstation/Update-ClaudeTools.py"
spec = importlib.util.spec_from_file_location("claude_tools", SCRIPT)
adapter = importlib.util.module_from_spec(spec)
spec.loader.exec_module(adapter)

MARKET = "claude-plugins-official"
PLUGIN = "security-guidance@" + MARKET


class MaintenanceTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.profile = Path(self.tmp.name).resolve()
        self.folder = self.profile / "plugins/marketplaces" / MARKET
        self.manifest = self.folder / ".claude-plugin/marketplace.json"
        self.manifest.parent.mkdir(parents=True)
        (self.folder / "plugins/security-guidance").mkdir(parents=True)
        self.set_source("./plugins/security-guidance")
        self.plugins = [
            dict(id=PLUGIN, scope="user", enabled=True, projectEnabled=False, version="1.0.0"),
            dict(id="disabled@elsewhere", scope="user", enabled=False, version="0.1.0"),
            dict(id=PLUGIN, scope="local", enabled=False, version="0.5.0", projectPath="private-project"),
        ]
        self.markets = [dict(name=MARKET, source="github", repo=adapter.CATALOGS[MARKET],
                             installLocation=str(self.folder))]
        self.calls = []
        self.mode = ""
        self.rows = []

    def set_source(self, source):
        self.manifest.write_text(json.dumps(dict(name=MARKET, plugins=[dict(name="security-guidance",
                                                                            source=source)])), encoding="utf-8")

    def cli(self, *args):
        self.assertEqual(args[0:2], ("--bare", "plugin"))
        self.calls.append(args[2:])
        sub = args[2:]
        code = 0
        if sub == ("list", "--json"):
            obj = self.plugins
        elif sub == ("marketplace", "list", "--json"):
            obj = self.markets
        elif sub == ("marketplace", "update", "--json", MARKET):
            obj = {"success": True}
        elif sub == ("update", "--scope", "user", "--json", PLUGIN):
            if self.mode == "prompt":
                code, obj = 1, {"success": False, "shownCommand": {"command": "private-value"}}
            else:
                obj = {"success": True}
                self.plugins[0]["version"] = "1.1.0"
                if self.mode == "state":
                    self.plugins[1]["enabled"] = True
                if self.mode == "other-version":
                    self.plugins[2]["version"] = "1.1.0"
        else:
            self.fail(f"Unexpected operation {sub!r}")
        return subprocess.CompletedProcess([], code, json.dumps(obj), "")

    def service(self, apply=True):
        adapter.service_plugins(self.cli, self.profile, apply, lambda *row: self.rows.append(row))

    def test_updates_only_selected_enabled_user_install(self):
        unaffected = deepcopy(self.plugins[1:])
        self.service()
        self.assertEqual(unaffected, self.plugins[1:])
        self.assertIn((PLUGIN, "updated", "1.0.0 -> 1.1.0"), self.rows)
        self.assertFalse(any("private-project" in str(x) for x in self.rows))

    def test_check_only_reports_cached_and_never_refreshes(self):
        self.service(False)
        self.assertTrue(any(row[1] == "cached" for row in self.rows))
        self.assertFalse(any("update" in a for a in self.calls))

    def test_unapproved_market_never_refreshed(self):
        self.markets[0]["repo"] = "other/owner"
        self.service()
        self.assertTrue(any(row[1] == "review" for row in self.rows))
        self.assertFalse(any("update" in a for a in self.calls))

    def test_command_sources_not_updated_and_new_commands_never_accepted(self):
        self.set_source(dict(source="command", command="private-value"))
        self.service()
        self.assertNotIn(("update", "--scope", "user", "--json", PLUGIN), self.calls)
        self.set_source("./plugins/security-guidance")
        self.mode = "prompt"
        self.service()
        self.assertTrue(any("no command was accepted" in row[2] for row in self.rows))
        self.assertFalse(any("private-value" in str(x) for x in self.rows))

    def test_unexpected_enablement_or_nonselected_version_stops_processing(self):
        for mode in ("state", "other-version"):
            with self.subTest(mode=mode):
                self.mode = mode
                snap = deepcopy(self.plugins)
                with self.assertRaisesRegex(adapter.Incomplete, "/verify-state: state-changed"):
                    self.service()
                self.plugins = snap

    def test_only_verified_reads_retry_once_and_stdin_is_closed(self):
        timeout = subprocess.TimeoutExpired(["private-command"], 90)
        ok = subprocess.CompletedProcess([], 0, "[]", "")
        with mock.patch.object(adapter.subprocess, "run", side_effect=[timeout, ok]) as call:
            self.assertIs(adapter.native("binary", ("--bare", "plugin", "list", "--json"), self.profile), ok)
            self.assertEqual(call.call_count, 2)
            self.assertTrue(all(x.kwargs["timeout"] == 90 and x.kwargs["stdin"] == subprocess.DEVNULL
                                and x.kwargs["cwd"] == self.profile for x in call.call_args_list))
        with mock.patch.object(adapter.subprocess, "run", side_effect=timeout) as call:
            with self.assertRaises(subprocess.TimeoutExpired):
                adapter.native("binary", ("--bare", "plugin", "marketplace", "list", "--json"), self.profile)
            self.assertEqual(call.call_count, 2)
        with mock.patch.object(adapter.subprocess, "run", side_effect=timeout) as call:
            with self.assertRaises(subprocess.TimeoutExpired):
                adapter.native("binary", ("--bare", "plugin", "update", "--scope", "user", "--json", PLUGIN),
                               self.profile)
            self.assertEqual(call.call_count, 1)
            self.assertEqual(call.call_args.kwargs["timeout"], 300)

    def test_catalog_timeout_reports_only_safe_stage_and_still_checks_state(self):
        calls = []
        def cli(*args):
            calls.append(args[2:])
            if args[2:] == ("marketplace", "update", "--json", MARKET):
                raise subprocess.TimeoutExpired(["private-command"], 300, output="private-output", stderr="private-error")
            return self.cli(*args)
        with self.assertRaises(adapter.Incomplete) as caught:
            adapter.service_plugins(cli, self.profile, True, lambda *row: None)
        self.assertEqual(str(caught.exception), MARKET + "/refresh: timeout.")
        self.assertEqual(calls[-1], ("list", "--json"))
        self.assertFalse(any(PLUGIN in call for call in calls))

    def test_rejects_nonzero_or_malformed_inventory_without_echoing_output(self):
        for result, reason in ((subprocess.CompletedProcess([], 1, "private-output", "private-error"), "publisher-error"),
                               (subprocess.CompletedProcess([], 0, "private-output", ""), "invalid-json")):
            with self.subTest(reason=reason), self.assertRaises(adapter.Incomplete) as caught:
                adapter.service_plugins(lambda *args: result, self.profile, True, lambda *row: None)
            self.assertEqual(str(caught.exception), "inventory/initial: " + reason + ".")

    def test_unknown_enabled_plugin_and_inactive_only_catalog_are_not_serviced(self):
        self.plugins[0]["enabled"] = False
        self.plugins.append(dict(id="other@unknown", scope="user", enabled=True, version="1.0.0"))
        self.service()
        self.assertFalse(any("update" in a for a in self.calls))
        self.assertTrue(any(row[1] == "review" for row in self.rows))

    def test_native_channel_comparison_respects_user_choice_and_no_downgrade(self):
        (self.profile / "settings.json").write_text('{"autoUpdatesChannel":"stable"}')
        feed = mock.MagicMock()
        feed.__enter__.return_value.geturl.return_value = adapter.TAGS
        feed.__enter__.return_value.read.return_value = b'{"stable":"2.1.2","latest":"2.2.0"}'
        cli = mock.Mock(return_value=subprocess.CompletedProcess([], 0, "2.1.3 (Claude Code)", ""))
        with mock.patch.object(adapter.urllib.request, "urlopen", return_value=feed):
            self.assertEqual(adapter.service_cli(cli, self.profile, True), ("current", "2.1.3 (stable: 2.1.2)"))
        cli.assert_called_once_with("--version")

    def test_native_update_uses_selected_channel_and_verifies_result(self):
        (self.profile / "settings.json").write_text('{"autoUpdatesChannel":"stable"}')
        feed = mock.MagicMock()
        feed.__enter__.return_value.geturl.return_value = adapter.TAGS
        feed.__enter__.return_value.read.return_value = b'{"stable":"2.1.2","latest":"2.2.0"}'
        cli = mock.Mock(side_effect=[subprocess.CompletedProcess([], 0, v, "") for v in
                                     ("2.1.1 (Claude Code)", "", "2.1.2 (Claude Code)")])
        with mock.patch.object(adapter.urllib.request, "urlopen", return_value=feed):
            self.assertEqual(adapter.service_cli(cli, self.profile, True), ("updated", "2.1.1 -> 2.1.2 (stable)"))
        self.assertEqual(cli.call_args_list, [mock.call("--version"),
                         mock.call("--bare", "--settings", '{"autoUpdatesChannel": "stable"}', "update"),
                         mock.call("--version")])
