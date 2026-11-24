using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.UI.Xaml;
using Windows.UI.ViewManagement;
using Wintodo.Core.Models;
using Wintodo.Services;
using Wintodo.ViewModels;

namespace Wintodo.Pages;

public sealed partial class ShellPage
{
    private const int ResizeSettleMilliseconds = 500;
    private string? _displayedBackgroundAsset;
    private bool _isDraggingDetailSizer;
    private bool _waitingForPaneResize;
    private bool _stoppedPaneResize;
    private long _paneResizeDue;

    public static Visibility BoolToVisibility(bool value)
    {
        return value ? Visibility.Visible : Visibility.Collapsed;
    }

    public static Visibility CountToVisibility(int count)
    {
        return count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public static Visibility NewItemActionsVisibility(bool isCreatingList, bool isCreatingGroup)
    {
        return isCreatingList || isCreatingGroup ? Visibility.Collapsed : Visibility.Visible;
    }

    public static ElementTheme ThemeForIndex(int index)
    {
        return index switch
        {
            1 => ElementTheme.Light,
            2 => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
    }

    public InfoBarSeverity ToInfoBarSeverity(bool isError)
    {
        return isError ? InfoBarSeverity.Error : InfoBarSeverity.Success;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(
        uint action,
        uint parameter,
        ref HighContrastState state,
        uint updateFlags);

    private static bool IsSystemHighContrastEnabled()
    {
        var state = new HighContrastState
        {
            Size = checked((uint)Unsafe.SizeOf<HighContrastState>()),
        };
        return SystemParametersInfo(
                SpiGetHighContrast,
                state.Size,
                ref state,
                updateFlags: 0)
            && (state.Flags & HighContrastOn) != 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HighContrastState
    {
        public uint Size;

        public uint Flags;

        public nint DefaultScheme;
    }

    private void OnActualThemeChanged(FrameworkElement sender, object args)
    {
        string? selectedKey =
            (ShellNavigation.SelectedItem as NavigationViewItem)?.Tag as string;
        PopulateUserListNavigationItems();
        _selectedList = ViewModel.UserLists.FirstOrDefault(
            list => list.NavigationKey == selectedKey);
        ApplyListAccent(_selectedList);
        ApplyListBackground();
        ViewModel.RefreshFixedCategoryIconBrushes();
    }

    private void OnColorValuesChanged(UISettings sender, object args)
    {
        bool isHighContrast = IsSystemHighContrastEnabled();
        DispatcherQueue.TryEnqueue(() =>
        {
            _isHighContrast = isHighContrast;
            ApplyListAccent(_selectedList, isHighContrast);
            RefreshUserListAccentBrushes(isHighContrast);
            ApplyListBackground(isHighContrast);
            ViewModel.RefreshFixedCategoryIconBrushes();
        });
    }

    private void ApplyListAccent(
        ShellListItemViewModel? list,
        bool? isHighContrast = null)
    {
        string resourceKey = list?.AccentResourceKey ?? "ListAccentBrush";
        Brush brush = (isHighContrast ?? _isHighContrast)
            ? ResolveBrush("SystemColorHighlightColorBrush")
            : ResolveBrush(resourceKey);
        TaskList.Resources["ListAccentBrush"] = brush;
        TaskList.SetHeaderPlaceholderBrush((isHighContrast ?? _isHighContrast)
            ? ResolveBrush("SystemColorWindowTextColorBrush")
            : ResolveHeaderPlaceholderBrush(list));
        DetailPane.Resources["ListAccentBrush"] = brush;
        ShellNavigation.Resources["NavigationViewSelectionIndicatorForeground"] = brush;
    }

    private void UpdateFixedCategoryNavigationAccessibility()
    {
        SetFixedCategoryNavigationAccessibleName(
            MyDayNavigationItem,
            ViewModel.MyDayTaskCount);
        SetFixedCategoryNavigationAccessibleName(
            ImportantNavigationItem,
            ViewModel.ImportantTaskCount);
        SetFixedCategoryNavigationAccessibleName(
            PlannedNavigationItem,
            ViewModel.PlannedTaskCount);
        SetFixedCategoryNavigationAccessibleName(
            AllNavigationItem,
            ViewModel.AllTaskCount);
        SetFixedCategoryNavigationAccessibleName(
            CompletedNavigationItem,
            ViewModel.CompletedTaskCount);
    }

    private void SetFixedCategoryNavigationAccessibleName(
        NavigationViewItem item,
        int count)
    {
        string title = item.Content?.ToString() ?? string.Empty;
        string accessibleName = count == 0
            ? title
            : string.Format(
                DisplayCulture.Current,
                _resources.GetString("FixedCategory_AccessibleNameWithCount"),
                title,
                count);
        AutomationProperties.SetName(item, accessibleName);
    }

    private void RefreshUserListAccentBrushes(bool isHighContrast)
    {
        foreach (ShellListItemViewModel list in ViewModel.UserLists)
        {
            if (_navigationItems.TryGetValue(list.NavigationKey, out NavigationViewItem? item))
            {
                RefreshUserListAccentBrush(item, list, isHighContrast);
            }
        }
    }

    private void OnUserListSelectionChanged(DependencyObject sender, DependencyProperty property)
    {
        if (sender is NavigationViewItem { Tag: string navigationKey } item
            && ViewModel.UserLists.FirstOrDefault(list => list.NavigationKey == navigationKey)
                is { } list)
        {
            RefreshUserListAccentBrush(item, list, _isHighContrast);
        }
    }

    private void RefreshUserListAccentBrush(
        NavigationViewItem item,
        ShellListItemViewModel list,
        bool isHighContrast)
    {
        if (item.Content is Grid { Children.Count: > 0 } content
            && content.Children[0] is Ellipse dot)
        {
            string resourceKey = isHighContrast
                ? item.IsSelected
                    ? "SystemColorHighlightTextColorBrush"
                    : "SystemColorWindowTextColorBrush"
                : list.AccentResourceKey;
            dot.Fill = ResolveBrush(resourceKey);
        }
    }

    private void OnAppearanceSettingsChanged()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            ThemeChanged?.Invoke(ThemeForIndex(ViewModel.AppearanceThemeIndex));
            ApplyListBackground();
        });
    }

    // The detail pane's upper drag limit depends on how much room the window has,
    // so it cannot come from a visual state the way its column and margin do.
    private void OnContentGridSizeChanged(object sender, SizeChangedEventArgs args)
    {
        const double listReserve = 320;
        DetailHost.MaxWidth = Math.Clamp(
            args.NewSize.Width - listReserve,
            DetailHost.MinWidth,
            ShellViewModel.MaximumDetailPaneWidth);
    }

    private void OnDetailResizeStarted(object sender, ManipulationStartedRoutedEventArgs args)
    {
        _isDraggingDetailSizer = true;
    }

    private void OnDetailResizeCompleted(object sender, ManipulationCompletedRoutedEventArgs args)
    {
        _isDraggingDetailSizer = false;
        _ = ViewModel.FlushDetailPaneWidthAsync();
        // The final list layout may not have been arranged yet.
        QueueSettledPaneResize();
    }

    private void OnDetailHostSizeChanged(object sender, SizeChangedEventArgs args)
    {
        // Width is the user's preference; a change in ActualWidth from a narrower
        // window must not overwrite it. Keyboard resizing also comes through here.
        if (args.PreviousSize.Width != args.NewSize.Width && ViewModel.HasPendingDetailPaneWidth)
            QueueSettledPaneResize();
    }

    private void OnListPaneSizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (!_isDraggingDetailSizer && args.PreviousSize.Width != args.NewSize.Width
            && ViewModel.ShowListBackgrounds && !_isHighContrast
            && ResolveBackgroundAsset(ViewModel.TaskList.BackgroundTheme) is not null)
            QueueSettledPaneResize();
    }

