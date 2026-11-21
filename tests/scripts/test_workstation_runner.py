"""Run the real orchestration on temporary reports. Replace only its component adapter boundary;
no updater, publisher, installed profile, Task Scheduler API or real toast is ever invoked.
"""
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import tempfile
import time
import unittest
from datetime import datetime, timedelta, timezone

RUNNER = Path(__file__).resolve().parents[2] / "scripts/workstation/Invoke-WorkstationMaintenance.ps1"
PWSH = shutil.which("pwsh")
FIXTURE_COMPONENT = """function Invoke-Component($name, [scriptblock]$action) {
    if ($env:FIXTURE_PLUGIN -and $name -eq 'Codex plugins') { & $action; return }
    if ($env:FIXTURE_WAIT -and !(Test-Path (Join-Path $ReportDirectory 'release'))) {
        [IO.File]::WriteAllText((Join-Path $ReportDirectory 'waiting'), '')
        $end = [DateTime]::UtcNow.AddSeconds(15)
        while (!(Test-Path (Join-Path $ReportDirectory 'release')) -and [DateTime]::UtcNow -lt $end) {
            Start-Sleep -Milliseconds 50
        }
    }
    Add-Result $name $env:FIXTURE_STATE 'Synthetic component.'
}"""


@unittest.skipUnless(PWSH, "PowerShell 7 required")
class WorkstationRun(unittest.TestCase):
    def setUp(self):
        tmp = tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        self.base = Path(tmp.name)
        runner = self.base / "snapshot" / RUNNER.name
        runner.parent.mkdir()
        source, count = re.subn(r"^function Invoke-Component\([^\n]+\) \{\n.*?^\}", lambda _: FIXTURE_COMPONENT,
                                RUNNER.read_text(encoding="utf-8"), flags=re.M | re.S)
        self.assertEqual(count, 1, "Fixture must replace the entire component adapter")
        runner.write_text(source, encoding="utf-8")
        (runner.parent / 'Show-MaintenanceNotice.ps1').write_text(
            'param([string]$Reason)\n[IO.File]::WriteAllText($env:FIXTURE_SENT,$Reason)\nexit ([int]$env:FIXTURE_NOTICE)')
        (runner.parent / 'Update-CodexPlugins.ps1').write_text(
            """param([switch]$Json, [switch]$Apply)
            '{"exitCode":0,"plugins":[{"plugin":"local-fixture","state":"updated","installed":"1.2.3"}]}'
            """)
        self.reports = self.base / "reports"
        self.latest = self.reports / "latest.json"
        self.cmd = [PWSH, "-NoProfile", "-NonInteractive", "-File", str(runner), "-RepoPath", str(self.base),
                    "-ReportDirectory", str(self.reports)]
        self.env = {**os.environ, "USERPROFILE": tmp.name, "LOCALAPPDATA": tmp.name, "FIXTURE_STATE": "review",
                    "FIXTURE_NOTICE": "0", "FIXTURE_WAIT": "", "FIXTURE_PLUGIN": "",
                    "FIXTURE_SENT": str(self.base / "notification-reason")}

    def invoke(self, *options, expect):
        result = subprocess.run([*self.cmd, *options], capture_output=True, text=True, env=self.env, timeout=20)
        self.assertEqual(result.returncode, expect, result.stderr + result.stdout)
        return json.loads(self.latest.read_text(encoding="utf-8"))

    def test_check_runs_reused_but_never_suppress_apply(self):
        first = self.invoke("-IfDue", "-RunSource", "Scheduled", expect=1)
        self.assertTrue(first["completed"])
        self.assertEqual(first["runSource"], "Scheduled")
        self.assertEqual(first, self.invoke("-IfDue", expect=1))
        archived = self.reports / first["history"]
        saved = archived.read_bytes()
        applied = self.invoke("-IfDue", "-Apply", expect=1)
        self.assertTrue(applied["applied"])
        self.assertEqual(applied["runSource"], "Unspecified")
        self.assertEqual(applied, self.invoke("-IfDue", expect=1))
        self.assertNotEqual(first["history"], applied["history"])
        self.assertEqual(archived.read_bytes(), saved)
        self.assertEqual(json.loads(archived.with_suffix(".json").read_text()), first)
        self.assertIn("live reports folder", archived.read_text(encoding="utf-8"))

    def test_if_due_rejects_incompatible_incomplete_and_stale_reports(self):
        valid = self.invoke(expect=1)
        self.assertEqual(valid["runSource"], "Manual")
        now = datetime.now(timezone.utc)
        for change in ({"repo": "other"}, {"runnerId": "previous"}, {"completed": False}, {"exitCode": "0"},
                       {"checkedAt": (now + timedelta(days=1)).isoformat()},
                       {"checkedAt": (now - timedelta(days=7)).isoformat()},
                       {"exitCode": 2, "checkedAt": (now - timedelta(hours=25)).isoformat()}):
            with self.subTest(change=change):
                self.latest.write_text(json.dumps({**valid, **change}), encoding="utf-8")
                self.assertNotEqual(self.invoke("-IfDue", expect=1)["history"], valid["history"])
        self.latest.write_text('{"checkedAt":', encoding="utf-8")
        self.assertNotEqual(self.invoke("-IfDue", expect=1)["history"], valid["history"])
        retry = {**valid, "exitCode": 2, "checkedAt": (now - timedelta(hours=23)).isoformat()}
        self.latest.write_text(json.dumps(retry), encoding="utf-8")
        self.assertEqual(self.invoke("-IfDue", expect=2), retry)

    def test_notification_failure_and_history_write_failure_are_visible(self):
        self.env["FIXTURE_NOTICE"] = "31"
        failed = self.invoke("-Notify", expect=2)
        self.assertEqual(failed["notification"]["state"], "failed")
        self.assertIn("failed", (self.reports / "latest.md").read_text(encoding="utf-8"))
        if os.name == "nt":
            self.assertIn("31", failed["notification"]["detail"])
            self.env["FIXTURE_NOTICE"] = "0"
            self.assertEqual(self.invoke("-Notify", expect=1)["notification"]["state"], "requested")
            self.assertEqual((self.base / "notification-reason").read_text(), "Review")
            (self.base / "notification-reason").unlink()
        self.env["FIXTURE_STATE"] = "current"
        self.assertEqual(self.invoke("-Notify", expect=0)["notification"]["state"], "not needed")
        self.assertFalse((self.base / "notification-reason").exists())
        # A storage failure must not replace the previous completed report with a partial one.
        broken = self.base / "broken"
        broken.mkdir()
        for filename in ("latest.md", "latest.json"):
            (broken / filename).write_bytes((self.reports / filename).read_bytes())
        (broken / "history").write_text("cannot create history here")
        self.cmd[-1] = str(broken)
        result = subprocess.run(self.cmd, capture_output=True, env=self.env, timeout=20)
        self.assertEqual(result.returncode, 2, result.stderr)
        for filename in ("latest.md", "latest.json"):
            self.assertEqual((broken / filename).read_bytes(), (self.reports / filename).read_bytes())

    def test_verified_plugin_update_triggers_notice_even_if_other_tools_are_current(self):
        # The real Codex wrapper reads the synthetic plugin adapter: aggregate 'checked' cannot hide an update.
        self.env.update(FIXTURE_STATE="current", FIXTURE_PLUGIN="1")
        changed = self.invoke("-Apply", "-Notify", expect=0 if os.name == "nt" else 2)
        self.assertEqual(changed["notification"]["reason"], "Changes")
        self.assertIn("local-fixture", [r["name"] for r in changed["components"] if r["state"] == "updated"])
        if os.name == "nt":
            self.assertEqual(changed["notification"]["state"], "requested")
            self.assertEqual((self.base / "notification-reason").read_text(), "Changes")
            self.env["FIXTURE_STATE"] = "review"
            mixed = self.invoke("-Apply", "-Notify", expect=1)
            self.assertEqual(mixed["notification"]["reason"], "ChangesAndReview")
            self.assertEqual((self.base / "notification-reason").read_text(), "ChangesAndReview")

    @unittest.skipUnless(os.name == "nt", "Exercises real Windows share modes")
    def test_held_alias_is_preserved_other_alias_updates_and_due_uses_short_retry(self):
        for name, other in (("latest.md", "latest.json"), ("latest.json", "latest.md")):
            with self.subTest(name=name):
                previous = self.invoke(expect=1)
                target, independent = self.reports / name, self.reports / other
                held_bytes, other_bytes = target.read_bytes(), independent.read_bytes()
                ready, release = self.base / (name + ".ready"), self.base / (name + ".release")
                hold = """$f=[IO.File]::Open($env:HELD,'Open','Read','Read')
                    try { [IO.File]::WriteAllText($env:READY,''); $end=[DateTime]::UtcNow.AddSeconds(15)
                        while (![IO.File]::Exists($env:RELEASE) -and [DateTime]::UtcNow -lt $end) {
                            Start-Sleep -Milliseconds 40
                        }
                    } finally { $f.Dispose() }"""
                with subprocess.Popen([PWSH, "-NoProfile", "-NonInteractive", "-Command", hold],
                                      env={**self.env, "HELD": str(target), "READY": str(ready), "RELEASE": str(release)},
                                      stdout=subprocess.DEVNULL) as viewer:
                    try:
                        until = time.monotonic() + 8
                        while not ready.is_file() and time.monotonic() < until:
                            time.sleep(0.04)
                        self.assertTrue(ready.is_file())
                        result = subprocess.run(self.cmd, capture_output=True, text=True, env=self.env, timeout=10)
                        self.assertEqual(result.returncode, 2, result.stderr)
                        self.assertEqual(target.read_bytes(), held_bytes)
                        self.assertNotEqual(independent.read_bytes(), other_bytes)
                    finally:
                        release.touch()
                        viewer.wait(timeout=10)
                newest = sorted((self.reports / "history").glob("*/summary.json"))[-1]
                dated = json.loads(newest.read_text())
                self.assertNotEqual(dated["history"], previous["history"])
                self.assertIn(str(newest.with_suffix(".md")), result.stdout)
                publication = json.loads((newest.parent / "publication.json").read_text())
                self.assertEqual(publication["exitCode"], 2)
                self.assertEqual([f["name"] for f in publication["unavailable"]], [name])
                self.invoke("-IfDue", expect=2)
                self.assertEqual(sorted((self.reports / "history").glob("*/summary.json"))[-1], newest)
                dated["checkedAt"] = (datetime.now(timezone.utc) - timedelta(hours=25)).isoformat()
                newest.write_text(json.dumps(dated))
                recovered = self.invoke("-IfDue", expect=1)
                self.assertNotEqual(recovered["history"], dated["history"])
                self.assertIn(recovered["history"], (self.reports / "latest.md").read_text(encoding="utf-8"))

    @unittest.skipUnless(os.name == "nt", "Exercises Windows DACL preservation")
    def test_replacing_existing_alias_preserves_its_distinct_dacl(self):
        previous = self.invoke(expect=1)
        env = {**self.env, "ALIAS": str(self.latest)}
        grant = """$a=Get-Acl -LiteralPath $env:ALIAS
            $a.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
                [Security.Principal.WindowsIdentity]::GetCurrent().User,'ReadAttributes','Allow'))
            Set-Acl -LiteralPath $env:ALIAS -AclObject $a"""
        read = "(Get-Acl -LiteralPath $env:ALIAS).GetSecurityDescriptorSddlForm('Access')"
        def dacl():
            return subprocess.check_output([PWSH, "-NoProfile", "-Command", read], env=env, text=True)
        inherited = dacl()
        subprocess.run([PWSH, "-NoProfile", "-Command", grant], env=env, check=True)
        explicit = dacl()
        self.assertNotEqual(explicit, inherited, "Fixture must add a distinct rule to the temporary alias")
        self.assertNotEqual(self.invoke(expect=1)["history"], previous["history"])
        self.assertEqual(dacl(), explicit)

    def test_overlapping_manual_and_scheduled_runs_do_not_touch_each_others_reports(self):
        previous = self.invoke(expect=1)
        with subprocess.Popen([*self.cmd, "-RunSource", "Scheduled"], env={**self.env, "FIXTURE_WAIT": "1"},
                              stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL) as owner:
            try:
                until = time.monotonic() + 10
                while not (self.reports / "waiting").exists() and time.monotonic() < until:
                    time.sleep(0.05)
                self.assertTrue((self.reports / "waiting").exists(), "The scheduled fixture never acquired its lock")
                self.assertEqual(self.invoke("-Apply", expect=2), previous)
            finally:
                (self.reports / "release").touch()
                owner.wait(timeout=15)
            self.assertEqual(owner.returncode, 1)
        self.assertFalse(json.loads(self.latest.read_text())["applied"])


if __name__ == "__main__":
    unittest.main()
