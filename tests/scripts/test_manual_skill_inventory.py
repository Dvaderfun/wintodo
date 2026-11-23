"""Exercise the inventory comparison without invoking the workstation updater."""

from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

SCRIPT = Path(__file__).resolve().parents[2] / "scripts/workstation/Invoke-WorkstationMaintenance.ps1"
HARNESS = r'''
param($Source, $Folder)
$ErrorActionPreference = 'Stop'
$tokens=$null; $errors=$null
$ast = [Management.Automation.Language.Parser]::ParseFile($Source, [ref]$tokens, [ref]$errors)
if ($errors.Count) { throw 'Runner does not parse' }
$f = $ast.Find({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Get-ManualSkillState'}, $true)
. ([scriptblock]::Create($f.Extent.Text))
function Expect($items, $apply, $expected) {
    $observed = Get-ManualSkillState $Folder $items $apply
    if ($observed -cne $expected) { throw "Expected $expected, got $observed" }
}
Expect @() $false 'checked'
Expect @('a') $false 'review'
Expect @('a') $true 'review'
Expect @('a') $true 'manual'
Expect @('b','a') $false 'review'
Expect @('a','b') $true 'review'
Expect @('b','a') $true 'manual'
Expect @() $true 'review'
Expect @() $false 'checked'
'''


@unittest.skipUnless(shutil.which("pwsh"), "PowerShell 7 is required")
class ManualSkillTests(unittest.TestCase):
    def test_new_changed_unchanged_and_preview(self):
        with tempfile.TemporaryDirectory() as tmp:
            script = Path(tmp) / "check.ps1"
            script.write_text(HARNESS, encoding="utf-8")
            subprocess.run(["pwsh", "-NoProfile", "-NonInteractive", "-File", str(script), str(SCRIPT), tmp], check=True)
