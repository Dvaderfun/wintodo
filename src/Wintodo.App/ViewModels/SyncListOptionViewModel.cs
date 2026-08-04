using CommunityToolkit.Mvvm.ComponentModel;
using Wintodo.Core.Models;

namespace Wintodo.ViewModels;

public sealed partial class SyncListOptionViewModel : ObservableObject
{
    private readonly Func<SyncListOptionViewModel, bool, Task> _changed;
    private bool _suppressChanged;

    internal SyncListOptionViewModel(
        TodoList list,
        Func<SyncListOptionViewModel, bool, Task> changed)
    {
        List = list;
        DisplayName = list.DisplayName;
        _changed = changed;
        _suppressChanged = true;
        IsEnabled = list.IsSyncEnabled;
        _suppressChanged = false;
    }

    internal TodoList List { get; }

    public string DisplayName { get; }

    public string AutomationId => $"Todo.Settings.Sync.List.{List.Uid}";

    [ObservableProperty]
    public partial bool IsEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsAvailable { get; set; } = true;

    internal void SetWithoutCallback(bool value)
    {
        _suppressChanged = true;
        IsEnabled = value;
        _suppressChanged = false;
    }

    partial void OnIsEnabledChanged(bool value)
    {
        if (!_suppressChanged)
        {
            IsAvailable = false;
            _ = ApplyChangeAsync(value);
        }
    }

    private async Task ApplyChangeAsync(bool value)
    {
        try
        {
            await _changed(this, value);
        }
        finally
        {
            IsAvailable = true;
        }
    }
}
