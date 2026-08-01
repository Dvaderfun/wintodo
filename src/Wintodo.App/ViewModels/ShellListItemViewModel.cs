using CommunityToolkit.Mvvm.ComponentModel;
using Wintodo.Core.Models;

namespace Wintodo.ViewModels;

public sealed partial class ShellListItemViewModel : ObservableObject
{
    internal ShellListItemViewModel(TodoList list)
    {
        Id = list.Id;
        Uid = list.Uid;
        DisplayName = list.DisplayName;
        Color = list.Color;
        BackgroundTheme = list.BackgroundTheme;
        GroupId = list.GroupId;
        SortOrder = list.SortOrder;
        SortMode = list.SortMode;
        SortDescending = list.SortDescending;
        ShowCompleted = list.ShowCompleted;
        CompletedExpanded = list.CompletedExpanded;
        IsDefault = list.IsDefault;
    }

    public long Id { get; }

    public bool IsDefault { get; }

    public string Uid { get; }

    [ObservableProperty]
    public partial string DisplayName { get; set; }

    [ObservableProperty]
    public partial ListColor Color { get; set; }

    [ObservableProperty]
    public partial string? BackgroundTheme { get; set; }

    [ObservableProperty]
    public partial long? GroupId { get; set; }

    [ObservableProperty]
    public partial double SortOrder { get; set; }

    [ObservableProperty]
    public partial SortMode SortMode { get; set; }

    [ObservableProperty]
    public partial bool SortDescending { get; set; }

    [ObservableProperty]
    public partial bool ShowCompleted { get; set; }

    [ObservableProperty]
    public partial bool CompletedExpanded { get; set; }

    public string NavigationKey => $"list:{Uid}";

    public string AutomationId => $"Todo.Nav.UserList.{Uid}";

    public string AccentResourceKey => $"ListAccent{Color}Brush";

    partial void OnColorChanged(ListColor value)
    {
        OnPropertyChanged(nameof(AccentResourceKey));
    }

    public override string ToString()
    {
        return DisplayName;
    }
}
