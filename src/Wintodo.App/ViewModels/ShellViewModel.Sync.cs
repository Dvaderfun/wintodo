using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Wintodo.Core.Models;
using Wintodo.Sync;
using Wintodo.Core.Services;

namespace Wintodo.ViewModels;

public sealed partial class ShellViewModel
{
    private CalDavSyncService _syncService = null!;
    private DispatcherQueue _dispatcherQueue = null!;
    private Account? _syncAccount;

    internal event Action<IReadOnlyList<SyncConflict>>? SyncConflictsRequested;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCalDavCommand))]
    public partial string CalDavServerUrl { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCalDavCommand))]
    public partial string CalDavUsername { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCalDavCommand))]
    public partial string CalDavPassword { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCalDavCommand))]
    [NotifyCanExecuteChangedFor(nameof(DisconnectCalDavCommand))]
    [NotifyCanExecuteChangedFor(nameof(SyncNowCommand))]
    public partial bool IsSyncBusy { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCalDavCommand))]
    [NotifyCanExecuteChangedFor(nameof(DisconnectCalDavCommand))]
    [NotifyCanExecuteChangedFor(nameof(SyncNowCommand))]
    public partial bool IsSyncConnected { get; set; }

    [ObservableProperty]
    public partial bool IsSyncConflictDialogLoaded { get; set; }

    [ObservableProperty]
    public partial string SyncAccountDisplayName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SyncStatusText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SyncStatusGlyph { get; set; } = "\uE895";

    // SyncStatusText only renders inside the connected-account card, so a failed
    // connect \u2014 which leaves IsSyncConnected false \u2014 would otherwise report
    // nothing at all. These drive a section-level InfoBar instead.
    [ObservableProperty]
    public partial bool IsSyncErrorOpen { get; set; }

    [ObservableProperty]
    public partial string SyncErrorMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial IList<object> SyncListOptions { get; set; } = new List<object>();

    private void InitializeSync(CalDavSyncService syncService)
    {
        _syncService = syncService;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        _syncService.StatusChanged += OnSyncStatusChanged;
        SetSyncStatus(SyncRunStatus.Idle);
    }

    internal async Task InitializeSyncAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<Account> accounts = await _taskStore.GetAccountsAsync(cancellationToken);
        _syncAccount = accounts.FirstOrDefault();
        IsSyncConnected = _syncAccount is not null;
        SyncAccountDisplayName = _syncAccount?.DisplayName ?? string.Empty;
        bool hasPendingMoves = await _taskStore.HasPendingTaskMovesAsync(cancellationToken);
        if (hasPendingMoves && _syncAccount is null)
        {
            ShowPendingTaskMoveGuidance();
        }
        else
        {
            SetSyncStatus(hasPendingMoves ? SyncRunStatus.AttentionRequired
                : _syncAccount is null ? SyncRunStatus.Idle : SyncRunStatus.Success);
        }
    }

    private void LoadSyncListOptions(IReadOnlyList<TodoList> lists)
    {
        SyncListOptions = lists
            .Where(list => !list.IsDeleted)
            .OrderByDescending(list => list.IsDefault)
            .ThenBy(list => list.SortOrder)
            .Select(list => (object)new SyncListOptionViewModel(list, SetListSyncEnabledAsync))
            .ToList();
    }

    [RelayCommand(CanExecute = nameof(CanConnectCalDav))]
    private async Task ConnectCalDavAsync()
    {
        IsSyncErrorOpen = false;
        SyncErrorMessage = string.Empty;
        IsSyncBusy = true;
        try
        {
            _syncAccount = await _syncService.ConnectAsync(
                CalDavUsername.Trim(),
                CalDavServerUrl.Trim(),
                CalDavUsername.Trim(),
                CalDavPassword);
            CalDavPassword = string.Empty;
            IsSyncConnected = true;
            SyncAccountDisplayName = _syncAccount.DisplayName;
            await RefreshNavigationAsync();
            await ExecuteSyncAndRefreshAsync(promptConflicts: true, CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Trace.WriteLine($"WINTODO_SYNC_CONNECT_EXCEPTION={DiagnosticException.Describe(exception)}");
            SetSyncStatus(SyncRunStatus.Error);
        }
        finally
        {
            IsSyncBusy = false;
        }
    }

    private bool CanConnectCalDav()
    {
        return !IsSyncConnected
            && !IsSyncBusy
            && !string.IsNullOrWhiteSpace(CalDavServerUrl)
            && !string.IsNullOrWhiteSpace(CalDavUsername)
            && !string.IsNullOrWhiteSpace(CalDavPassword);
    }

    [RelayCommand(CanExecute = nameof(CanUseCalDavAccount))]
    private async Task DisconnectCalDavAsync()
    {
        if (_syncAccount is null)
        {
            return;
        }

        IsSyncBusy = true;
        try
        {
            await _syncService.DisconnectAsync(_syncAccount);
            _syncAccount = null;
            IsSyncConnected = false;
            SyncAccountDisplayName = string.Empty;
            await RefreshNavigationAsync();
        }
        catch (PendingTaskMoveException)
        {
            ShowPendingTaskMoveGuidance();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Trace.WriteLine($"WINTODO_SYNC_DISCONNECT_EXCEPTION={DiagnosticException.Describe(exception)}");
            SetSyncStatus(SyncRunStatus.Error);
        }
        finally
        {
            IsSyncBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseCalDavAccount))]
    private Task SyncNowAsync()
    {
        return RunSyncAsync(promptConflicts: true);
    }

    private bool CanUseCalDavAccount()
    {
        return IsSyncConnected && !IsSyncBusy;
    }

    internal Task RunBackgroundSyncAsync(CancellationToken cancellationToken = default)
    {
        return !IsSyncConnected || IsSyncBusy
            ? Task.CompletedTask
            : RunSyncAsync(promptConflicts: true, cancellationToken);
    }

    internal Task ResolveSyncConflictAsync(
        SyncConflict conflict,
        SyncConflictChoice choice,
        CancellationToken cancellationToken = default)
    {
        Undo.Invalidate([conflict.LocalTask.Uid]);
        return _syncService.ResolveConflictAsync(conflict, choice, cancellationToken);
    }

    internal Task PushConflictResolutionsAsync(CancellationToken cancellationToken = default)
    {
        return RunSyncAsync(promptConflicts: true, cancellationToken);
    }

    internal void DisposeSync()
    {
        _syncService.StatusChanged -= OnSyncStatusChanged;
        _taskStore.StoreChanged -= OnUndoStoreChanged;
        TaskList.DisposeUndo();
    }

    private async Task RunSyncAsync(
        bool promptConflicts,
        CancellationToken cancellationToken = default)
    {
        if (!IsSyncConnected || IsSyncBusy)
        {
            return;
        }

        IsSyncBusy = true;
        try
        {
            await ExecuteSyncAndRefreshAsync(promptConflicts, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Trace.WriteLine($"WINTODO_SYNC_EXCEPTION={DiagnosticException.Describe(exception)}");
            SetSyncStatus(SyncRunStatus.Error);
        }
        finally
        {
            IsSyncBusy = false;
        }
    }

    private async Task SetListSyncEnabledAsync(
        SyncListOptionViewModel option,
        bool isEnabled)
    {
        if (_syncAccount is null || IsSyncBusy)
        {
            option.SetWithoutCallback(!isEnabled);
            return;
        }

        IsSyncBusy = true;
        try
        {
            await _syncService.SetListSyncEnabledAsync(
                _syncAccount,
                option.List,
                isEnabled);
            await RefreshNavigationAsync();
            if (isEnabled)
            {
                await ExecuteSyncAndRefreshAsync(promptConflicts: true, CancellationToken.None);
            }
        }
        catch (PendingTaskMoveException)
        {
            option.SetWithoutCallback(!isEnabled);
            ShowPendingTaskMoveGuidance();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Trace.WriteLine($"WINTODO_SYNC_LIST_EXCEPTION={DiagnosticException.Describe(exception)}");
            option.SetWithoutCallback(!isEnabled);
            SetSyncStatus(SyncRunStatus.Error);
        }
        finally
        {
            IsSyncBusy = false;
        }
    }

    private async Task ExecuteSyncAndRefreshAsync(
        bool promptConflicts,
        CancellationToken cancellationToken)
    {
        SyncRunResult result = await _syncService.SyncAllAsync(cancellationToken);
        await RefreshNavigationAsync(cancellationToken);
        TaskList.RefreshOpenSuggestions();
        if (promptConflicts && result.Conflicts.Count > 0)
        {
            SyncConflictsRequested?.Invoke(result.Conflicts);
        }
    }

    private void OnSyncStatusChanged(object? sender, SyncStatusChangedEventArgs args)
    {
        if (!string.IsNullOrWhiteSpace(args.Message))
        {
            Trace.WriteLine($"WINTODO_SYNC_STATUS={(int)args.Status}");
        }

        _dispatcherQueue.TryEnqueue(() => SetSyncStatus(args.Status));
    }

    private void ShowPendingTaskMoveGuidance()
    {
        SetSyncStatus(SyncRunStatus.AttentionRequired);
        SyncErrorMessage = _resources.GetString("Sync_PendingMovesMessage");
        IsSyncErrorOpen = true;
    }

    private void SetSyncStatus(SyncRunStatus status)
    {
        string key = status switch
        {
            SyncRunStatus.Syncing => "Sync_StatusSyncing",
            SyncRunStatus.Success => "Sync_StatusUpToDate",
            SyncRunStatus.Offline => "Sync_StatusOffline",
            SyncRunStatus.AttentionRequired => "Sync_StatusAttention",
            SyncRunStatus.Error => "Sync_StatusError",
            _ => "Sync_StatusNotConnected",
        };
        SyncStatusText = _resources.GetString(key);
        SyncStatusGlyph = status switch
        {
            SyncRunStatus.Syncing => "\uE895",
            SyncRunStatus.Success => "\uE73E",
            SyncRunStatus.Offline => "\uE704",
            SyncRunStatus.AttentionRequired => "\uE7BA",
            SyncRunStatus.Error => "\uEA39",
            _ => "\uE895",
        };

        if (status == SyncRunStatus.Error)
        {
            // Service/provider messages can include account URLs or task content.
            SyncErrorMessage = _resources.GetString("Sync_ErrorRetry");
            IsSyncErrorOpen = true;
            return;
        }

        IsSyncErrorOpen = false;
        SyncErrorMessage = string.Empty;
    }
}
