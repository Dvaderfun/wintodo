# Called in Windows PowerShell for its inbox WinRT projection. Notification is best effort.
$ErrorActionPreference = 'Stop'
[void][Windows.UI.Notifications.ToastNotificationManager,Windows.UI.Notifications,ContentType=WindowsRuntime]
[void][Windows.Data.Xml.Dom.XmlDocument,Windows.Data.Xml.Dom.XmlDocument,ContentType=WindowsRuntime]
$xml = New-Object Windows.Data.Xml.Dom.XmlDocument
$xml.LoadXml('<toast><visual><binding template="ToastGeneric"><text>Wintodo maintenance needs review</text><text>Open %LOCALAPPDATA%\WintodoMaintenance\reports\latest.md for the component, result and next step.</text></binding></visual></toast>')
$toast = [Windows.UI.Notifications.ToastNotification]::new($xml)
$toast.Tag = 'weekly-review'
$toast.Group = 'maintenance'
[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('Wintodo.Workstation.Maintenance').Show($toast)
