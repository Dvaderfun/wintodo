using System.Runtime.CompilerServices;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Wintodo.ViewModels;

public enum TaskListGroupKind
{
    Hidden = 0,
    Named,
    Completed,
}

public sealed partial class TaskListGroupViewModel : ObservableObject
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
        Items = new TaskGroupCollection();
        Metadata.Add(Items, this);
    }

    internal TaskGroupCollection Items { get; }

    public TaskListGroupKind Kind { get; }

    [ObservableProperty]
    public partial string Title { get; set; }

    public string AutomationId { get; }

    [ObservableProperty]
    public partial string AccessibleName { get; set; }

    public bool IsCritical { get; }

    [ObservableProperty]
    public partial int HeaderCount { get; set; }

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

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
