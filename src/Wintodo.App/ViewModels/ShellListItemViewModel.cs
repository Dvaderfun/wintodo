namespace Wintodo.ViewModels;

public sealed class ShellListItemViewModel
{
    public required string Uid { get; init; }

    public required string DisplayName { get; init; }

    public override string ToString()
    {
        return DisplayName;
    }
}
