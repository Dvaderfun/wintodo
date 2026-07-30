using System.Collections.ObjectModel;
using Wintodo.Core.Models;

namespace Wintodo.ViewModels;

public sealed class ShellGroupViewModel
{
    internal ShellGroupViewModel(ListGroup group)
    {
        Id = group.Id;
        Name = group.Name;
        SortOrder = group.SortOrder;
        IsExpanded = group.IsExpanded;
    }

    public long Id { get; }

    public string Name { get; }

    public double SortOrder { get; }

    public bool IsExpanded { get; }

    public string AutomationId => $"Todo.Nav.Group.{Id}";

    public ObservableCollection<ShellListItemViewModel> Lists { get; } = [];
}
