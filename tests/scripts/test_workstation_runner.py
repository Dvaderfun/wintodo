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
        (runner.parent / 'Show-MaintenanceNotice.ps1').write_text('exit ([int]$env:FIXTURE_NOTICE)')
        self.reports = self.base / "reports"
        self.latest = self.reports / "latest.json"
        self.cmd = [PWSH, "-NoProfile", "-NonInteractive", "-File", str(runner), "-RepoPath", str(self.base),
                    "-ReportDirectory", str(self.reports)]
        self.env = {**os.environ, "USERPROFILE": tmp.name, "LOCALAPPDATA": tmp.name, "FIXTURE_STATE": "review",
                    "FIXTURE_NOTICE": "0", "FIXTURE_WAIT": ""}

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
        self.env["FIXTURE_STATE"] = "current"
        self.assertEqual(self.invoke("-Notify", expect=0)["notification"]["state"], "not needed")
        # A storage failure must not replace the previous completed report with a partial one.
        broken = self.base / "broken"
        broken.mkdir()
        for filename in ("latest.md", "latest.json"):
            (broken / filename).write_bytes((self.reports / filename).read_bytes())
        (broken / "history").write_text("cannot create history here")
        self.cmd[-1] = str(broken)
        result = subprocess.run(self.cmd, capture_output=True, env=self.env, timeout=20)
        self.assertNotEqual(result.returncode, 0)
        for filename in ("latest.md", "latest.json"):
            self.assertEqual((broken / filename).read_bytes(), (self.reports / filename).read_bytes())

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
