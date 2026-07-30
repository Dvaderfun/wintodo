using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Wintodo.ViewModels;

public enum TaskListGroupKind
{
    Hidden = 0,
    Named,
    Completed,
}

public sealed partial class TaskListGroupViewModel : ObservableCollection<TaskItemViewModel>
{
    private readonly Func<Task>? _toggle;
    private bool _isExpanded;

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
    }

    public TaskListGroupKind Kind { get; }

    public string Title { get; }

    public string AutomationId { get; }

    public string AccessibleName { get; }

    public bool IsCritical { get; }

    public int HeaderCount => CountOverride ?? Count;

    internal int? CountOverride { get; set; }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value)
            {
                return;
            }

            _isExpanded = value;
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(IsExpanded)));
        }
    }

    [RelayCommand]
    private Task ToggleAsync()
    {
        return _toggle?.Invoke() ?? Task.CompletedTask;
    }
}
