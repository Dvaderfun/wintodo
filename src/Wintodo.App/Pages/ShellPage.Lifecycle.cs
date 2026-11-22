using System.Diagnostics;
using Microsoft.UI.Xaml.Controls;
using Wintodo.Core.Models;
using Wintodo.Core.Services;
using Wintodo.Services;
using Wintodo.ViewModels;

namespace Wintodo.Pages;

public sealed partial class ShellPage
{
    private bool _isActivationRefreshRunning;
    internal async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        string selectedNavigation = await ViewModel.InitializeAsync(cancellationToken);
        PopulateUserListNavigationItems();
        object selectedItem = ResolveNavigationItem(selectedNavigation);
        ShellNavigation.SelectedItem = selectedItem;
        await ApplySelectionAsync(selectedItem, isSettingsSelected: false, cancellationToken);
        _isNavigationInitialized = true;
        _uiSettings.ColorValuesChanged += OnColorValuesChanged;
        ScheduleMidnightRefresh();
        ScheduleBackgroundSync();
        _ = RunBackgroundSyncAsync();
    }

    internal Task SearchAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        return ViewModel.SearchAsync(text, cancellationToken);
    }

    internal async Task RefreshForActivationAsync(
        CancellationToken cancellationToken = default, bool reloadExternalChanges = false)
    {
        if (!_isNavigationInitialized || _isActivationRefreshRunning) return;
        _isActivationRefreshRunning = true;
        try
        {
            // Reload externally committed shares without replacing active editors or their drafts.
            if (reloadExternalChanges)
            {
                await ViewModel.TaskList.ReloadAsync(cancellationToken);
                await ViewModel.RefreshNavigationAsync(cancellationToken);
            }
            await RefreshForDateChangeAsync(cancellationToken);
            await _windowsIntegration.RefreshAsync(cancellationToken);
            if (_clock.UtcNow - _lastSyncAttempt >= TimeSpan.FromMinutes(1))
            {
                await RunBackgroundSyncAsync(cancellationToken);
            }
            ScheduleMidnightRefresh();
        }
        finally { _isActivationRefreshRunning = false; }
    }

    internal Task RefreshForExternalChangeAsync(
        CancellationToken cancellationToken = default)
    {
        return ViewModel.TaskList.ReloadAsync(cancellationToken);
    }

    internal async Task HandleJumpListArgumentAsync(
        string argument,
        CancellationToken cancellationToken = default)
    {
        if (string.Equals(
            argument,
            WindowsIntegrationService.JumpListNewTaskArgument,
            StringComparison.Ordinal))
        {
            if (!ViewModel.TaskList.CanCreateTask)
            {
                ShellListItemViewModel? fallback = ViewModel.GetQuickAddFallback();
                if (fallback is not null)
                {
                    await SelectNavigationAsync(
                        ResolveNavigationItem(fallback.NavigationKey),
                        cancellationToken);
                }
            }

            TaskList.FocusComposer();
            return;
        }

        if (!argument.StartsWith(
            WindowsIntegrationService.JumpListOpenListPrefix,
            StringComparison.Ordinal))
        {
            return;
        }

        string uid = argument[WindowsIntegrationService.JumpListOpenListPrefix.Length..];
        ShellListItemViewModel? list = ViewModel.UserLists.FirstOrDefault(
            candidate => string.Equals(candidate.Uid, uid, StringComparison.Ordinal));
        if (list is not null)
        {
            await SelectNavigationAsync(
                ResolveNavigationItem(list.NavigationKey),
                cancellationToken);
        }
    }

    internal async Task OpenTaskAsync(
        string uid,
        CancellationToken cancellationToken = default)
    {
        TodoTask? task = await _taskStore.GetTaskAsync(uid, cancellationToken);
        if (task is null)
        {
            return;
        }

        ShellListItemViewModel? list = ViewModel.UserLists.FirstOrDefault(
            candidate => candidate.Id == task.ListId);
        if (list is null)
        {
            return;
        }

        await SelectNavigationAsync(
            ResolveNavigationItem(list.NavigationKey),
            cancellationToken);
        TaskItemViewModel? item = ViewModel.TaskList.FindTask(uid);
        if (item is not null)
        {
            await ViewModel.OpenDetailAsync(item, cancellationToken);
            TaskList.SelectTask(item);
        }
    }

    internal void StopMidnightRefresh()
    {
        ViewModel.UserListVisited -= OnUserListVisited;
        ViewModel.SyncConflictsRequested -= OnSyncConflictsRequested;
        ViewModel.CustomListIconRequested -= OnCustomListIconRequested;
        ViewModel.DisposeSync();
        if (_syncTimer is not null)
        {
            _syncTimer.Stop();
            _syncTimer.Tick -= OnSyncTimerTick;
            _syncTimer = null;
        }

        if (_midnightTimer is null)
        {
            return;
        }

        _midnightTimer.Stop();
        _midnightTimer.Tick -= OnMidnightTimerTick;
        _midnightTimer = null;
    }

    private void ScheduleMidnightRefresh()
    {
        _midnightTimer ??= DispatcherQueue.CreateTimer();
        _midnightTimer.Stop();
        _midnightTimer.Tick -= OnMidnightTimerTick;
        _midnightTimer.Tick += OnMidnightTimerTick;
        _midnightTimer.IsRepeating = false;

        DateTime nextLocalMidnight = DateTime.SpecifyKind(
            _clock.Today.AddDays(1).ToDateTime(TimeOnly.MinValue),
            DateTimeKind.Unspecified);
        DateTime nextUtc =
            TimeZoneInfo.ConvertTimeToUtc(nextLocalMidnight, _clock.LocalTimeZone);
        TimeSpan interval = nextUtc - _clock.UtcNow.UtcDateTime;
        _midnightTimer.Interval = interval > TimeSpan.Zero
            ? interval
            : TimeSpan.FromSeconds(1);
        _midnightTimer.Start();
    }

    private void ScheduleBackgroundSync()
    {
        _syncTimer ??= DispatcherQueue.CreateTimer();
        _syncTimer.Stop();
        _syncTimer.Tick -= OnSyncTimerTick;
        _syncTimer.Tick += OnSyncTimerTick;
        _syncTimer.Interval = TimeSpan.FromMinutes(15);
        _syncTimer.IsRepeating = true;
        _syncTimer.Start();
    }

    private async void OnSyncTimerTick(
        Microsoft.UI.Dispatching.DispatcherQueueTimer sender,
        object args)
    {
        await RunBackgroundSyncAsync();
    }

    private async Task RunBackgroundSyncAsync(
        CancellationToken cancellationToken = default)
    {
        _lastSyncAttempt = _clock.UtcNow;
        try
        {
            await ViewModel.RunBackgroundSyncAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Trace.WriteLine($"WINTODO_BACKGROUND_SYNC_EXCEPTION={DiagnosticException.Describe(exception)}");
        }
    }

    private async void OnMidnightTimerTick(
        Microsoft.UI.Dispatching.DispatcherQueueTimer sender,
        object args)
    {
        await RefreshForDateChangeAsync();
        ScheduleMidnightRefresh();
    }

    private async Task RefreshForDateChangeAsync(CancellationToken cancellationToken = default)
    {
        if (await ViewModel.TaskList.RefreshForDateChangeAsync(cancellationToken))
        {
            ViewModel.Detail.RefreshDateLabels();
            await ViewModel.RefreshNavigationCountsAsync(cancellationToken);
        }
    }
}
