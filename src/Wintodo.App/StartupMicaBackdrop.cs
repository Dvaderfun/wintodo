#if DEBUG || WINTODO_PRIVATE_PREVIEW
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Wintodo;

// A private preview with an explicit theme must not first render the system-theme Mica.
// WinUI's default configuration continues to track activation, contrast and later theme changes.
internal sealed partial class StartupMicaBackdrop(ElementTheme initialTheme) : SystemBackdrop
{
    private MicaController? _controller;
    private bool _firstConnection = true;

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(target, xamlRoot);
        var configuration = GetDefaultSystemBackdropConfiguration(target, xamlRoot);
        if (_firstConnection)
        {
            configuration.Theme = initialTheme == ElementTheme.Dark ? SystemBackdropTheme.Dark : SystemBackdropTheme.Light;
            _firstConnection = false; // Reconnection must use the current theme, not the launch preference.
        }
        _controller = new MicaController { Kind = MicaKind.Base };
        // The standard MicaBackdrop attaches the target first and applies configuration second.
        // Reverse those calls so its first brush uses the same palette as the startup logo.
        _controller.SetSystemBackdropConfiguration(configuration);
        _controller.AddSystemBackdropTarget(target);
    }

    protected override void OnDefaultSystemBackdropConfigurationChanged(
        ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
    {
        // WinUI has already updated the live configuration given to our controller.
        // The notification can arrive with a null target; forwarding it to the
        // projected base throws E_INVALIDARG (microsoft/microsoft-ui-xaml#11535).
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop target)
    {
        _controller?.RemoveSystemBackdropTarget(target);
        _controller?.Dispose();
        _controller = null;
        base.OnTargetDisconnected(target);
    }
}
#endif
