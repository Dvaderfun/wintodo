using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Wintodo.ViewModels;

public sealed partial class ShareViewModel(Action close) : ObservableObject
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CloseCommand))]
    public partial bool IsBusy { get; set; } = true;

    [ObservableProperty]
    public partial string ErrorMessage { get; set; } = string.Empty;

    private bool CanClose() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanClose))]
    private void Close() => close();
}
