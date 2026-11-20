"""Exercise the real adapter with a private temporary Git catalog and a fake CLI."""

import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest

ADAPTER = Path(__file__).resolve().parents[2] / "scripts/workstation/Update-CodexPlugins.ps1"
OFFICIAL = "https://github.com/haydenschmitty/floorplan.git"
FAKE = '''
import json,os,sys
from pathlib import Path
p=Path(os.environ["MOCK_CODEX"]); args=sys.argv[1:-1]; s=json.loads((p/"state.json").read_text())
with (p/"calls").open("a") as f: f.write(" ".join(args)+"\\n")
root=str(p/"market"); src={"sourceType":"git","source":s["origin"]}
if args==["plugin","marketplace","list"]: out={"marketplaces":[{"name":"floorplan","root":root,"marketplaceSource":src}]}
elif args==["plugin","list"]: out={"installed":[{"pluginId":"floorplan@floorplan","name":"floorplan","marketplaceName":"floorplan","installed":True,"enabled":s["enabled"],"version":s["installed"],"marketplaceSource":src,"source":{"source":"local","path":root}}]}
elif args==["plugin","marketplace","upgrade","floorplan"]: out={"selectedMarketplaces":["floorplan"],"upgradedRoots":[],"errors":[]}
elif args==["plugin","add","floorplan@floorplan"]:
 s["installed"]="0.12.0"; (p/"state.json").write_text(json.dumps(s))
 out={"pluginId":"floorplan@floorplan","version":s["installed"],"installedPath":root}
else: sys.exit(1)
print(json.dumps(out))
'''


@unittest.skipUnless(shutil.which("pwsh"), "PowerShell 7 is required")
class PluginMaintenanceTests(unittest.TestCase):
    def test_only_known_preinstalled_enabled_plugin_can_be_readded(self):
        with tempfile.TemporaryDirectory() as tmp:
            base = Path(tmp)
            repo = base / "market"
            subprocess.run(["git", "init", "--quiet", str(repo)], check=True)
            subprocess.run(["git", "-C", str(repo), "remote", "add", "origin", OFFICIAL], check=True)
            manifest = repo / ".codex-plugin/plugin.json"
            manifest.parent.mkdir()
            manifest.write_text('{"name":"floorplan","version":"0.12.0"}')
            logic = base / "cli.py"
            logic.write_text(FAKE)
            if os.name == "nt":
                stub = base / "codex.cmd"
                stub.write_text(f'@"{sys.executable}" "{logic}" %*\n')
            else:
                stub = base / "codex"
                stub.write_text("#!" + sys.executable + "\n" + FAKE)
                stub.chmod(0o755)
            env = {**os.environ, "MOCK_CODEX": tmp, "PATH": str(base) + os.pathsep + os.environ["PATH"]}
            for enabled, origin, apply, code, should_add, should_refresh in [
                (True, OFFICIAL, False, 1, False, False), (True, OFFICIAL, True, 0, True, True),
                (False, OFFICIAL, True, 0, False, True),
                (True, "https://example.invalid/rogue.git", True, 1, False, False),
            ]:
                with self.subTest(enabled=enabled, origin=origin, apply=apply):
                    (base / "calls").write_text("")
                    (base / "state.json").write_text(json.dumps(dict(enabled=enabled, origin=origin, installed="0.11.0")))
                    cmd = ["pwsh", "-NoLogo", "-NoProfile", "-File", str(ADAPTER), "-Json", "-CodexCommand", str(stub)]
                    run = subprocess.run(cmd + (["-Apply"] if apply else []), capture_output=True, text=True, env=env)
                    self.assertEqual(code, run.returncode, run.stdout + run.stderr)
                    self.assertEqual(code, json.loads(run.stdout)["exitCode"])
                    calls = (base / "calls").read_text()
                    self.assertEqual(should_add, "plugin add floorplan@floorplan" in calls)
                    self.assertEqual(should_refresh, "plugin marketplace upgrade floorplan" in calls)
                    self.assertEqual(enabled, json.loads((base / "state.json").read_text())["enabled"])
