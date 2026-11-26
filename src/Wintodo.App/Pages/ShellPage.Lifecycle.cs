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
    private bool _isWindowMinimized;
    private bool _backgroundSyncStarted;
    private bool _timersStopped;

    internal async Task InitializeAsync(Task<string>? startupNavigation, CancellationToken cancellationToken = default)
    {
        string selectedNavigation = await (startupNavigation ?? ViewModel.InitializeAsync(cancellationToken));
        PopulateUserListNavigationItems();
        object selectedItem = ResolveNavigationItem(selectedNavigation);
        ShellNavigation.SelectedItem = selectedItem;
        // Query the selected list before account initialization; the rows can appear independently.
        Task selection = ApplySelectionAsync(selectedItem, isSettingsSelected: false, cancellationToken);
        await Task.WhenAll(selection, ViewModel.InitializeSyncAsync(cancellationToken));
        _isNavigationInitialized = true;
        _uiSettings.ColorValuesChanged += OnColorValuesChanged;
        ScheduleMidnightRefresh();
        StartBackgroundSync();
    }

    private void StartBackgroundSync()
    {
        if (_timersStopped || _backgroundSyncStarted) return;
        _backgroundSyncStarted = true;
        UpdateBackgroundSyncTimer();
        TraceTimerState("startup");
        if (ViewModel.IsSyncConnected) _ = RunBackgroundSyncAsync();
    }

    internal async Task SetWindowMinimizedAsync(bool minimized)
    {
        if (_isWindowMinimized == minimized) return;
        _isWindowMinimized = minimized;
        // Navigation briefly suppresses selection events while it awaits programmatic
        // list switches. Window visibility still owns these timers during that time.
        if (_timersStopped || !_backgroundSyncStarted) return;
        if (minimized)
        {
            _midnightTimer?.Stop();
            TraceTimerState("minimize");
            return;
        }

        // A restore need not take keyboard focus. The task list records the new
        // date before it awaits a reload, so a concurrent activation sees it too.
        try
        {
            await RefreshForDateChangeAsync();
        }
        finally
        {
            ScheduleMidnightRefresh();
            TraceTimerState("restore");
        }
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
            if (_backgroundSyncStarted && ViewModel.IsSyncConnected &&
                _clock.UtcNow - _lastSyncAttempt >= TimeSpan.FromMinutes(1))
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

    // Returns true for New Task so the caller can focus the composer once input is enabled.
    internal async Task<bool> SelectJumpListTargetAsync(
        string argument,
        CancellationToken cancellationToken = default)
    {
        if (string.Equals(
            argument,
            LaunchOptions.NewTaskArgument,
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

            return true;
        }

        if (!argument.StartsWith(
            LaunchOptions.OpenListArgumentPrefix,
            StringComparison.Ordinal))
        {
            return false;
        }

        string uid = argument[LaunchOptions.OpenListArgumentPrefix.Length..];
        ShellListItemViewModel? list = ViewModel.UserLists.FirstOrDefault(
            candidate => string.Equals(candidate.Uid, uid, StringComparison.Ordinal));
        if (list is not null)
        {
            await SelectNavigationAsync(
                ResolveNavigationItem(list.NavigationKey),
                cancellationToken);
        }
        return false;
    }

    internal void FocusComposer() => TaskList.FocusComposer();

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
            TaskList.SelectTask(item);
            await ViewModel.OpenDetailAsync(item, cancellationToken);
        }
    }

    internal void StopMidnightRefresh()
    {
        _timersStopped = true;
        ShellRoot.ActualThemeChanged -= OnActualThemeChanged;
        _uiSettings.ColorValuesChanged -= OnColorValuesChanged;
        ViewModel.AppearanceSettingsChanged -= OnAppearanceSettingsChanged;
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        ViewModel.CancelDetailRead();
        _stoppedPaneResize = true;
        ViewModel.UserListVisited -= OnUserListVisited;
        ViewModel.SyncConflictsRequested -= OnSyncConflictsRequested;
        ViewModel.CustomListIconRequested -= OnCustomListIconRequested;
        ViewModel.DisposeSync();
        UpdateBackgroundSyncTimer();

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
        if (_timersStopped || _isWindowMinimized) return;
        _midnightTimer ??= DispatcherQueue.CreateTimer();
        _midnightTimer.Stop();
        _midnightTimer.Tick -= OnMidnightTimerTick;
        _midnightTimer.Tick += OnMidnightTimerTick;
        _midnightTimer.IsRepeating = false;

        DateTimeOffset nextUtc = LocalDay.StartUtc(_clock.Today.AddDays(1), _clock.LocalTimeZone);
        TimeSpan interval = nextUtc - _clock.UtcNow;
        _midnightTimer.Interval = interval > TimeSpan.Zero
            ? interval
            : TimeSpan.FromSeconds(1);
        _midnightTimer.Start();
    }

    private void UpdateBackgroundSyncTimer()
    {
        if (!_backgroundSyncStarted || _timersStopped || !ViewModel.IsSyncConnected)
        {
            if (_syncTimer is not null)
            {
                _syncTimer.Stop();
                _syncTimer.Tick -= OnSyncTimerTick;
                _syncTimer = null;
            }
            return;
        }

        if (_syncTimer is not null) return;
        _syncTimer = DispatcherQueue.CreateTimer();
        _syncTimer.Tick += OnSyncTimerTick;
        _syncTimer.Interval = TimeSpan.FromMinutes(15);
        _syncTimer.IsRepeating = true;
        _syncTimer.Start();
    }

    [Conditional("DEBUG")]
    private void TraceTimerState(string transition)
    {
        string sync = _syncTimer is null ? "absent" : _syncTimer.IsRunning ? "running" : "stopped";
        string midnight = _midnightTimer?.IsRunning == true ? "running" : "off";
        Trace.WriteLine($"WINTODO_SHELL_TIMERS={transition};SYNC={sync};MIDNIGHT={midnight}");
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
        if (_timersStopped || !ViewModel.IsSyncConnected || ViewModel.IsSyncBusy) return;
        _lastSyncAttempt = _clock.UtcNow;
        TraceTimerState("sync");
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
        if (_timersStopped || _isWindowMinimized) return;
        if (await ViewModel.TaskList.RefreshForDateChangeAsync(cancellationToken))
        {
            ViewModel.Detail.RefreshDateLabels();
            await ViewModel.RefreshNavigationCountsAsync(cancellationToken);
        }
    }
}
