using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Wintodo.Controls;

public sealed partial class ResponsiveTitleSearch : UserControl
{
    private const double WideMinWindowWidth = 900;
    private static readonly TimeSpan SearchDebounce =
        TimeSpan.FromMilliseconds(120);

    private CancellationTokenSource? _debounceCancellation;
    private bool _synchronizingText;

    public ResponsiveTitleSearch()
    {
        InitializeComponent();
        SearchBox.AddHandler(
            KeyDownEvent,
            new KeyEventHandler(OnSearchBoxKeyDown),
            handledEventsToo: true);
        CompactSearchBox.AddHandler(
            KeyDownEvent,
            new KeyEventHandler(OnSearchBoxKeyDown),
            handledEventsToo: true);
    }

    internal event Action<string>? SearchRequested;

    internal bool HasQuery => !string.IsNullOrEmpty(SearchBox.Text);

    internal void UpdateWindowWidth(double width)
    {
        string stateName = width >= WideMinWindowWidth
            ? nameof(Wide)
            : nameof(Compact);
        VisualStateManager.GoToState(this, stateName, false);
    }

    internal void FocusSearch()
    {
        if (SearchBox.Visibility == Visibility.Visible)
        {
            SearchBox.Focus(FocusState.Programmatic);
            return;
        }

        CompactSearchFlyout.ShowAt(SearchButton);
    }

    internal bool TryClear()
    {
        if (!HasQuery)
        {
            return false;
        }

        _debounceCancellation?.Cancel();
        SetSearchText(string.Empty);
        SearchRequested?.Invoke(string.Empty);
        return true;
    }

    private async void OnSearchTextChanged(
        AutoSuggestBox sender,
        AutoSuggestBoxTextChangedEventArgs args)
    {
        if (_synchronizingText
            || args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
        {
            return;
        }

        SetSearchText(sender.Text);
        _debounceCancellation?.Cancel();
        _debounceCancellation?.Dispose();
        _debounceCancellation = new CancellationTokenSource();
        try
        {
            await Task.Delay(SearchDebounce, _debounceCancellation.Token);
            SearchRequested?.Invoke(sender.Text.Trim());
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void SetSearchText(string text)
    {
        _synchronizingText = true;
        SearchBox.Text = text;
        CompactSearchBox.Text = text;
        _synchronizingText = false;
    }

    private void OnCompactSearchFlyoutOpened(object sender, object args)
    {
        CompactSearchBox.Focus(FocusState.Programmatic);
    }

    private void OnSearchBoxKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key == VirtualKey.Escape && TryClear())
        {
            args.Handled = true;
        }
    }
}
