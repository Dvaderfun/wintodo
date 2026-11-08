using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wintodo.Import.MicrosoftTodo;
using Wintodo.Services;

namespace Wintodo.ViewModels;

public sealed partial class ImportWizardViewModel
{
    private Exception? _lastFailure;
    private string? _workTenantId;

    [ObservableProperty]
    public partial string ErrorActionText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ResumeMessage { get; set; } = string.Empty;

    [RelayCommand]
    private async Task ErrorActionAsync()
    {
        if (_session.IsDismissed || IsBusy || !IsErrorOpen) return;
        if (_lastFailure is GraphAuthorizationException { IsSignInExpired: false } && _workTenantId is not null)
        {
            // VERIFY with the owner's work/school gate; no tenant claim is inferred from email.
            CopyDetailsRequested?.Invoke("https://login.microsoftonline.com/" + _workTenantId + "/v2.0/adminconsent?client_id="
                + MicrosoftTodoRegistration.ClientId + "&scope=https%3A%2F%2Fgraph.microsoft.com%2F.default&redirect_uri=http%3A%2F%2Flocalhost");
            return;
        }
        if (IsConnectStep) await SignInCommand.ExecuteAsync(null);
        else if (IsReviewStep) await ImportCommand.ExecuteAsync(null);
        else if (IsChooseStep) await ContinueCommand.ExecuteAsync(null);
    }
}
