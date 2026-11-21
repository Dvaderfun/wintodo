# Called in Windows PowerShell for its inbox WinRT projection. Notification is best effort.
param([ValidateSet('Changes','Review','ChangesAndReview')][string]$Reason = 'Review')
$ErrorActionPreference = 'Stop'
[void][Windows.UI.Notifications.ToastNotificationManager,Windows.UI.Notifications,ContentType=WindowsRuntime]
[void][Windows.Data.Xml.Dom.XmlDocument,Windows.Data.Xml.Dom.XmlDocument,ContentType=WindowsRuntime]
$xml = New-Object Windows.Data.Xml.Dom.XmlDocument
$title = switch ($Reason) {
    'Changes' {'Wintodo maintenance updated tools'}
    'ChangesAndReview' {'Wintodo maintenance updated tools and needs review'}
    'Review' {'Wintodo maintenance needs review'}
}
# Static display text only. Opening the controls or report is always the user's choice.
$xml.LoadXml('<toast><visual><binding template="ToastGeneric"><text>'+$title+'</text><text>Controls: %LOCALAPPDATA%\WintodoMaintenance\Maintenance.cmd</text><text>Report: %LOCALAPPDATA%\WintodoMaintenance\reports\latest.md</text></binding></visual></toast>')
$toast = [Windows.UI.Notifications.ToastNotification]::new($xml)
$toast.Tag = 'weekly-review'
$toast.Group = 'maintenance'
[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('Wintodo.Workstation.Maintenance').Show($toast)
