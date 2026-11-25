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
        // A rejected database only shows its error dialog. Never write settings to it.
        if (!_hasPresentedShell || _closeApproved) return;
        args.Cancel = true;
        if (_closePending) return;
        _closePending = true;
        try
        {
            // A close during initial hydration must not dispose the store under startup.
            await _startupFinished.Task;
            var viewModel = Shell.ViewModel;
            var wizard = viewModel.ImportWizard;
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
            try
            {
                // Read the live presenter and commit its whole placement before destroying the HWND.
                await _stateService.SaveWindowPlacementAsync(GetLivePlacement());
            }
            catch (Exception exception)
            {
                // A failed preference write must not trap the user in the app.
                System.Diagnostics.Trace.WriteLine($"WINTODO_PLACEMENT_SAVE_EXCEPTION={DiagnosticException.Describe(exception)}");
            }
            do
            {
                await viewModel.FlushDetailPaneWidthAsync();
                await viewModel.AppearancePreferenceWrites;
                if (!await viewModel.Detail.FlushPendingEditsAsync()) return;
            } while (viewModel.HasPendingDetailPaneWidth || !viewModel.AppearancePreferenceWrites.IsCompleted);
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
