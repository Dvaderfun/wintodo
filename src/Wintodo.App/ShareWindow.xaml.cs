using Microsoft.UI.Xaml;
using Microsoft.Windows.ApplicationModel.Resources;
using Wintodo.ViewModels;

namespace Wintodo;

public sealed partial class ShareWindow : Window
{
    public ShareViewModel ViewModel { get; }

    internal ShareWindow()
    {
        ViewModel = new ShareViewModel(Close);
        InitializeComponent();
        Title = new ResourceLoader().GetString("AppDisplayName");
        nint hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        double scale = GetDpiForWindow(hwnd) / 96d;
        AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(480 * scale), (int)(320 * scale)));
        AppWindow.Closing += (_, args) => args.Cancel = ViewModel.IsBusy;
        Closed += (_, _) => Application.Current.Exit();
    }

    internal void ShowError(string message)
    {
        ViewModel.ErrorMessage = message;
        ViewModel.IsBusy = false;
        VisualStateManager.GoToState(Root, "Failed", false);
    }

    internal void Complete()
    {
        ViewModel.IsBusy = false;
        Close();
    }

    [System.Runtime.InteropServices.LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(nint hwnd);
}
