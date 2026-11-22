using CommunityToolkit.Mvvm.Input;
using Wintodo.Core.Models;
using Wintodo.Core.Presentation;
using Wintodo.Core.Services;

namespace Wintodo.ViewModels;

public sealed partial class TaskDetailViewModel
{
    internal event Func<TodoTask, Task>? MyDayMembershipChanged;

    internal async Task PersistStepOrderAsync(CancellationToken cancellationToken = default)
    {
        for (int index = 0; index < Steps.Count; index++)
        {
            var step = (StepItemViewModel)Steps[index];
            double sortOrder = (index + 1) * 1024d;
            step.Model.SortOrder = sortOrder;
            await _taskStore.ReorderStepAsync(step.Uid, sortOrder, cancellationToken);
        }
    }

    internal void ReportError(string message)
    {
        ShowError(message);
    }

    [RelayCommand]
    private async Task CloseAsync()
    {
        if (!await FlushPendingEditsAsync()) return;
        CloseRequested?.Invoke();
    }

    [RelayCommand]
    private async Task ToggleCompletionAsync()
    {
        if (_model is not { } owner || !await FlushPendingEditsAsync()) return;

        int editRevision = _textSaves.Revision;
        if (owner.Status == Core.Models.TaskStatus.Completed)
        {
            await _taskStore.UncompleteTaskAsync(owner.Uid);
        }
        else
        {
            await _taskStore.CompleteTaskAsync(owner.Uid);
        }

        TodoTask updated = await _taskStore.GetTaskAsync(owner.Uid)
            ?? throw new InvalidOperationException("The edited task no longer exists.");
        if (!ReferenceEquals(_model, owner))
        {
            TaskChanged?.Invoke(CloneTask(updated));
            return;
        }
        if (editRevision != _textSaves.Revision)
        {
            updated.Summary = owner.Summary;
            updated.Description = owner.Description;
        }
        _model = updated;
        ApplyModelToEditor(updated);
        OnPropertyChanged(nameof(IsCompleted));
        RebuildAttachments();
        TaskChanged?.Invoke(CloneTask(updated));
    }

    [RelayCommand]
    private async Task ToggleImportanceAsync()
    {
        if (_model is null)
        {
            return;
        }

        _model.Priority = IsImportant
            ? (int)TaskPriority.Undefined
            : (int)TaskPriority.High;
        IsImportant = !IsImportant;
        await PersistModelAsync();
    }

    [RelayCommand]
    private async Task ToggleMyDayAsync()
    {
        TodoTask? current = _model;
        if (current is null)
        {
            return;
        }

        try
        {
            await FlushUndoEditorAsync();
            var states = await _taskStore.CaptureTaskUndoAsync([current.Uid], TaskUndoFields.MyDay);
            bool remove = TaskDateLabels.IsInMyDay(states[0].Task, _clock);
            await _taskStore.ApplyTaskBatchAsync([current.Uid],
                remove ? TaskBatchAction.RemoveFromMyDay : TaskBatchAction.AddToMyDay);
            RecordUndo(states, "Undo_TasksChanged");
            if (await _taskStore.GetTaskAsync(current.Uid) is { } updated)
            {
                if (_model is { } editor && editor.Uid == updated.Uid) ApplyTaskDates(editor, updated);
                if (MyDayMembershipChanged is not null) await MyDayMembershipChanged(updated);
            }
        }
        catch (Exception exception)
        {
            System.Diagnostics.Trace.WriteLine($"WINTODO_MY_DAY_EXCEPTION={DiagnosticException.Describe(exception)}");
            ShowError(_resources.GetString("Detail_SaveFailed"));
        }
    }

    internal async Task RefreshTaskDatesAsync(IReadOnlyList<string> uids)
    {
        TodoTask? current = _model;
        if (current is null || !uids.Contains(current.Uid)) return;
        TodoTask? updated = await _taskStore.GetTaskAsync(current.Uid);
        if (updated is not null && ReferenceEquals(_model, current)) ApplyTaskDates(current, updated);
    }

