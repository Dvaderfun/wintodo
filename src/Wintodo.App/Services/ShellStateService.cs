using System.Globalization;
using Wintodo.Core.Services;

namespace Wintodo.Services;

internal sealed class ShellStateService
{
    private const string WindowXKey = "shell.window.x";
    private const string WindowYKey = "shell.window.y";
    private const string WindowWidthKey = "shell.window.width";
    private const string WindowHeightKey = "shell.window.height";
    private const string WindowMaximizedKey = "shell.window.maximized";
    private const string SelectedNavigationKey = "shell.navigation.selected";

    private readonly ISettingsStore _settingsStore;

    internal ShellStateService(ISettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
    }

    internal async Task<WindowPlacementState?> LoadWindowPlacementAsync(
        CancellationToken cancellationToken = default)
    {
        string? xText = await _settingsStore.GetSettingAsync(WindowXKey, cancellationToken);
        string? yText = await _settingsStore.GetSettingAsync(WindowYKey, cancellationToken);
        string? widthText = await _settingsStore.GetSettingAsync(WindowWidthKey, cancellationToken);
        string? heightText = await _settingsStore.GetSettingAsync(WindowHeightKey, cancellationToken);
        string? maximizedText = await _settingsStore.GetSettingAsync(WindowMaximizedKey, cancellationToken);

        if (!int.TryParse(xText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int x)
            || !int.TryParse(yText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int y)
            || !double.TryParse(widthText, NumberStyles.Float, CultureInfo.InvariantCulture, out double width)
            || !double.TryParse(heightText, NumberStyles.Float, CultureInfo.InvariantCulture, out double height)
            || width < 640
            || height < 480)
        {
            return null;
        }

        return new WindowPlacementState(
            x,
            y,
            width,
            height,
            bool.TryParse(maximizedText, out bool maximized) && maximized);
    }

    internal Task SaveWindowPlacementAsync(
        WindowPlacementState state,
        CancellationToken cancellationToken = default)
    {
        return Task.WhenAll(
            _settingsStore.SetSettingAsync(
                WindowXKey,
                state.X.ToString(CultureInfo.InvariantCulture),
                cancellationToken),
            _settingsStore.SetSettingAsync(
                WindowYKey,
                state.Y.ToString(CultureInfo.InvariantCulture),
                cancellationToken),
            _settingsStore.SetSettingAsync(
                WindowWidthKey,
                state.WidthDip.ToString("R", CultureInfo.InvariantCulture),
                cancellationToken),
            _settingsStore.SetSettingAsync(
                WindowHeightKey,
                state.HeightDip.ToString("R", CultureInfo.InvariantCulture),
                cancellationToken),
            _settingsStore.SetSettingAsync(
                WindowMaximizedKey,
                state.IsMaximized.ToString(CultureInfo.InvariantCulture),
                cancellationToken));
    }

    internal Task<string?> LoadSelectedNavigationAsync(
        CancellationToken cancellationToken = default)
    {
        return _settingsStore.GetSettingAsync(SelectedNavigationKey, cancellationToken);
    }

    internal Task SaveSelectedNavigationAsync(
        string navigationKey,
        CancellationToken cancellationToken = default)
    {
        return _settingsStore.SetSettingAsync(
            SelectedNavigationKey,
            navigationKey,
            cancellationToken);
    }
}

internal readonly record struct WindowPlacementState(
    int X,
    int Y,
    double WidthDip,
    double HeightDip,
    bool IsMaximized);
