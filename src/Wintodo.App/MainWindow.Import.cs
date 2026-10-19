using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.Windows.ApplicationModel.Resources;
using System.ComponentModel;

namespace Wintodo;

public sealed partial class MainWindow
{
    private bool _closingImport;

    private async void OnImportWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        var wizard = _shellPage?.ViewModel.ImportWizard;
        if (wizard?.IsBusy != true) return;
        args.Cancel = true;
        if (_closingImport) return;
        _closingImport = true;
        try
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
            if (!wizard.IsSaving) wizard.CancelCommand.Execute(null);
            if (wizard.IsBusy)
            {
                var complete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                void Changed(object? source, PropertyChangedEventArgs change)
                {
                    if (change.PropertyName == nameof(wizard.IsBusy) && !wizard.IsBusy) complete.TrySetResult();
                }
                wizard.PropertyChanged += Changed;
                try
                {
                    if (!wizard.IsBusy) complete.TrySetResult();
                    await complete.Task;
                }
                finally { wizard.PropertyChanged -= Changed; }
            }
            Close();
        }
        finally { _closingImport = false; }
    }
}
