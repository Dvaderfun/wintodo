using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Diagnostics;
using Wintodo.Core.Models;
using Wintodo.Core.Services;
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

    private CancellationTokenSource? _detailRead;
    private string? _openingUid;
    private Task<bool>? _openingDetail;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDetailPreview))]
    [NotifyPropertyChangedFor(nameof(DetailPreviewTitle))]
    [NotifyPropertyChangedFor(nameof(IsPreviewCompleted))]
    [NotifyPropertyChangedFor(nameof(IsPreviewImportant))]
    public partial TaskItemViewModel? DetailPreview { get; private set; }

    public bool HasDetailPreview => DetailPreview is not null;
    public string DetailPreviewTitle => DetailPreview?.Summary ?? string.Empty;
    public bool IsPreviewCompleted => DetailPreview?.IsCompleted == true;
    public bool IsPreviewImportant => DetailPreview?.IsImportant == true;
    public string DetailLoadFailedText => _resources.GetString("Detail_LoadFailed");
    public IAsyncRelayCommand CloseDetailPreviewCommand => Detail.CloseCommand;

    [ObservableProperty]
    public partial bool IsDetailEditorReady { get; private set; }

    [ObservableProperty]
    public partial bool IsDetailLoadError { get; private set; }

    internal event Action<TaskItemViewModel>? RestoreDetailSelectionRequested;

    internal bool IsEditingSelectedTask(string uid) =>
        IsDetailPaneOpen && IsDetailEditorReady && Detail.Uid == uid && TaskList.SelectedTask?.Uid == uid;

    internal Task<bool> OpenDetailAsync(
        TaskItemViewModel task,
        CancellationToken cancellationToken = default,
        bool refresh = false)
    {
        if (TaskList.SelectedTask?.Uid != task.Uid || cancellationToken.IsCancellationRequested)
            return Task.FromResult(false);
        if (!refresh && _openingUid == task.Uid && _detailRead is { IsCancellationRequested: false }
            && _openingDetail is { IsCompleted: false } pending)
            return pending;

        int version = ++_detailOpenVersion;
        _detailRead?.Cancel();
        if (!refresh && IsEditingSelectedTask(task.Uid))
        {
            _openingUid = null;
            _openingDetail = null;
            return Task.FromResult(true);
        }
        _openingUid = task.Uid;
        return _openingDetail = LoadSelectedDetailAsync(task, version, cancellationToken);
    }

    private async Task<bool> LoadSelectedDetailAsync(TaskItemViewModel task, int version, CancellationToken cancellationToken)
    {
        using var read = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _detailRead = read;
        bool StillSelected() => !read.IsCancellationRequested && version == _detailOpenVersion
            && TaskList.SelectedTask?.Uid == task.Uid;
        try
        {
            // Finish the outgoing write before hiding its editor. Cancel only the incoming reads.
            bool saved = await Detail.FlushPendingEditsAsync();
            if (!StillSelected()) return false;
            if (!saved)
            {
                Detail.ReportError(_resources.GetString("Detail_SaveFailed"));
                TaskList.CloseSuggestions();
                DetailPreview = null;
                IsDetailEditorReady = true;
                IsDetailPaneLoaded = true;
                IsDetailPaneOpen = true;
                if (TaskList.FindTask(Detail.Uid) is { } owner && owner.Uid != task.Uid
                    && TaskList.VisibleItems.Contains(owner))
                    RestoreDetailSelectionRequested?.Invoke(owner);
                return false;
            }

            TaskList.CloseSuggestions();
            IsDetailLoadError = false;
            if (!IsEditingSelectedTask(task.Uid))
            {
                IsDetailEditorReady = false;
                DetailPreview = task;
                IsDetailPaneOpen = true;
            }
            if (!await Detail.LoadAsync(task.Uid, read.Token) || !StillSelected()) return false;
            IsDetailPaneLoaded = true;
            IsDetailEditorReady = true;
            DetailPreview = null;
            return true;
        }
        catch (OperationCanceledException) when (read.IsCancellationRequested)
        {
            if (version == _detailOpenVersion && HasDetailPreview) CloseDetail();
            return false;
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"WINTODO_DETAIL_LOAD_EXCEPTION={DiagnosticException.Describe(exception)}");
            if (StillSelected())
            {
                if (HasDetailPreview) IsDetailLoadError = true;
                else Detail.ReportError(_resources.GetString("Detail_LoadFailed"));
            }
            return false;
        }
        finally
        {
            if (ReferenceEquals(_detailRead, read)) _detailRead = null;
        }
    }

    [RelayCommand]
    private async Task RetryDetailLoadAsync()
    {
        if (DetailPreview is { } target) await OpenDetailAsync(target);
    }

    private async Task<bool> PrepareSuggestionsAsync()
    {
        int version = _detailOpenVersion;
        if (IsDetailPaneOpen && !await Detail.FlushPendingEditsAsync()) return false;
        if (version != _detailOpenVersion) return false;
        CloseDetail();
        return true;
    }

    internal void CloseDetail()
    {
        CancelDetailRead();
        IsDetailPaneOpen = false;
        DetailPreview = null;
        IsDetailLoadError = false;
    }

    internal void CancelDetailRead()
    {
        ++_detailOpenVersion;
        _detailRead?.Cancel();
        _openingUid = null;
        _openingDetail = null;
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
