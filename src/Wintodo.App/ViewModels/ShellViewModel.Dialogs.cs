using CommunityToolkit.Mvvm.Input;
using Wintodo.Core.Models;
using Wintodo.Services;

namespace Wintodo.ViewModels;

public sealed partial class ShellViewModel
{
    internal void SetTransferBusy(bool value)
    {
        if (value)
        {
            IsTransferStatusOpen = false;
        }

        IsTransferBusy = value;
        TaskList.IsTransferBusy = value;
    }

    internal void ShowTransferStatus(
        string title,
        string message,
        bool isError)
    {
        TransferStatusTitle = title;
        TransferStatusMessage = message;
        TransferStatusIsError = isError;
        IsTransferStatusOpen = true;
    }

    internal async Task OpenDetailAsync(
        TaskItemViewModel task,
        CancellationToken cancellationToken = default)
    {
        int version = ++_detailOpenVersion;
        await Detail.LoadAsync(task.Uid, cancellationToken);
        if (version != _detailOpenVersion || TaskList.SelectionCount != 1) return;
        IsDetailPaneLoaded = true;
        IsDetailPaneOpen = true;
    }

    internal void CloseDetail()
    {
        ++_detailOpenVersion;
        IsDetailPaneOpen = false;
    }

    private bool CanTransferData()
    {
        return !IsTransferBusy;
    }

    [RelayCommand(CanExecute = nameof(CanTransferData))]
    private void ImportIcal()
    {
        ImportIcalRequested?.Invoke();
    }

    [RelayCommand(CanExecute = nameof(CanTransferData))]
    private void ExportAll()
    {
        ExportAllRequested?.Invoke();
    }

    private void OnShortcutsRequested()
    {
        KeyboardShortcutsRequested?.Invoke();
    }

    private void OnCustomListIconRequested(string? iconEmoji)
    {
        CustomListIconInput = iconEmoji ?? string.Empty;
        CustomListIconError = string.Empty;
        IsListIconDialogLoaded = true;
        CustomListIconRequested?.Invoke();
    }

    internal bool ValidateCustomListIconInput()
    {
        if (!ListEmoji.TryNormalize(CustomListIconInput, out _))
        {
            CustomListIconError = _resources.GetString("ListIcon_Invalid");
            return false;
        }

        CustomListIconError = string.Empty;
        return true;
    }

    internal async Task CommitCustomListIconAsync()
    {
        if (!ListEmoji.TryNormalize(CustomListIconInput, out string? normalized))
        {
            return;
        }

        await TaskList.SetSelectedListIconAsync(normalized);
    }

    private void OnRowDeleteRequested(TaskItemViewModel item)
    {
        _pendingBulkDelete = null;
        _pendingRowDelete = item;
        _isDetailDeletePending = false;
        DeleteTaskRequested?.Invoke(item.Summary);
    }

    private void OnRowsDeleteRequested(IReadOnlyList<TaskItemViewModel> items)
    {
        if (items.Count == 1) { OnRowDeleteRequested(items[0]); return; }
        _pendingRowDelete = null;
        _isDetailDeletePending = false;
        _pendingBulkDelete = items.ToArray();
        DeleteTaskRequested?.Invoke(string.Empty);
    }

    internal int PendingDeleteCount => _pendingBulkDelete?.Count ?? 1;

    private void OnDetailDeleteRequested(string summary)
    {
        _pendingBulkDelete = null;
        _pendingRowDelete = null;
        _isDetailDeletePending = true;
        DeleteTaskRequested?.Invoke(summary);
    }

    internal async Task ConfirmDeleteTaskAsync(
        CancellationToken cancellationToken = default)
    {
        TaskItemViewModel? row = _pendingRowDelete;
        IReadOnlyList<TaskItemViewModel>? bulk = _pendingBulkDelete;
        bool isDetail = _isDetailDeletePending;
        CancelDeleteTask();
        if (bulk is not null)
        {
            await TaskList.ConfirmDeleteTasksAsync(bulk, cancellationToken);
            return;
        }
        if (row is not null)
        {
            await TaskList.ConfirmDeleteTaskAsync(row, cancellationToken);
            return;
        }

        if (isDetail)
        {
            await Detail.ConfirmDeleteAsync(cancellationToken);
        }
    }

    internal void CancelDeleteTask()
    {
        _pendingBulkDelete = null;
        _pendingRowDelete = null;
        _isDetailDeletePending = false;
    }

}
