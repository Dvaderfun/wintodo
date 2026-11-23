"""The workstation must not change an npm installation owned by another manager."""
from pathlib import Path
import os
import shutil
import subprocess
import tempfile
import unittest


@unittest.skipUnless(os.name == "nt" and shutil.which("pwsh"), "Windows workstation adapter")
class NodeOwnership(unittest.TestCase):
    def test_external_prefix_and_unsupported_line(self):
        adapter = Path(__file__).resolve().parents[2] / "scripts/workstation/Update-AgentNode.ps1"
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "node.cmd").write_text("@echo off\necho v26.13.0\n", encoding="ascii")
            (root / "npm.cmd").write_text(
                '@echo off\nif "%1"=="--version" (echo 12.2.3) else (echo mutation>>"%TEST_ROOT%\\unexpected")\n',
                encoding="ascii")
            probe = root / "check.ps1"
            probe.write_text("""
$ErrorActionPreference='Stop'
$env:PATH=$env:TEST_ROOT+';'+$env:PATH
function Invoke-RestMethod($Uri,$TimeoutSec) {
  if ($Uri -like '*/index.json') { return @([pscustomobject]@{version=$env:FIXTURE_NODE_RELEASE;lts='Lithium'}) }
  if ($Uri -like '*/schedule.json') { return @{'v26'=@{end=$env:FIXTURE_NODE_EOL}} }
  if ($Uri -like '*/npm/latest') { return @{version='12.2.3'} }
  throw 'Unexpected network request'
}
$env:FIXTURE_NODE_RELEASE='v26.13.0'; $env:FIXTURE_NODE_EOL='2099-04-30'
$row = & $env:TEST_ADAPTER -Apply
if ($row.state -ne 'current') { throw "Wrong no-op status: $($row.detail)" }
$env:FIXTURE_NODE_RELEASE='v26.13.1'
if ((& $env:TEST_ADAPTER -Apply).detail -notmatch 'does not own') { throw 'Missed external prefix' }
$env:FIXTURE_NODE_EOL='2001-04-30'
if ((& $env:TEST_ADAPTER -Apply).detail -notmatch 'supported LTS') { throw 'Missed unsupported LTS' }
if (Test-Path -LiteralPath (Join-Path $env:TEST_ROOT 'unexpected')) { throw 'Called a mutating command' }
""", encoding="utf-8")
            subprocess.run(["pwsh", "-NoProfile", "-NonInteractive", "-File", str(probe)],
                           env={**os.environ, "TEST_ROOT": str(root), "TEST_ADAPTER": str(adapter)},
                           check=True, timeout=45)
