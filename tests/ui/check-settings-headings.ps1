param([Parameter(Mandatory)][string]$Window)
$ErrorActionPreference = 'Stop'
if ($env:WINTODO_ALLOW_LOCAL_UI -ne '1') { throw 'Run through the authorized Settings UI gate.' }

# Use Windows PowerShell's UIA client: read what a screen reader receives, including
# offscreen headings, rather than inferring navigation structure from the font style.
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes
$handle = if ($Window -like '0x*') { [Convert]::ToInt64($Window.Substring(2), 16) } else { [long]$Window }
$element = [System.Windows.Automation.AutomationElement]
$root = $element::FromHandle([IntPtr]::new($handle))
$headings = @('Title', 'Heading.Appearance', 'Heading.Tasks', 'Heading.Notifications', 'Heading.Sync', 'Heading.Data', 'Heading.About')
foreach ($suffix in $headings) {
    $id = "Todo.Settings.$suffix"
    $condition = [System.Windows.Automation.PropertyCondition]::new($element::AutomationIdProperty, $id)
    $node = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
    if (!$node -or [string]::IsNullOrWhiteSpace($node.Current.Name)) { throw "Missing named heading: $id" }
    # The managed UIA client converts native 80051/80052 to Level1/Level2.
    # Reading with ignoreDefaultValue=true also distinguishes an unsupported property.
    $actual = $node.GetCurrentPropertyValue($element::HeadingLevelProperty, $true)
    $expected = if ($suffix -eq 'Title') { 'Level1' } else { 'Level2' }
    if ($actual -isnot [Enum] -or $actual.ToString() -cne $expected) {
        throw "Wrong UIA heading level for ${id}: $actual (expected $expected)"
    }
}
