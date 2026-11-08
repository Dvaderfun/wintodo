using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.Windows.ApplicationModel.Resources;
using Wintodo.Core.Services;

namespace Wintodo;

public sealed partial class MainWindow
{
    private bool _closingImport;
    private bool _importCloseApproved;

    private async void OnImportWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        var wizard = _shellPage?.ViewModel.ImportWizard;
        if (wizard is null || _importCloseApproved) return;
        args.Cancel = true;
        if (_closingImport) return;
        _closingImport = true;
        try
        {
            if (wizard.IsBusy)
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
            await _shellPage!.ViewModel.ShutdownImportAsync();
            _importCloseApproved = true;
            DispatcherQueue.TryEnqueue(Close);
        }
        catch (Exception exception)
        {
            System.Diagnostics.Trace.WriteLine($"WINTODO_IMPORT_CLOSE_EXCEPTION={DiagnosticException.Describe(exception)}");
        }
        finally { _closingImport = false; }
    }
}
