using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.Input;

namespace Wintodo.ViewModels;

public enum TaskListGroupKind
{
    Hidden = 0,
    Named,
    Completed,
}

public sealed partial class TaskListGroupViewModel
{
    private static readonly ConditionalWeakTable<object, TaskListGroupViewModel>
        Metadata = new();
    private readonly Func<Task>? _toggle;

    internal TaskListGroupViewModel(
        TaskListGroupKind kind,
        string title,
        string automationId,
        bool isCritical = false,
        Func<Task>? toggle = null,
        string? accessibleName = null)
    {
        Kind = kind;
        Title = title;
        AutomationId = automationId;
        IsCritical = isCritical;
        AccessibleName = accessibleName ?? title;
        _toggle = toggle;
        Items = new List<object>();
        Metadata.Add(Items, this);
    }

    internal IList<object> Items { get; }

    public TaskListGroupKind Kind { get; }

    public string Title { get; }

    public string AutomationId { get; }

    public string AccessibleName { get; }

    public bool IsCritical { get; }

    public int HeaderCount => CountOverride ?? Items.Count;

    internal int? CountOverride { get; set; }

    public bool IsExpanded { get; set; }

    internal static TaskListGroupViewModel GetMetadata(object items)
    {
        return Metadata.TryGetValue(items, out TaskListGroupViewModel? group)
            ? group
            : throw new ArgumentException("Unknown task group collection.", nameof(items));
    }

    [RelayCommand]
    private Task ToggleAsync()
    {
        return _toggle?.Invoke() ?? Task.CompletedTask;
    }
}
