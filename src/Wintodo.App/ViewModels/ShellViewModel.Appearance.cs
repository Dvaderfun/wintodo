using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using Wintodo.Core.Models;
using Wintodo.Core.Services;
using Wintodo.Services;

namespace Wintodo.ViewModels;

public sealed partial class ShellViewModel
{
    private readonly HashSet<string> _unsavedAppearancePreferences = new(StringComparer.Ordinal);

    internal Task AppearancePreferenceWrites { get; private set; } = Task.CompletedTask;

    [ObservableProperty]
    public partial bool IsAppearanceSaveErrorOpen { get; set; }

    internal string? GetFixedCategoryBackgroundTheme(string navigationKey)
    {
        if (DefaultList?.NavigationKey == navigationKey)
        {
            return DefaultList.BackgroundTheme;
        }

        return _fixedCategoryBackgrounds.GetValueOrDefault(navigationKey);
    }

    private static string GetFixedBackgroundSettingKey(string navigationKey)
    {
        string suffix = navigationKey switch
        {
            MyDayNavigationKey => "my_day",
            ImportantNavigationKey => "important",
            PlannedNavigationKey => "planned",
            AllNavigationKey => "all",
            CompletedNavigationKey => "completed",
            _ => throw new ArgumentOutOfRangeException(
                nameof(navigationKey),
                navigationKey,
                "Only fixed smart lists have background setting keys."),
        };
        return FixedBackgroundSettingPrefix + suffix;
    }

    private static string? NormalizeBackgroundTheme(string? value)
    {
        return value is "Coast"
            or "Forest"
            or "Mist"
            or "Sunrise"
            or "Indigo"
            or "Petals"
            or "Graphite"
            or "Field"
            ? value
            : null;
    }

    private void UpdateFixedCategoryIconColors()
    {
        MyDayIconColor = GetFixedCategoryIconColor(MyDayNavigationKey);
        ImportantIconColor = GetFixedCategoryIconColor(ImportantNavigationKey);
        PlannedIconColor = GetFixedCategoryIconColor(PlannedNavigationKey);
        AllIconColor = GetFixedCategoryIconColor(AllNavigationKey);
        CompletedIconColor = GetFixedCategoryIconColor(CompletedNavigationKey);
        TasksIconColor = GetFixedCategoryIconColor(DefaultList?.NavigationKey);
        TaskList.FixedHeaderIconColor = GetFixedCategoryIconColor(_selectedNavigationKey);
    }

    private ListColor? GetFixedCategoryIconColor(string? navigationKey)
    {
        if (!TintFixedCategoryIcons || navigationKey is null)
        {
            return null;
        }

        string? backgroundTheme = GetFixedCategoryBackgroundTheme(navigationKey);
        if (backgroundTheme is not null)
        {
            return backgroundTheme switch
            {
                "Coast" => ListColor.Blue,
                "Forest" => ListColor.Green,
                "Mist" => ListColor.Teal,
                "Sunrise" => ListColor.Orange,
                "Indigo" => ListColor.Purple,
                "Petals" => ListColor.Pink,
                "Graphite" => ListColor.Graphite,
                "Field" => ListColor.Lime,
                _ => ListColor.Blue,
            };
        }

        if (DefaultList is { } defaultList
            && defaultList.NavigationKey == navigationKey)
        {
            return defaultList.Color;
        }

        return navigationKey switch
        {
            MyDayNavigationKey => ListColor.Yellow,
            ImportantNavigationKey => ListColor.Pink,
            PlannedNavigationKey => ListColor.Blue,
            AllNavigationKey => ListColor.Purple,
            CompletedNavigationKey => ListColor.Green,
            _ => null,
        };
    }

    private void OnBackgroundThemeChanged(string navigationKey, string? theme)
    {
        if (_fixedCategoryBackgrounds.ContainsKey(navigationKey))
        {
            _fixedCategoryBackgrounds[navigationKey] = theme;
        }

        UpdateFixedCategoryIconColors();
        AppearanceSettingsChanged?.Invoke();
    }

    internal static int ParseStartupTheme(string? setting) => ParseChoice(setting, maximum: 2);

    partial void OnAppearanceThemeIndexChanged(int value)
    {
        SaveAppearanceSetting(ShellStateService.AppearanceThemeSetting, value.ToString(
            System.Globalization.CultureInfo.InvariantCulture));
    }

    partial void OnDensityIndexChanged(int value)
    {
        TaskList.IsCompactDensity = value == 1;
        SaveAppearanceSetting(DensitySetting, value.ToString(
            System.Globalization.CultureInfo.InvariantCulture));
    }

