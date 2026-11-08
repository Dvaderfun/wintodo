"""Installed smoke safety seams; these regressions never execute UI or deploy."""
import json
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

REPOSITORY = Path(__file__).parents[2]
RUNNER = REPOSITORY / "tests/ui/run-installed-package-smoke-gate.ps1"
MARKER = "SYNTHETIC-OWNER-private-value"
VM_ID = "12345678-1234-1234-1234-123456789abc"


def quote(value):
    return "'" + str(value).replace("'", "''") + "'"


class InstalledPackageSmokeTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.pwsh = shutil.which("pwsh")
        if not cls.pwsh:
            raise RuntimeError("PowerShell is required for installed smoke regressions")
        cls.scratch = REPOSITORY / "artifacts/dep-installed-smoke/tests"
        cls.scratch.mkdir(parents=True, exist_ok=True)
        cls.functions = f"""
$ErrorActionPreference = 'Stop'
$tokens = $null
$parseErrors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile({quote(RUNNER)}, [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count) {{ throw 'Runner parse failed.' }}
$names = @('Assert-InstalledSmokeGuest', 'Assert-InstalledSmokeRegistration',
    'Assert-InstalledSmokeInspection', 'Invoke-InstalledSmokeUi',
    'Assert-InstalledSmokeProcessUnchanged', 'Assert-InstalledSmokeSignature',
    'Assert-InstalledSmokeScreenshot')
$definitions = $ast.FindAll({{ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] }}, $true)
foreach ($name in $names) {{
    $definition = @($definitions | Where-Object Name -eq $name)
    if ($definition.Count -ne 1) {{ throw 'Runner seam missing.' }}
    . ([scriptblock]::Create($definition[0].Extent.Text))
}}
"""

    def run_script(self, body):
        result = subprocess.run([self.pwsh, "-NoProfile", "-NonInteractive", "-Command", self.functions + body],
                                capture_output=True, text=True, check=False)
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertNotIn(MARKER, result.stdout + result.stderr)
        self.assertNotIn(str(REPOSITORY), result.stdout)
        return json.loads(result.stdout)

    def test_guest_id_and_name_are_independent_of_ui_opt_in(self):
        source = RUNNER.read_text(encoding="utf-8")
        guest_function = source.split("function Assert-InstalledSmokeGuest {", 1)[1].split("\nfunction ", 1)[0]
        self.assertNotIn("WINTODO_ALLOW_LOCAL_UI", guest_function)
        self.assertLess(source.index("Assert-InstalledSmokeGuest @{"), source.index("if ($Execute)"))
        result = self.run_script(f"""
$snapshot = @{{ VmName = 'WintodoTest'; VmId = '{VM_ID}'; Manufacturer = 'Microsoft Corporation';
    Model = 'Virtual Machine'; Architecture = 'x64'; DeveloperMode = 0; OsBuild = 26100; Interactive = $true }}
Assert-InstalledSmokeGuest $snapshot -VmId '{VM_ID}' -Architecture x64
$rejected = 0
foreach ($field in @('VmName', 'VmId', 'Manufacturer', 'Model', 'Architecture')) {{
    $bad = $snapshot.Clone(); $bad[$field] = '{MARKER}'
    try {{ Assert-InstalledSmokeGuest $bad -VmId '{VM_ID}' -Architecture x64; throw 'Accepted mismatch.' }}
    catch {{ if ($_.Exception.Message -cne 'WINTODO_INSTALLED_SMOKE_GUEST_REJECTED') {{ throw }}; $rejected++ }}
}}
foreach ($change in @(@{{ DeveloperMode = 1 }}, @{{ OsBuild = 26000 }}, @{{ Interactive = $false }})) {{
    $bad = $snapshot.Clone(); foreach ($key in $change.Keys) {{ $bad[$key] = $change[$key] }}
    try {{ Assert-InstalledSmokeGuest $bad -VmId '{VM_ID}' -Architecture x64; throw 'Accepted baseline.' }}
    catch {{ if ($_.Exception.Message -cne 'WINTODO_INSTALLED_SMOKE_GUEST_REJECTED') {{ throw }}; $rejected++ }}
}}
@{{ rejected = $rejected }} | ConvertTo-Json -Compress
""")
        self.assertEqual(8, result["rejected"])

    def test_guest_guid_braces_are_normalized_but_empty_pin_rejected(self):
        result = self.run_script(f"""
$snapshot = @{{ VmName = 'WintodoTest'; VmId = '{{{VM_ID}}}'; Manufacturer = 'Microsoft Corporation';
    Model = 'Virtual Machine'; Architecture = 'Arm64'; DeveloperMode = 0; OsBuild = 26100; Interactive = $true }}
Assert-InstalledSmokeGuest $snapshot -VmId '{VM_ID}' -Architecture ARM64
try {{ Assert-InstalledSmokeGuest $snapshot -VmId ([guid]::Empty) -Architecture ARM64; throw 'Accepted empty pin.' }}
catch {{ @{{ error = $_.Exception.Message }} | ConvertTo-Json -Compress }}
""")
        self.assertEqual({"error": "WINTODO_INSTALLED_SMOKE_GUEST_REJECTED"}, result)

    def registration_fixture(self):
        return f"""
$registration = @{{ Name = 'Approved.App'; Publisher = 'CN=Approved'; Version = '1.0.0.7';
    Architecture = 'x64'; IsDevelopmentMode = $false; Status = 'Ok'; SignatureKind = 'Enterprise';
    InstallLocation = 'C:\\Program Files\\WindowsApps\\approved-package' }}
$process = @{{ Id = 4242; Path = (Join-Path $registration.InstallLocation 'Wintodo.exe');
    SessionId = 5; Responding = $true }}
$expected = @{{ Identity = 'Approved.App'; Publisher = 'CN=Approved'; Version = '1.0.0.7';
    Architecture = 'x64'; AppPid = 4242; SessionId = 5 }}
"""

    def test_exact_registration_and_installed_process_path(self):
        result = self.run_script(self.registration_fixture() + f"""
$null = Assert-InstalledSmokeRegistration $registration $process $expected
$rejected = 0
foreach ($change in @(@{{ Name = '{MARKER}' }}, @{{ Publisher = '{MARKER}' }},
    @{{ Version = '1.0.0.6' }}, @{{ Architecture = 'Arm64' }}, @{{ IsDevelopmentMode = $true }},
    @{{ SignatureKind = 'Developer' }}, @{{ SignatureKind = 'None' }}, @{{ Status = 'Modified' }})) {{
    $bad = $registration.Clone(); foreach ($key in $change.Keys) {{ $bad[$key] = $change[$key] }}
    try {{ $null = Assert-InstalledSmokeRegistration $bad $process $expected; throw 'Accepted registration.' }}
    catch {{ if ($_.Exception.Message -cne 'WINTODO_INSTALLED_SMOKE_REGISTRATION_REJECTED') {{ throw }}; $rejected++ }}
}}
foreach ($change in @(@{{ Id = 9999 }}, @{{ Path = 'C:\\scratch\\Wintodo.exe' }},
    @{{ SessionId = 8 }}, @{{ Responding = $false }})) {{
    $bad = $process.Clone(); foreach ($key in $change.Keys) {{ $bad[$key] = $change[$key] }}
    try {{ $null = Assert-InstalledSmokeRegistration $registration $bad $expected; throw 'Accepted process.' }}
    catch {{ if ($_.Exception.Message -cne 'WINTODO_INSTALLED_SMOKE_REGISTRATION_REJECTED') {{ throw }}; $rejected++ }}
}}
@{{ rejected = $rejected }} | ConvertTo-Json -Compress
""")
        self.assertEqual(12, result["rejected"])

    def test_reused_pid_or_changed_process_is_rejected(self):
        result = self.run_script("""
$before = @{ Id = 42; Path = 'C:\\WindowsApps\\Wintodo.exe'; StartTime = 100; SessionId = 5; Responding = $true }
Assert-InstalledSmokeProcessUnchanged $before $before
$rejected = 0
foreach ($change in @(@{ Id = 99 }, @{ StartTime = 101 }, @{ Path = 'C:\\scratch\\Wintodo.exe' },
    @{ SessionId = 8 }, @{ Responding = $false })) {
    $bad = $before.Clone(); foreach ($key in $change.Keys) { $bad[$key] = $change[$key] }
    try { Assert-InstalledSmokeProcessUnchanged $before $bad; throw 'Accepted changed process.' }
    catch { if ($_.Exception.Message -cne 'WINTODO_INSTALLED_SMOKE_PROCESS_CHANGED') { throw }; $rejected++ }
}
@{ rejected = $rejected } | ConvertTo-Json -Compress
""")
        self.assertEqual(5, result["rejected"])

    def test_inspection_supports_nested_window_and_flat_shapes(self):
        result = self.run_script("""
$node = @{ type = 'Button'; name = 'Synthetic'; automationId = 'Todo.List.Add' }
$flat = Assert-InstalledSmokeInspection @{ elements = @($node) }
$nested = Assert-InstalledSmokeInspection @{ windows = @(@{ elements = @(@{ type = 'Pane'; children = @($node) }) }) }
@{ flat = $flat; nested = $nested } | ConvertTo-Json -Compress
""")
        self.assertEqual({"flat": 1, "nested": 1}, result)

    def test_empty_noninteractive_and_missing_ids_never_pass(self):
        result = self.run_script(f"""
$rejected = 0
foreach ($inspection in @(@{{}}, @{{ elements = @() }}, @{{ elements = @(@{{ type = 'Pane' }}) }},
    @{{ windows = @(@{{ elements = @(@{{ type = 'Button'; name = '{MARKER}' }}) }}) }})) {{
    try {{ $null = Assert-InstalledSmokeInspection $inspection; throw 'Accepted empty audit.' }}
    catch {{ if ($_.Exception.Message -cne 'WINTODO_INSTALLED_SMOKE_INSPECTION_REJECTED') {{ throw }}; $rejected++ }}
}}
@{{ rejected = $rejected }} | ConvertTo-Json -Compress
""")
        self.assertEqual(4, result["rejected"])

    def test_inspection_is_bounded(self):
        result = self.run_script("""
$nodes = @(1..4097 | ForEach-Object { @{ type = 'Button'; automationId = 'Todo.Test.Element' } })
try { $null = Assert-InstalledSmokeInspection @{ elements = $nodes }; throw 'Accepted unbounded inspection.' }
catch { @{ error = $_.Exception.Message } | ConvertTo-Json -Compress }
""")
        self.assertEqual({"error": "WINTODO_INSTALLED_SMOKE_INSPECTION_REJECTED"}, result)

    def test_native_failure_and_missing_exit_code_cannot_use_stale_success(self):
        result = self.run_script(f"""
$AppPid = 4242
$rejected = 0
foreach ($mode in @('failure', 'missing', 'throws')) {{
    $global:LASTEXITCODE = 0
    function winapp {{
        if ($mode -eq 'failure') {{ $global:LASTEXITCODE = 7; '{MARKER}' }}
        elseif ($mode -eq 'throws') {{ throw '{MARKER}' }}
        else {{ '{{}}' }}
    }}
    try {{ $null = Invoke-InstalledSmokeUi @('inspect', '--json'); throw 'Accepted native failure.' }}
    catch {{ if ($_.Exception.Message -cne 'WINTODO_INSTALLED_SMOKE_UI_COMMAND_FAILED') {{ throw }}; $rejected++ }}
}}
@{{ rejected = $rejected }} | ConvertTo-Json -Compress
""")
        self.assertEqual(3, result["rejected"])

    def test_native_commands_select_pid_and_yield_has_no_selector(self):
        result = self.run_script("""
$AppPid = 4242
$calls = [Collections.Generic.List[object]]::new()
function winapp { $calls.Add(@($args)); $global:LASTEXITCODE = 0; '{}' }
$null = Invoke-InstalledSmokeUi @('inspect', '--json')
$null = Invoke-InstalledSmokeUi @('yield')
@{ inspect = $calls[0]; yield = $calls[1] } | ConvertTo-Json -Depth 4 -Compress
""")
        self.assertEqual(["ui", "inspect", "--json", "-a", "4242"], result["inspect"])
        self.assertEqual(["ui", "yield"], result["yield"])

    def test_signature_presence_does_not_substitute_for_trust_and_signer_pin(self):
        result = self.run_script("""
$pin = 'ab' * 20
$signature = @{ Status = 'Valid'; SignerCertificate = @{ Thumbprint = $pin } }
Assert-InstalledSmokeSignature $signature -Thumbprint $pin
$rejected = 0
foreach ($bad in @(@{ Status = 'NotTrusted'; SignerCertificate = @{ Thumbprint = $pin } },
    @{ Status = 'Valid'; SignerCertificate = @{ Thumbprint = ('cd' * 20) } }, @{ Status = 'Valid' })) {
    try { Assert-InstalledSmokeSignature $bad -Thumbprint $pin; throw 'Accepted signature.' }
    catch { if ($_.Exception.Message -cne 'WINTODO_INSTALLED_SMOKE_SIGNATURE_REJECTED') { throw }; $rejected++ }
}
@{ rejected = $rejected } | ConvertTo-Json -Compress
""")
        self.assertEqual(3, result["rejected"])

    def test_screenshot_must_exist_and_have_bytes(self):
        with tempfile.TemporaryDirectory(prefix=MARKER, dir=self.scratch) as directory:
            root = Path(directory)
            (root / "empty.png").write_bytes(b"")
            (root / "synthetic.png").write_bytes(b"synthetic")
            result = self.run_script(f"""
$rejected = 0
foreach ($path in @({quote(root / 'missing.png')}, {quote(root / 'empty.png')})) {{
    try {{ Assert-InstalledSmokeScreenshot -Path $path; throw 'Accepted missing screenshot.' }}
    catch {{ if ($_.Exception.Message -cne 'WINTODO_INSTALLED_SMOKE_SCREENSHOT_MISSING') {{ throw }}; $rejected++ }}
}}
Assert-InstalledSmokeScreenshot -Path {quote(root / 'synthetic.png')}
@{{ rejected = $rejected }} | ConvertTo-Json -Compress
""")
            self.assertEqual(2, result["rejected"])

    def test_whole_runner_rejects_host_before_any_ui(self):
        # All observation commands are mocked. No registry/CIM/UI calls occur.
        result = self.run_script(f"""
$global:uiCalls = 0
function winapp {{ $global:uiCalls++; throw 'Unexpected UI.' }}
function Get-ItemProperty {{ @{{ VirtualMachineName = 'OWNER-HOST'; VirtualMachineId = '{VM_ID}' }} }}
function Get-CimInstance {{ @{{ Manufacturer = 'Microsoft Corporation'; Model = 'Virtual Machine'; BuildNumber = 26100 }} }}
function Test-Path {{ $false }}
$arguments = @{{ PackagePath = '{MARKER}.msix'; SourceCommit = ('ab' * 20); PackageSha256 = ('ab' * 32); ExecutableSha256 = ('cd' * 32);
    SignerThumbprint = ('ef' * 20); ExpectedIdentity = '{MARKER}'; ExpectedPublisher = '{MARKER}';
    ExpectedVersion = '1.0.0.7'; Architecture = 'x64'; AppPid = 4242; ExpectedVmId = '{VM_ID}'; ExpectedTitle = '{MARKER}'; Execute = $true }}
$output = & {quote(RUNNER)} @arguments | ConvertFrom-Json
@{{ report = $output; ui_calls = $global:uiCalls; code = $LASTEXITCODE }} | ConvertTo-Json -Depth 5 -Compress
""")
        self.assertEqual(0, result["ui_calls"])
        self.assertEqual(1, result["code"])
        self.assertEqual("FAIL", result["report"]["status"])
        self.assertEqual([{"category": "guest_boundary"}], result["report"]["findings"])


if __name__ == "__main__":
    unittest.main()
