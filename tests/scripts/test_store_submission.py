"""Contract with the reserved Store family and the tag job's GitHub protection check."""
from pathlib import Path
import subprocess
import unittest
import xml.etree.ElementTree as ET


ROOT = Path(__file__).resolve().parents[2]


class StoreSubmissionTests(unittest.TestCase):
    def test_reserved_family_and_publisher_survive_packaging_edits(self):
        app = ROOT / "src/Wintodo.App"
        manifest = ET.parse(app / "Package.appxmanifest")
        identity = manifest.find("{*}Identity")
        self.assertEqual("47371DermenzhyStudio.Wintodo", identity.get("Name"))
        self.assertEqual("CN=03D194EC-2260-464A-80F9-EF22C07F62D0", identity.get("Publisher"))
        display = manifest.findtext("{*}Properties/{*}PublisherDisplayName")
        label = ET.parse(app / "Strings/en-US/Resources.resw").findtext("data[@name='PublisherDisplayName']/value")
        self.assertEqual(("Univera LLC", "Univera LLC"), (display, label))

    def test_only_protected_tag_and_confirmed_main_proceed(self):
        # Exercise the actual tag guard without a token, remote mutation or tag.
        result = subprocess.run(["pwsh", "-NoProfile", "-NonInteractive", "-Command", """
function gh {
    if (($args -join ' ') -cne 'api repos/{owner}/{repo}/branches/main --jq .protected') { throw 'Unexpected query' }
    $global:LASTEXITCODE = $global:apiCode
    $global:apiValue
}
foreach ($row in @(
    @('push','refs/tags/v1.0.0','true','true',0,$true),
    @('push','refs/tags/v1.0.0','false','true',0,$false),
    @('push','refs/tags/v1.0.0','true','false',0,$false),
    @('push','refs/tags/v1.0.0','true','true',1,$false),
    @('push','refs/tags/v1.0.0','true','',0,$false),
    @('pull_request','refs/pull/1/merge','true','true',0,$false),
    @('push','refs/heads/main','true','true',0,$false))) {
    $env:GITHUB_EVENT_NAME,$env:GITHUB_REF,$env:GITHUB_REF_PROTECTED,$global:apiValue,$global:apiCode,$expected = $row
    try { & ./scripts/Assert-StoreRefProtection.ps1; $allowed = $true } catch { $allowed = $false; $reason = $_.Exception.Message }
    if ($allowed -ne $expected) { throw "Incorrect decision for [$row]: $reason" }
}
exit 0
"""], cwd=ROOT, capture_output=True, text=True, timeout=30)
        self.assertEqual(0, result.returncode, result.stderr)
