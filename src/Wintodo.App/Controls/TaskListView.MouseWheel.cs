using System.Runtime.InteropServices;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace Wintodo.Controls;

public sealed partial class TaskListView
{
    private UIElement? _wheelInputRoot;
    private ScrollViewer? _wheelViewer;
    private PointerEventHandler? _wheelHandler;
    private double? _pendingWheelOffset; // requested by input; not yet sent to ChangeView
    private double? _issuedWheelOffset;  // sent; VerticalOffset may not reflect it until layout

    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
    private static partial int ReadWheelSetting(uint action, uint unused, out uint lines, uint flags);

    private void AttachTaskWheel(ItemsStackPanel panel)
    {
        DetachTaskWheel();
        DependencyObject? parent;
        for (DependencyObject? node = panel; node is not null; node = parent)
        {
            parent = VisualTreeHelper.GetParent(node);
            if (parent is ScrollViewer viewer && node is UIElement templateRoot)
            {
                // The viewer's template root also receives input over recycled card
                // gaps, where the content presenter alone does not see the wheel.
                _wheelViewer = viewer;
                _wheelInputRoot = templateRoot;
                _wheelHandler ??= OnTaskContentWheel;
                templateRoot.AddHandler(PointerWheelChangedEvent, _wheelHandler, true);
                viewer.ViewChanged += OnWheelViewChanged;
                return;
            }
        }
    }

    private void DetachTaskWheel()
    {
        CancelTaskWheel();
        if (_wheelHandler is not null) _wheelInputRoot?.RemoveHandler(PointerWheelChangedEvent, _wheelHandler);
        if (_wheelViewer is not null) _wheelViewer.ViewChanged -= OnWheelViewChanged;
        _wheelInputRoot = null;
        _wheelViewer = null;
    }

    private void CancelTaskWheel()
    {
        if (_pendingWheelOffset.HasValue) CompositionTarget.Rendering -= ApplyTaskWheel;
        _pendingWheelOffset = null;
        _issuedWheelOffset = null;
    }

    private void OnTaskWheelPointerPressed(object sender, PointerRoutedEventArgs args)
    {
        CancelTaskWheel(); // includes a scrollbar drag whose press was handled by the thumb
        if (!args.Handled) OnTaskPointerInteraction(sender, args);
    }

    private void OnWheelViewChanged(object? sender, ScrollViewerViewChangedEventArgs args)
    {
        if (_issuedWheelOffset is double target && _wheelViewer is { } viewer &&
            Math.Abs(viewer.VerticalOffset - Math.Min(target, viewer.ScrollableHeight)) < 0.5)
            _issuedWheelOffset = null;
    }

    private void OnTaskContentWheel(object sender, PointerRoutedEventArgs args)
    {
        if (args.Handled || _wheelViewer is not { ViewportHeight: > 0, ScrollableHeight: > 0 } viewer)
        {
            CancelTaskWheel();
            return;
        }
        var point = args.GetCurrentPoint((UIElement)sender).Properties;
        const int wheelDelta = 120;
        const uint getWheelScrollLines = 0x0068;
        int delta = point.MouseWheelDelta;
        // A final smaller packet can arrive before the frame carrying the burst.
        // Include its signed distance; dropping the accepted burst here loses input.
        bool joinsPendingBurst = _pendingWheelOffset.HasValue && delta != 0;
        if (args.Pointer.PointerDeviceType == PointerDeviceType.Mouse && (args.KeyModifiers != VirtualKeyModifiers.None || point.IsHorizontalMouseWheel))
        {
            // Settle the already-consumed vertical input before native tilt or zoom.
            if (_pendingWheelOffset.HasValue && IsTaskWheelSurface(args.OriginalSource as DependencyObject)) FlushTaskWheel();
            CancelTaskWheel();
            return;
        }
        if (args.Pointer.PointerDeviceType != PointerDeviceType.Mouse ||
            (!joinsPendingBurst && (Math.Abs((long)delta) < 2 * wheelDelta || delta % wheelDelta != 0)) ||
            !IsTaskWheelSurface(args.OriginalSource as DependencyObject) ||
            ReadWheelSetting(getWheelScrollLines, 0, out uint lines, 0) == 0)
        {
            // Let the native input own the viewport; a queued jump must not
            // override a user's ordinary wheel, horizontal gesture, or scrollbar.
            CancelTaskWheel();
            return;
        }
        if (lines == 0) { CancelTaskWheel(); args.Handled = true; return; }

        // Base distance from WinUI ScrollViewer's non-inertial wheel path at the
        // Windows default (three lines). Respect customized lines and page scrolling.
        double viewport = viewer.ViewportHeight;
        double normalNotch = Math.Max(48, Math.Round(viewport * 0.15));
        double perNotch = lines == uint.MaxValue ? viewport : Math.Min(viewport, normalNotch * lines / 3.0);
        double from = _pendingWheelOffset ?? _issuedWheelOffset ?? viewer.VerticalOffset;
        double target = Math.Clamp(from - delta / (double)wheelDelta * perNotch, 0, viewer.ScrollableHeight);
        if (target == from && !_pendingWheelOffset.HasValue && !_issuedWheelOffset.HasValue) return; // chaining
        OnTaskPointerInteraction(sender, args);
        args.Handled = true;
        if (target == from) return;
        if (!_pendingWheelOffset.HasValue) CompositionTarget.Rendering += ApplyTaskWheel;
        _pendingWheelOffset = target;
    }

    private bool IsTaskWheelSurface(DependencyObject? source)
    {
        for (var node = source; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (ReferenceEquals(node, _wheelInputRoot)) return true;
            if (node is ScrollBar or ScrollViewer) return false;
        }
        return false;
    }

    private void ApplyTaskWheel(object? sender, object args) => FlushTaskWheel();

    private void FlushTaskWheel()
    {
        CompositionTarget.Rendering -= ApplyTaskWheel;
        double? target = _pendingWheelOffset;
        _pendingWheelOffset = null;
        if (target is not double next || !IsLoaded || _wheelViewer is not { } viewer) return;
        next = Math.Clamp(next, 0, viewer.ScrollableHeight);
        _issuedWheelOffset = next; // ViewChanged may arrive inside or after ChangeView.
        bool accepted = viewer.ChangeView(null, next, null, disableAnimation: true);
        // Prepare the destination after the combined input and before presentation.
        if (accepted) viewer.UpdateLayout();
        if (!accepted || Math.Abs(viewer.VerticalOffset - next) < 0.5)
            _issuedWheelOffset = null;
    }
}
