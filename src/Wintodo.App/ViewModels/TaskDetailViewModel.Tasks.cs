using CommunityToolkit.Mvvm.Input;
using Wintodo.Core.Models;

namespace Wintodo.ViewModels;

public sealed partial class TaskDetailViewModel
{
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
        if (_model is null)
        {
            return;
        }

        _model.MyDayDate = IsMyDay ? null : _clock.Today;
        IsMyDay = _model.MyDayDate is not null;
        UpdateActionLabels();
        await PersistModelAsync();
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
