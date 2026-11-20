"""Keep the Settings licence summary in step with package pins and the shipped notices."""
from pathlib import Path
import re
import unittest
import xml.etree.ElementTree as ET


ROOT = Path(__file__).resolve().parents[2]
APP = ROOT / "src/Wintodo.App"


class SettingsLicencesTests(unittest.TestCase):
    def test_declared_versions_and_supplier_labels_match(self):
        central = ET.parse(ROOT / "Directory.Packages.props").findall(".//PackageVersion")
        resources = ET.parse(APP / "Strings/en-US/Resources.resw")
        summary = resources.findtext("./data[@name='SettingsLicencesText.Text']/value")
        notices = (APP / "Assets/ThirdPartyNotices.txt").read_text(encoding="utf-8-sig")

        def package_rows(text):
            rows = []
            for line in text.split("\n\n", 2)[1].splitlines():
                match = re.fullmatch(r"(\S+) (\S+) — (.+)", line)
                self.assertIsNotNone(match, f"Unexpected package notice: {line}")
                rows.append(match.groups())
            return rows

        rows = package_rows(summary)
        self.assertCountEqual([(name, version) for name, version, _ in rows],
                              [(p.attrib["Include"], p.attrib["Version"]) for p in central])
        self.assertCountEqual(rows, package_rows(notices))
