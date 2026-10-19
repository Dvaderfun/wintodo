using CommunityToolkit.Mvvm.ComponentModel;

namespace Wintodo.ViewModels;

/// <summary>One Microsoft To Do list in the wizard's "choose" step.</summary>
public sealed partial class ImportListOptionViewModel : ObservableObject
{
    internal ImportListOptionViewModel(string externalId, string label, string detail, int index, bool isSelected)
    {
        ExternalId = externalId;
        Label = label;
        Detail = detail;
        AutomationId = "Todo.Import.List." + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
        IsSelected = isSelected;
    }

    internal event Action? SelectionChanged;

    internal string ExternalId { get; }

    public string Label { get; }

    public string Detail { get; }

    public string AutomationId { get; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    partial void OnIsSelectedChanged(bool value)
    {
        SelectionChanged?.Invoke();
    }
}
