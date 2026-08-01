namespace Wintodo.ViewModels;

public sealed class ShortcutEntryViewModel
{
    internal ShortcutEntryViewModel(string shortcut, string action)
    {
        Shortcut = shortcut;
        Action = action;
    }

    public string Shortcut { get; }

    public string Action { get; }
}
