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
    internal const string SelectedNavigationKey = "shell.navigation.selected";
    private static readonly string[] PlacementKeys = [WindowXKey, WindowYKey, WindowWidthKey, WindowHeightKey, WindowMaximizedKey];

    private readonly ISettingsStore _settingsStore;

    internal ShellStateService(ISettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
    }

    internal async Task<WindowPlacementState?> LoadWindowPlacementAsync(
        CancellationToken cancellationToken = default)
    {
        var values = await _settingsStore.GetSettingsAsync(PlacementKeys, cancellationToken).ConfigureAwait(false);

        if (!int.TryParse(values.GetValueOrDefault(WindowXKey), NumberStyles.Integer, CultureInfo.InvariantCulture, out int x)
            || !int.TryParse(values.GetValueOrDefault(WindowYKey), NumberStyles.Integer, CultureInfo.InvariantCulture, out int y)
            || !double.TryParse(values.GetValueOrDefault(WindowWidthKey), NumberStyles.Float, CultureInfo.InvariantCulture, out double width)
            || !double.TryParse(values.GetValueOrDefault(WindowHeightKey), NumberStyles.Float, CultureInfo.InvariantCulture, out double height)
            || !double.IsFinite(width) || width <= 0
            || !double.IsFinite(height) || height <= 0)
        {
            return null;
        }

        return new WindowPlacementState(
            x,
            y,
            width,
            height,
            bool.TryParse(values.GetValueOrDefault(WindowMaximizedKey), out bool maximized) && maximized);
    }

    internal Task SaveWindowPlacementAsync(
        WindowPlacementState state,
        CancellationToken cancellationToken = default)
    {
        return _settingsStore.SetSettingsAsync(new Dictionary<string, string>
        {
            [WindowXKey] = state.X.ToString(CultureInfo.InvariantCulture),
            [WindowYKey] = state.Y.ToString(CultureInfo.InvariantCulture),
            [WindowWidthKey] = state.WidthDip.ToString("R", CultureInfo.InvariantCulture),
            [WindowHeightKey] = state.HeightDip.ToString("R", CultureInfo.InvariantCulture),
            [WindowMaximizedKey] = state.IsMaximized.ToString(CultureInfo.InvariantCulture),
        }, cancellationToken);
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