    partial void OnShowListBackgroundsChanged(bool value)
    {
        SaveAppearanceSetting(ShowListBackgroundsSetting, value.ToString());
    }

    partial void OnTintFixedCategoryIconsChanged(bool value)
    {
        UpdateFixedCategoryIconColors();
        SaveAppearanceSetting(TintFixedCategoryIconsSetting, value.ToString());
    }

    partial void OnLaunchAtLoginChanged(bool value)
    {
        if (_areWindowsSettingsLoaded && !_isApplyingStartupTaskState)
        {
            _ = UpdateStartupTaskAsync(value);
        }
    }

    private async Task<StartupTaskSettingState> LoadStartupTaskStateAsync()
    {
        try
        {
            return await _startupTaskService.GetStateAsync();
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"WINTODO_STARTUP_TASK_READ_ERROR={DiagnosticException.Describe(exception)}");
            return new StartupTaskSettingState(false, false);
        }
    }

    private async Task ApplyStartupTaskStateWhenReadyAsync(
        Task<StartupTaskSettingState> startupTask)
    {
        ApplyStartupTaskState(await startupTask);
        _areWindowsSettingsLoaded = true;
    }

    private async Task UpdateStartupTaskAsync(bool enabled)
    {
        try
        {
            StartupTaskSettingState state =
                await _startupTaskService.SetEnabledAsync(enabled);
            ApplyStartupTaskState(state);
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"WINTODO_STARTUP_TASK_WRITE_ERROR={DiagnosticException.Describe(exception)}");
            ApplyStartupTaskState(await LoadStartupTaskStateAsync());
        }
    }

    private void ApplyStartupTaskState(StartupTaskSettingState state)
    {
        _isApplyingStartupTaskState = true;
        LaunchAtLogin = state.IsEnabled;
        CanChangeLaunchAtLogin = state.CanChange;
        _isApplyingStartupTaskState = false;
    }

    internal bool HasPendingDetailPaneWidth =>
        (_areAppearanceSettingsLoaded && DetailPaneWidth != _queuedDetailPaneWidth)
        || !_detailPaneWidthWrite.IsCompleted;

    internal Task FlushDetailPaneWidthAsync()
    {
        if (_areAppearanceSettingsLoaded && DetailPaneWidth != _queuedDetailPaneWidth)
        {
            _queuedDetailPaneWidth = DetailPaneWidth;
            _detailPaneWidthWrite = PersistWidthAfterAsync(_detailPaneWidthWrite, DetailPaneWidth);
        }
        return _detailPaneWidthWrite;
    }

    private async Task PersistWidthAfterAsync(Task previous, double width)
    {
        // A slower earlier write must not replace the last gesture's width.
        await previous;
        await PersistAppearanceSettingAsync(
            DetailPaneWidthSetting, width.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static double ParseDetailPaneWidth(string? value)
    {
        return double.TryParse(
                value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out double width) && double.IsFinite(width)
            ? Math.Clamp(width, MinimumDetailPaneWidth, MaximumDetailPaneWidth)
            : DefaultDetailPaneWidth;
    }

    private void SaveAppearanceSetting(string key, string value)
    {
        AppearanceSettingsChanged?.Invoke();
        if (_areAppearanceSettingsLoaded)
        {
            AppearancePreferenceWrites = SavePreferenceAfterAsync(AppearancePreferenceWrites, key, value);
        }
    }

    private async Task SavePreferenceAfterAsync(Task previous, string key, string value)
    {
        // Keep rapid selections in order, including the success or error shown for each write.
        await previous;
        if (!await PersistAppearanceSettingAsync(key, value))
        {
            _unsavedAppearancePreferences.Add(key);
            IsAppearanceSaveErrorOpen = true;
        }
        else if (_unsavedAppearancePreferences.Remove(key) && _unsavedAppearancePreferences.Count == 0)
        {
            IsAppearanceSaveErrorOpen = false;
        }
    }

    private async Task<bool> PersistAppearanceSettingAsync(string key, string value)
    {
        try
        {
            await _taskStore.SetSettingAsync(key, value);
            return true;
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"WINTODO_APPEARANCE_SETTING_ERROR={DiagnosticException.Describe(exception)}");
            return false;
        }
    }

    private static int ParseChoice(string? value, int maximum)
    {
        return int.TryParse(
                value,
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out int choice)
            ? Math.Clamp(choice, 0, maximum)
            : 0;
    }

}
