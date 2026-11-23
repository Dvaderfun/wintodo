using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.Windows.ApplicationModel.Resources;
using Wintodo.Core.Services;

namespace Wintodo;

public sealed partial class MainWindow
{
    private bool _closePending;
    private bool _closeApproved;

    private async void OnWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        var viewModel = _shellPage?.ViewModel;
        if (viewModel is null || _closeApproved) return;
        var wizard = viewModel.ImportWizard;
        if (!_closePending && wizard is null && !viewModel.Detail.HasPendingEdits) return;
        args.Cancel = true;
        if (_closePending) return;
        _closePending = true;
        try
        {
            if (wizard?.IsBusy == true)
            {
                var resources = new ResourceLoader();
                var dialog = new ContentDialog
                {
                    XamlRoot = WindowRoot.XamlRoot,
                    Title = resources.GetString("Import_CloseTitle"),
                    Content = resources.GetString("Import_CloseMessage"),
                    PrimaryButtonText = resources.GetString("Import_ClosePrimary"),
                    CloseButtonText = resources.GetString("Import_RemoveConfirmCancel"),
                    DefaultButton = ContentDialogButton.Close,
                };
                AutomationProperties.SetAutomationId(dialog, "Todo.Import.CloseConfirm");
                if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            }
            if (wizard is not null) await viewModel.ShutdownImportAsync();
            // Unwind the original Closing event before calling Close ourselves.
            await Task.Yield();
            if (!await viewModel.Detail.FlushPendingEditsAsync()) return;
            _closeApproved = true;
            Close();
        }
        catch (Exception exception)
        {
            _closeApproved = false;
            System.Diagnostics.Trace.WriteLine($"WINTODO_WINDOW_CLOSE_EXCEPTION={DiagnosticException.Describe(exception)}");
        }
        finally { _closePending = false; }
    }
}
