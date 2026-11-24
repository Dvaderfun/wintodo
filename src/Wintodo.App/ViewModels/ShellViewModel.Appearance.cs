using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml;
using Wintodo.Core.Models;
using Wintodo.Core.Services;
using Wintodo.Services;

namespace Wintodo.ViewModels;

public sealed partial class ShellViewModel
{
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

    private void UpdateFixedCategoryIconBrushes()
    {
        MyDayIconBrush = ResolveAppearanceBrush(
            ResolveFixedCategoryIconBrushKey(MyDayNavigationKey));
        ImportantIconBrush = ResolveAppearanceBrush(
            ResolveFixedCategoryIconBrushKey(ImportantNavigationKey));
        PlannedIconBrush = ResolveAppearanceBrush(
            ResolveFixedCategoryIconBrushKey(PlannedNavigationKey));
        AllIconBrush = ResolveAppearanceBrush(
            ResolveFixedCategoryIconBrushKey(AllNavigationKey));
        CompletedIconBrush = ResolveAppearanceBrush(
            ResolveFixedCategoryIconBrushKey(CompletedNavigationKey));
        TasksIconBrush = ResolveAppearanceBrush(
            ResolveFixedCategoryIconBrushKey(DefaultList?.NavigationKey));
        TaskList.FixedHeaderIconBrush = ResolveAppearanceBrush(
            ResolveFixedCategoryIconBrushKey(_selectedNavigationKey));
    }

    internal void RefreshFixedCategoryIconBrushes()
    {
        UpdateFixedCategoryIconBrushes();
        OnPropertyChanged(nameof(MyDayIconBrush));
        OnPropertyChanged(nameof(ImportantIconBrush));
        OnPropertyChanged(nameof(PlannedIconBrush));
        OnPropertyChanged(nameof(AllIconBrush));
        OnPropertyChanged(nameof(CompletedIconBrush));
        OnPropertyChanged(nameof(TasksIconBrush));
        TaskList.RefreshFixedHeaderIconBrush();
    }

    private string ResolveFixedCategoryIconBrushKey(string? navigationKey)
    {
        if (!TintFixedCategoryIcons || !IsFixedCategoryNavigationKey(navigationKey))
        {
            return "TextFillColorPrimaryBrush";
        }

        string? backgroundTheme =
            GetFixedCategoryBackgroundTheme(navigationKey!);
        if (backgroundTheme is not null)
        {
            return backgroundTheme switch
            {
                "Coast" => "ListAccentBlueBrush",
                "Forest" => "ListAccentGreenBrush",
                "Mist" => "ListAccentTealBrush",
                "Sunrise" => "ListAccentOrangeBrush",
                "Indigo" => "ListAccentPurpleBrush",
                "Petals" => "ListAccentPinkBrush",
                "Graphite" => "ListAccentGraphiteBrush",
                "Field" => "ListAccentLimeBrush",
                _ => "ListAccentBlueBrush",
            };
        }

        if (DefaultList is { } defaultList
            && defaultList.NavigationKey == navigationKey)
        {
            return defaultList.AccentResourceKey;
        }

        return navigationKey switch
        {
            MyDayNavigationKey => "ListAccentYellowBrush",
            ImportantNavigationKey => "ListAccentPinkBrush",
            PlannedNavigationKey => "ListAccentBlueBrush",
            AllNavigationKey => "ListAccentPurpleBrush",
            CompletedNavigationKey => "ListAccentGreenBrush",
            _ => "TextFillColorPrimaryBrush",
        };
    }

    private bool IsFixedCategoryNavigationKey(string? navigationKey)
    {
        return navigationKey is MyDayNavigationKey
            or ImportantNavigationKey
            or PlannedNavigationKey
            or AllNavigationKey
            or CompletedNavigationKey
            || DefaultList?.NavigationKey == navigationKey;
    }

    private static Brush ResolveAppearanceBrush(string resourceKey)
    {
        return (Brush)Application.Current.Resources[resourceKey];
    }

    private void OnBackgroundThemeChanged(string navigationKey, string? theme)
    {
        if (_fixedCategoryBackgrounds.ContainsKey(navigationKey))
        {
            _fixedCategoryBackgrounds[navigationKey] = theme;
        }

        UpdateFixedCategoryIconBrushes();
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
        UpdateFixedCategoryIconBrushes();
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
            _ = PersistAppearanceSettingAsync(key, value);
        }
    }

    private async Task PersistAppearanceSettingAsync(string key, string value)
    {
        try
        {
            await _taskStore.SetSettingAsync(key, value);
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"WINTODO_APPEARANCE_SETTING_ERROR={DiagnosticException.Describe(exception)}");
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
