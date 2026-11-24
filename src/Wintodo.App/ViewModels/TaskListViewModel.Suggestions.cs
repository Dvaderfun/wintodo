using System.Diagnostics;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wintodo.Core.Presentation;
using Wintodo.Core.Queries;
using Wintodo.Core.Services;
using Wintodo.Services;

namespace Wintodo.ViewModels;

public sealed partial record SuggestionRow(
    string Uid, string Title, string MetadataPrefix, string DueDate, bool IsOverdue,
    string Heading, string AddName, string CompleteName,
    ICommand AddCommand, ICommand CompleteCommand)
{
    public bool HasHeading => Heading.Length != 0;
    public string RegularDueDate => IsOverdue ? string.Empty : DueDate;
    public string OverdueDueDate => IsOverdue ? DueDate : string.Empty;
    public string AddId => $"Todo.Suggestions.Add.{Uid}";
    public string CompleteId => $"Todo.Suggestions.Complete.{Uid}";
}

public sealed partial class TaskListViewModel
{
    private int _suggestionsVersion;
    private int _suggestionsSession;
    internal event Action? SuggestionsOpened;

    [ObservableProperty]
    public partial bool IsSuggestionsOpen { get; private set; }

    public TaskGroupCollection Suggestions { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSuggestionStatus))]
    public partial string SuggestionStatus { get; private set; } = string.Empty;

    public bool HasSuggestionStatus => SuggestionStatus.Length != 0;

    [RelayCommand]
    private void ToggleSuggestions() => IsSuggestionsOpen = HasSubtitle && !IsSuggestionsOpen;

    partial void OnIsSuggestionsOpenChanged(bool value)
    {
        if (value)
        {
            SuggestionsOpened?.Invoke();
            SuggestionStatus = _resources.GetString("Suggestions_Loading");
            _ = RefreshSuggestionsAsync();
        }
        else
        {
            ++_suggestionsSession;
            ++_suggestionsVersion;
            Suggestions.Clear();
        }
    }

    internal void CloseSuggestions() => IsSuggestionsOpen = false;

    private async Task RefreshSuggestionsAsync()
    {
        if (!IsSuggestionsOpen || _currentQuery?.Kind != ListQueryKind.MyDay) return;
        int version = ++_suggestionsVersion;
        try
        {
            var tasks = await _taskStore.GetTasksAsync(new ListQuery
            {
                Kind = ListQueryKind.All,
                IncludeCompleted = false,
            });
            if (version != _suggestionsVersion || _currentQuery?.Kind != ListQueryKind.MyDay) return;

            SuggestionSection? previous = null;
            var rows = new List<SuggestionRow>();
            foreach (MyDaySuggestion suggestion in MyDaySuggestions.Build(tasks, _clock))
            {
                var task = suggestion.Task;
                string list = FindListName(task.ListId) ?? string.Empty;
                string due = suggestion.DueDate?.ToString("d", DisplayCulture.Current) ?? string.Empty;
                string metadataPrefix = list.Length != 0 && due.Length != 0 ? $"{list} · " : list;
                string heading = previous == suggestion.Section ? string.Empty
                    : _resources.GetString($"Suggestions_{suggestion.Section}");
                rows.Add(new SuggestionRow(task.Uid, task.Summary, metadataPrefix, due,
                    suggestion.Section == SuggestionSection.Overdue, heading,
                    SuggestionActionName("Suggestions_AddTask", task.Summary),
                    SuggestionActionName("Suggestions_CompleteTask", task.Summary),
                    AddSuggestionCommand, CompleteSuggestionCommand));
                previous = suggestion.Section;
            }

            ApplySequence(Suggestions, rows.Cast<object>().ToArray());
            SuggestionStatus = rows.Count == 0 ? _resources.GetString("Suggestions_Empty") : string.Empty;
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception.GetType().Name);
            if (version == _suggestionsVersion)
                SuggestionStatus = _resources.GetString("Suggestions_LoadFailed");
        }
    }

    private string SuggestionActionName(string key, string title) =>
        string.Format(DisplayCulture.Current, _resources.GetString(key), title);

    private bool CanApplySuggestion(string? uid) =>
        IsSuggestionsOpen && !_applyingSuggestion && _currentQuery?.Kind == ListQueryKind.MyDay
            && !string.IsNullOrEmpty(uid);

    [RelayCommand(CanExecute = nameof(CanApplySuggestion))]
    private Task AddSuggestionAsync(string? uid) => ApplySuggestionAsync(uid, TaskBatchAction.AddToMyDay);

    [RelayCommand(CanExecute = nameof(CanApplySuggestion))]
    private Task CompleteSuggestionAsync(string? uid) => ApplySuggestionAsync(uid, TaskBatchAction.Complete);

    private bool _applyingSuggestion;

    private async Task ApplySuggestionAsync(string? uid, TaskBatchAction action)
    {
        if (!CanApplySuggestion(uid)) return;
        int session = _suggestionsSession;
        _applyingSuggestion = true;
        NotifySuggestionCommands();
        try
        {
            string[] uids = [uid!];
            var states = await _taskStore.CaptureTaskUndoAsync(uids, UndoFields(action));
            if (session != _suggestionsSession || !IsSuggestionsOpen
                || _currentQuery?.Kind != ListQueryKind.MyDay) return;
            if (states.Count == 0)
            {
                await RefreshSuggestionsAsync();
                return;
            }
            await _taskStore.ApplyTaskBatchAsync(uids, action, expectedSuggestion: states[0].Task);
            RecordTaskUndo(states, action == TaskBatchAction.Complete ? "Undo_TaskCompleted" : "Undo_TasksChanged");
            if (action == TaskBatchAction.Complete)
            {
                var updated = await _taskStore.GetTaskAsync(uid!);
                if (updated is not null)
                    RowCompletionSaved?.Invoke(updated, updated.Status != Core.Models.TaskStatus.Completed);
            }
            else if (TaskDatesChanged is not null) await TaskDatesChanged(uids);
            await ReloadAsync();
            NavigationDataChanged?.Invoke();
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception.GetType().Name);
            await RefreshSuggestionsAsync();
            if (IsSuggestionsOpen && session == _suggestionsSession)
                SuggestionStatus = _resources.GetString("Suggestions_ActionFailed");
        }
        finally
        {
            _applyingSuggestion = false;
            NotifySuggestionCommands();
        }
    }

    private void NotifySuggestionCommands()
    {
        AddSuggestionCommand.NotifyCanExecuteChanged();
        CompleteSuggestionCommand.NotifyCanExecuteChanged();
    }
}