    private void QueueSettledPaneResize()
    {
        if (_isDraggingDetailSizer || _stoppedPaneResize) return;
        _paneResizeDue = Environment.TickCount64 + ResizeSettleMilliseconds;
        if (_waitingForPaneResize) return;
        _waitingForPaneResize = true;
        _ = FinishPaneResizeWhenIdleAsync();
    }

    private async Task FinishPaneResizeWhenIdleAsync()
    {
        // One delay for each burst, with at most one outstanding wait. Subsequent
        // size notifications only move the deadline; no cancellation source per pixel.
        long remaining;
        while (!_stoppedPaneResize && !_isDraggingDetailSizer
            && (remaining = _paneResizeDue - Environment.TickCount64) > 0)
            await Task.Delay(TimeSpan.FromMilliseconds(remaining));

        _waitingForPaneResize = false;
        if (_stoppedPaneResize || _isDraggingDetailSizer) return;
        ApplyListBackground();
        await ViewModel.FlushDetailPaneWidthAsync();
    }

    private void ApplyListBackground(bool? isHighContrast = null)
    {
        string? assetName = ResolveBackgroundAsset(ViewModel.TaskList.BackgroundTheme);
        if (!ViewModel.ShowListBackgrounds
            || assetName is null
            || (isHighContrast ?? _isHighContrast))
        {
            ListBackgroundImage.Source = null;
            _displayedBackgroundAsset = null;
            _backgroundDecodeWidth = 0;
            return;
        }

        int decodeWidth = GetBackgroundDecodeWidth();
        if (decodeWidth <= 0 || (_displayedBackgroundAsset == assetName
            && (_isDraggingDetailSizer || Math.Abs(decodeWidth - _backgroundDecodeWidth) < 64)))
        {
            return;
        }

        var bitmap = new BitmapImage
        {
            DecodePixelWidth = decodeWidth,
            UriSource = new Uri($"ms-appx:///Assets/ListBackgrounds/{assetName}.jpg"),
        };
        _displayedBackgroundAsset = assetName;
        _backgroundDecodeWidth = decodeWidth;
        ListBackgroundImage.Source = bitmap;
    }

    private int GetBackgroundDecodeWidth()
    {
        double scale = XamlRoot?.RasterizationScale ?? 1;
        return checked((int)Math.Ceiling(ListPane.ActualWidth * scale));
    }

    private static string? ResolveBackgroundAsset(string? theme)
    {
        return theme is "Coast"
            or "Forest"
            or "Mist"
            or "Sunrise"
            or "Indigo"
            or "Petals"
            or "Graphite"
            or "Field"
            ? theme
            : null;
    }

    private Brush ResolveBrush(string resourceKey)
    {
        return (Brush)Application.Current.Resources[resourceKey];
    }

    private Brush ResolveHeaderPlaceholderBrush(ShellListItemViewModel? list)
    {
        // Application-level lookup follows the system theme, not the app override.
        // Resolve the selected palette explicitly and update the shape immediately;
        // replacing a local resource does not invalidate an existing ThemeResource.
        string themeKey = ShellRoot.ActualTheme == ElementTheme.Dark ? "Dark" : "Light";
        string resourceKey = list?.AccentResourceKey ?? "ListHeaderPlaceholderBrush";
        foreach (ResourceDictionary merged in Application.Current.Resources.MergedDictionaries)
        {
            if (merged.ThemeDictionaries.TryGetValue(themeKey, out object? theme)
                && theme is ResourceDictionary palette
                && palette.TryGetValue(resourceKey, out object? value)
                && value is Brush resolved)
            {
                return resolved;
            }
        }

        return ResolveBrush(resourceKey);
    }

}