    internal void ApplyRowCompletion(TodoTask updated, bool occurrenceAdvanced)
    {
        if (_model is not { } current || current.Uid != updated.Uid) return;
        current.Status = updated.Status;
        current.PercentComplete = updated.PercentComplete;
        current.CompletedAt = updated.CompletedAt;
        IsCompleted = current.Status == Core.Models.TaskStatus.Completed;
        if (!occurrenceAdvanced) return;

        current.Start = updated.Start;
        current.Alarms.Clear();
        current.Alarms.AddRange(updated.Alarms);
        RefreshPendingReminder(current);
        ApplyTaskDates(current, updated);
    }

    private void ApplyTaskDates(TodoTask current, TodoTask updated)
    {
        if (current.Due != updated.Due || current.DueIsDateOnly != updated.DueIsDateOnly
            || current.DueTzid != updated.DueTzid)
        {
            current.Due = updated.Due;
            current.DueIsDateOnly = updated.DueIsDateOnly;
            current.DueTzid = updated.DueTzid;
            RefreshPendingDueDate(current);
        }
        current.MyDayDate = updated.MyDayDate;
        current.MyDayExcludedDate = updated.MyDayExcludedDate;
        UpdateActionLabels();
    }

    private bool CanAddStep()
    {
        return _model is not null && !string.IsNullOrWhiteSpace(NewStepTitle);
    }

    [RelayCommand(CanExecute = nameof(CanAddStep))]
    private async Task AddStepAsync()
    {
        if (_model is null || string.IsNullOrWhiteSpace(NewStepTitle))
        {
            return;
        }

        TodoTask step = await _taskStore.AddStepAsync(_model.Uid, NewStepTitle.Trim());
        Steps.Add(CreateStepViewModel(step));
        NewStepTitle = string.Empty;
        TaskChanged?.Invoke(CloneTask(_model));
    }

    [RelayCommand]
    private void RequestAddAttachment()
    {
        AddAttachmentRequested?.Invoke();
    }

    [RelayCommand]
    private async Task DeleteTaskAsync()
    {
        await Task.Yield();
        if (_model is not null)
        {
            DeleteRequested?.Invoke(Summary);
        }
    }

    internal async Task ConfirmDeleteAsync(
        CancellationToken cancellationToken = default)
    {
        if (_model is null)
        {
            return;
        }

        string uid = _model.Uid;
        if (!await FlushPendingEditsAsync()) return;
        var states = await _taskStore.CaptureTaskUndoAsync([uid], Core.Services.TaskUndoFields.Deleted, cancellationToken);
        await _taskStore.DeleteTaskAsync(uid, cancellationToken);
        RecordUndo(states, "Undo_TaskDeleted");
        TaskDeleted?.Invoke(uid);
        CloseRequested?.Invoke();
    }

    [RelayCommand]
    private void ClearError()
    {
        IsErrorOpen = false;
    }

    private async Task RenameStepAsync(StepItemViewModel item, string summary)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(300));
        if (string.IsNullOrWhiteSpace(summary)
            || !string.Equals(item.Summary, summary, StringComparison.Ordinal))
        {
            return;
        }

        item.Model.Summary = summary.Trim();
        await _taskStore.UpdateTaskAsync(item.Model);
    }

    private async Task ToggleStepCompletionAsync(StepItemViewModel item)
    {
        if (item.IsCompleted)
        {
            await _taskStore.UncompleteTaskAsync(item.Uid);
        }
        else
        {
            await _taskStore.CompleteTaskAsync(item.Uid);
        }

        TodoTask updated = await _taskStore.GetTaskAsync(item.Uid)
            ?? throw new InvalidOperationException("The edited step no longer exists.");
        item.Update(updated);
        TaskChanged?.Invoke(CloneTask(_model!));
    }

    private async Task DeleteStepAsync(StepItemViewModel item)
    {
        var states = await _taskStore.CaptureTaskUndoAsync([item.Uid], Core.Services.TaskUndoFields.Deleted);
        await _taskStore.DeleteTaskAsync(item.Uid);
        RecordUndo(states, "Undo_StepDeleted");
        Steps.Remove(item);
        TaskChanged?.Invoke(CloneTask(_model!));
    }

    private StepItemViewModel CreateStepViewModel(TodoTask step)
    {
        return new StepItemViewModel(
            step,
            RenameStepAsync,
            ToggleStepCompletionAsync,
            DeleteStepAsync);
    }

}
