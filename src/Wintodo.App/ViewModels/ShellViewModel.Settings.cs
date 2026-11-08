using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wintodo.Core.Services;
using Wintodo.Services;

namespace Wintodo.ViewModels;

public sealed partial class ShellViewModel
{
    public NotificationPreferences Notifications { get; }

    [ObservableProperty]
    public partial bool IsNotificationSettingsErrorOpen { get; set; }

    [ObservableProperty]
    public partial string NotificationSettingsErrorMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DatabasePath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DatabaseSizeText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string MicrosoftTodoAccountsText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsDataSettingsErrorOpen { get; set; }

    [ObservableProperty]
    public partial string DataSettingsErrorMessage { get; set; } = string.Empty;

    private void OnNotificationSettingsFailed(Exception exception)
    {
        Trace.WriteLine($"WINTODO_NOTIFICATION_SETTINGS_ERROR={DiagnosticException.Describe(exception)}");
        App.DispatcherQueue.TryEnqueue(() =>
        {
            NotificationSettingsErrorMessage = _resources.GetString("Settings_NotificationsSaveFailed");
            IsNotificationSettingsErrorOpen = true;
        });
    }

    private async Task RefreshDatabaseStatsAsync()
    {
        try
        {
            StoreStats stats = await _taskStore.GetStatsAsync();
            DatabasePath = stats.DatabasePath;
            DatabaseSizeText = string.Format(DisplayCulture.Current,
                _resources.GetString("Settings_DatabaseSizeFormat"), stats.DatabaseSizeBytes / 1024d);
            var sources = await _taskStore.GetImportSourcesAsync("mstodo");
            MicrosoftTodoAccountsText = string.Join(Environment.NewLine,
                sources.Where(source => source.LastImport is not null).Select(source => string.Format(DisplayCulture.Current,
                    _resources.GetString("Import_AccountHistoryFormat"), source.DisplayName,
                    TimeZoneInfo.ConvertTime(source.LastImport!.Value, _importClock.LocalTimeZone).ToString("d", DisplayCulture.Current))));
            IsDataSettingsErrorOpen = false;
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"WINTODO_DATABASE_STATS_ERROR={DiagnosticException.Describe(exception)}");
            DataSettingsErrorMessage = _resources.GetString("Settings_DatabaseStatsFailed");
            IsDataSettingsErrorOpen = true;
        }
    }

    [RelayCommand]
    private async Task ReadLicenceNoticesAsync()
    {
        try
        {
            Windows.Storage.StorageFile file = await Windows.Storage.StorageFile.GetFileFromApplicationUriAsync(
                new Uri("ms-appx:///Assets/ThirdPartyNotices.txt"));
            if (!await Windows.System.Launcher.LaunchFileAsync(file))
            {
                throw new IOException("No application opened the licence notices.");
            }
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"WINTODO_LICENCES_OPEN_ERROR={DiagnosticException.Describe(exception)}");
            DataSettingsErrorMessage = _resources.GetString("Settings_LicencesOpenFailed");
            IsDataSettingsErrorOpen = true;
        }
    }

    [RelayCommand]
    private void ShowDatabaseInExplorer()
    {
        if (string.IsNullOrWhiteSpace(DatabasePath))
        {
            return;
        }

        try
        {
            // ArgumentList keeps paths (including spaces) out of shell parsing.
            var startInfo = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
            startInfo.ArgumentList.Add("/select,");
            startInfo.ArgumentList.Add(DatabasePath);
            using Process? process = Process.Start(startInfo);
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"WINTODO_DATABASE_EXPLORER_ERROR={DiagnosticException.Describe(exception)}");
            DataSettingsErrorMessage = _resources.GetString("Settings_DatabaseExplorerFailed");
            IsDataSettingsErrorOpen = true;
        }
    }
}
