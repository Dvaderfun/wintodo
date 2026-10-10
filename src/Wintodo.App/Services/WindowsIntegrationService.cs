using System.Diagnostics;
using System.Globalization;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using Microsoft.Windows.ApplicationModel.Resources;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;
using Windows.UI.StartScreen;
using Wintodo.Core.Models;
using Wintodo.Core.Services;

namespace Wintodo.Services;

internal readonly record struct NotificationActivationResult(
    bool ShowWindow,
    string? TaskUid);

internal sealed class WindowsIntegrationService : IAsyncDisposable
{
    private const string RecentListsSetting = "windows.recent_lists";
    private const string NewTaskArgument = "wintodo:new-task";
    private const string OpenListArgumentPrefix = "wintodo:list:";
#if DEBUG
    internal const string DebugDatabasePathArgument = "debugDatabasePath";
    private readonly string? _debugDatabasePath;
#endif
    private readonly ITaskStore _store;
    private readonly IClock _clock;
    private readonly ResourceLoader _resources = new();
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly object _refreshSync = new();
    private CancellationTokenSource? _refreshDebounce;
    private bool _isInitialized;
    private bool _isDisposed;

    internal WindowsIntegrationService(
        ITaskStore store,
        IClock clock
#if DEBUG
        , string? debugDatabasePath = null
#endif
    )
    {
        _store = store;
        _clock = clock;
        NotificationPreferences = new NotificationPreferences(store);
#if DEBUG
        _debugDatabasePath = debugDatabasePath;
#endif
    }

    internal static string JumpListNewTaskArgument => NewTaskArgument;

    internal NotificationPreferences NotificationPreferences { get; }

    internal event Action<Exception>? NotificationSettingsFailed;

    internal static string JumpListOpenListPrefix => OpenListArgumentPrefix;

    internal async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_isInitialized)
        {
            return;
        }

        _isInitialized = true;
        await NotificationPreferences.LoadAsync(cancellationToken);
        NotificationPreferences.PropertyChanged += OnNotificationPreferencesChanged;
        _store.StoreChanged += OnStoreChanged;
        await RefreshAsync(cancellationToken);
    }

    internal async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        await _refreshGate.WaitAsync(cancellationToken);
        try
        {
            await TryRefreshScheduledRemindersAsync(cancellationToken);
            await TryRefreshBadgeAsync(cancellationToken);
            await TryRefreshJumpListAsync(cancellationToken);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    internal async Task RecordRecentListAsync(
        string listUid,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(listUid);
        string? stored = await _store.GetSettingAsync(
            RecentListsSetting,
            cancellationToken);
        List<string> recent = ParseRecentLists(stored);
        recent.RemoveAll(uid => string.Equals(uid, listUid, StringComparison.Ordinal));
        recent.Insert(0, listUid);
        if (recent.Count > 3)
        {
            recent.RemoveRange(3, recent.Count - 3);
        }

        await _store.SetSettingAsync(
            RecentListsSetting,
            string.Join('\n', recent),
            cancellationToken);
        await _refreshGate.WaitAsync(cancellationToken);
        try
        {
            await TryRefreshJumpListAsync(cancellationToken);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    internal async Task<NotificationActivationResult> HandleNotificationAsync(
        IDictionary<string, string> arguments,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        string action = GetArgument(arguments, "action");
        string taskUid = GetArgument(arguments, "taskUid");
        string alarmUid = GetArgument(arguments, "alarmUid");

        if (string.IsNullOrWhiteSpace(taskUid))
        {
            return new NotificationActivationResult(true, null);
        }

        switch (action)
        {
            case "complete":
                TodoTask? task = await _store.GetTaskAsync(taskUid, cancellationToken);
                await _store.CompleteTaskAsync(taskUid, cancellationToken);
                if (string.IsNullOrWhiteSpace(task?.RRule)
                    && !string.IsNullOrWhiteSpace(alarmUid))
                {
                    await TryAcknowledgeAlarmAsync(alarmUid, cancellationToken);
                }

                await RemoveTaskNotificationsAsync(taskUid);
                await RefreshAsync(cancellationToken);
                return new NotificationActivationResult(false, null);

            case "snooze" when !string.IsNullOrWhiteSpace(alarmUid):
                DateTimeOffset snoozedUntil = ResolveSnoozeTime(
                    GetArgument(arguments, "snooze"));
                await RemoveTaskNotificationsAsync(taskUid);
                await _store.SnoozeAlarmAsync(
                    alarmUid,
                    snoozedUntil,
                    cancellationToken);
                await RefreshAsync(cancellationToken);
                return new NotificationActivationResult(false, null);

            default:
                return new NotificationActivationResult(true, taskUid);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        NotificationPreferences.PropertyChanged -= OnNotificationPreferencesChanged;
        _store.StoreChanged -= OnStoreChanged;
        lock (_refreshSync)
        {
            _refreshDebounce?.Cancel();
            _refreshDebounce?.Dispose();
            _refreshDebounce = null;
        }

        await _refreshGate.WaitAsync();
        _refreshGate.Release();
        _refreshGate.Dispose();
    }

    private void OnNotificationPreferencesChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        // ToastNotifier can synchronously enter Windows COM APIs. Keep that work
        // out of a XAML/UI Automation toggle callback, even if saves finish inline.
        _ = Task.Run(RefreshAfterPreferenceChangeAsync);
    }

    private async Task RefreshAfterPreferenceChangeAsync()
    {
        try
        {
            await NotificationPreferences.FlushAsync();
            await _refreshGate.WaitAsync();
            try
            {
                if (!_isDisposed)
                {
                    await RefreshScheduledRemindersAsync(CancellationToken.None);
                }
            }
            finally
            {
                _refreshGate.Release();
            }
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"WINTODO_NOTIFICATION_SETTINGS_ERROR={exception}");
            NotificationSettingsFailed?.Invoke(exception);
        }
    }

    private void OnStoreChanged(object? sender, StoreChangedEventArgs args)
    {
        lock (_refreshSync)
        {
            if (_isDisposed)
            {
                return;
            }

            _refreshDebounce?.Cancel();
            _refreshDebounce?.Dispose();
            _refreshDebounce = new CancellationTokenSource();
            _ = RefreshAfterStoreChangeAsync(_refreshDebounce.Token);
        }
    }

    private async Task RefreshAfterStoreChangeAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
            await RefreshAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"WINTODO_WINDOWS_REFRESH_EXCEPTION={exception}");
        }
    }

    private async Task TryRefreshScheduledRemindersAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            await RefreshScheduledRemindersAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"WINTODO_REMINDER_REFRESH_EXCEPTION={exception}");
        }
    }

    private async Task RefreshScheduledRemindersAsync(
        CancellationToken cancellationToken)
    {
        ToastNotifier notifier = CreateScheduledToastNotifier();
        IReadOnlyList<ScheduledToastNotification> scheduled =
            notifier.GetScheduledToastNotifications();
        if (!NotificationPreferences.Enabled)
        {
            foreach (ScheduledToastNotification notification in scheduled)
            {
                notifier.RemoveFromSchedule(notification);
            }
#if DEBUG
            WriteGateNotificationState(notifier);
#endif
            return;
        }

        IReadOnlyList<PendingReminder> pending =
            await _store.GetPendingRemindersAsync(
                DateTimeOffset.MaxValue,
                cancellationToken);
        pending = pending
            .Where(reminder => reminder.Alarm.Action == AlarmAction.Display)
            .ToArray();
        var desiredKeys = pending
            .Select(reminder => (reminder.Alarm.Uid, reminder.TaskUid))
            .ToHashSet();

        foreach (ScheduledToastNotification notification in scheduled)
        {
            if (!desiredKeys.Contains((notification.Tag, notification.Group)))
            {
                notifier.RemoveFromSchedule(notification);
            }
        }

        foreach (PendingReminder reminder in pending)
        {
            ScheduledToastNotification? existing = scheduled.FirstOrDefault(
                notification => string.Equals(
                        notification.Tag,
                        reminder.Alarm.Uid,
                        StringComparison.Ordinal)
                    && string.Equals(
                        notification.Group,
                        reminder.TaskUid,
                        StringComparison.Ordinal));
            if (reminder.EffectiveTrigger <= _clock.UtcNow.AddSeconds(1))
            {
                if (existing is not null)
                {
                    notifier.RemoveFromSchedule(existing);
                }

                if (string.IsNullOrWhiteSpace(reminder.Alarm.ScheduledToastTag))
                {
                    AppNotification notification = BuildNotification(reminder);
                    AppNotificationManager.Default.Show(notification);
                    await _store.SetAlarmScheduledToastTagAsync(
                        reminder.Alarm.Uid,
                        reminder.TaskUid,
                        cancellationToken);
                }

                continue;
            }

            bool matchesExisting = existing is not null
                && (existing.DeliveryTime - reminder.EffectiveTrigger).Duration()
                    < TimeSpan.FromSeconds(1)
                && (existing.Content.SelectSingleNode("//input[@id='snooze']") as XmlElement)?
                    .GetAttribute("defaultInput") == NotificationPreferences.DefaultSnooze;
            if (matchesExisting)
            {
                if (!string.Equals(
                    reminder.Alarm.ScheduledToastTag,
                    reminder.TaskUid,
                    StringComparison.Ordinal))
                {
                    await _store.SetAlarmScheduledToastTagAsync(
                        reminder.Alarm.Uid,
                        reminder.TaskUid,
                        cancellationToken);
                }

                continue;
            }

            if (existing is not null)
            {
                notifier.RemoveFromSchedule(existing);
            }

            AppNotification appNotification = BuildNotification(reminder);
            var payload = new XmlDocument();
            payload.LoadXml(appNotification.Payload);
            var scheduledNotification = new ScheduledToastNotification(
                payload,
                reminder.EffectiveTrigger)
            {
                Tag = reminder.Alarm.Uid,
                Group = reminder.TaskUid,
            };
            notifier.AddToSchedule(scheduledNotification);
            await _store.SetAlarmScheduledToastTagAsync(
                reminder.Alarm.Uid,
                reminder.TaskUid,
                cancellationToken);
        }
#if DEBUG
        WriteGateNotificationState(notifier);
#endif
    }

#if DEBUG
    private void WriteGateNotificationState(ToastNotifier notifier)
    {
        // Only the existing m7-gate.db harness opts into this evidence file.
        if (string.IsNullOrWhiteSpace(_debugDatabasePath))
        {
            return;
        }

        var state = new System.Xml.Linq.XElement("scheduled",
            notifier.GetScheduledToastNotifications().Select(notification =>
                new System.Xml.Linq.XElement("notification",
                    new System.Xml.Linq.XAttribute("tag", notification.Tag),
                    new System.Xml.Linq.XAttribute("group", notification.Group),
                    System.Xml.Linq.XElement.Parse(notification.Content.GetXml()))));
        string path = Path.Combine(Path.GetDirectoryName(_debugDatabasePath)!, "notification-state.xml");
        File.WriteAllText(path + ".tmp", state.ToString());
        File.Move(path + ".tmp", path, overwrite: true);
    }
#endif

    private AppNotification BuildNotification(PendingReminder reminder)
    {
        string body = string.IsNullOrWhiteSpace(reminder.Alarm.Description)
            ? _resources.GetString("Notification_ReminderBody")
            : reminder.Alarm.Description;
        var completeButton = new AppNotificationButton(
                _resources.GetString("Notification_Complete"))
            .AddArgument("action", "complete")
            .AddArgument("taskUid", reminder.TaskUid)
            .AddArgument("alarmUid", reminder.Alarm.Uid);
        var snoozeButton = new AppNotificationButton(
                _resources.GetString("Notification_Snooze"))
            .AddArgument("action", "snooze")
            .AddArgument("taskUid", reminder.TaskUid)
            .AddArgument("alarmUid", reminder.Alarm.Uid);
#if DEBUG
        if (!string.IsNullOrWhiteSpace(_debugDatabasePath))
        {
            completeButton.AddArgument(DebugDatabasePathArgument, _debugDatabasePath);
            snoozeButton.AddArgument(DebugDatabasePathArgument, _debugDatabasePath);
        }
#endif
        AppNotificationBuilder builder = new AppNotificationBuilder()
            .AddArgument("action", "open")
            .AddArgument("taskUid", reminder.TaskUid)
            .AddArgument("alarmUid", reminder.Alarm.Uid)
            .AddText(reminder.TaskSummary)
            .AddText(body)
            .AddComboBox(new AppNotificationComboBox("snooze")
                .AddItem("10", _resources.GetString("Notification_Snooze10Minutes"))
                .AddItem("60", _resources.GetString("Notification_SnoozeOneHour"))
                .AddItem("tomorrow", _resources.GetString("Notification_SnoozeTomorrow"))
                .SetSelectedItem(NotificationPreferences.DefaultSnooze))
            .AddButton(completeButton)
            .AddButton(snoozeButton)
            .SetScenario(AppNotificationScenario.Reminder);
#if DEBUG
        if (!string.IsNullOrWhiteSpace(_debugDatabasePath))
        {
            builder.AddArgument(DebugDatabasePathArgument, _debugDatabasePath);
        }
#endif
        AppNotification notification = builder.BuildNotification();
        notification.Tag = reminder.Alarm.Uid;
        notification.Group = reminder.TaskUid;
        return notification;
    }

    private async Task TryRefreshBadgeAsync(CancellationToken cancellationToken)
    {
        try
        {
            DateTimeOffset tomorrow = StartOfLocalDay(_clock.Today.AddDays(1));
            int count = await _store.GetActiveDueTaskCountAsync(
                tomorrow,
                cancellationToken);
            BadgeUpdater updater = BadgeUpdateManager.CreateBadgeUpdaterForApplication();
            if (count <= 0)
            {
                updater.Clear();
                return;
            }

            XmlDocument badgeXml = BadgeUpdateManager.GetTemplateContent(
                BadgeTemplateType.BadgeNumber);
            if (badgeXml.SelectSingleNode("/badge") is XmlElement badge)
            {
                badge.SetAttribute(
                    "value",
                    Math.Min(count, 99).ToString(CultureInfo.InvariantCulture));
                updater.Update(new BadgeNotification(badgeXml));
            }
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"WINTODO_BADGE_REFRESH_EXCEPTION={exception}");
        }
    }

    private async Task TryRefreshJumpListAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!JumpList.IsSupported())
            {
                return;
            }

            Task<IReadOnlyList<TodoList>> listsTask =
                _store.GetListsAsync(cancellationToken);
            Task<string?> recentTask = _store.GetSettingAsync(
                RecentListsSetting,
                cancellationToken);
            await Task.WhenAll(listsTask, recentTask);
            Dictionary<string, TodoList> lists = (await listsTask)
                .ToDictionary(list => list.Uid, StringComparer.Ordinal);
            List<string> recent = ParseRecentLists(await recentTask);
            JumpList jumpList = await JumpList.LoadCurrentAsync();
            jumpList.Items.Clear();

            JumpListItem newTask = JumpListItem.CreateWithArguments(
                NewTaskArgument,
                _resources.GetString("JumpList_NewTask"));
            newTask.Description = _resources.GetString("JumpList_NewTaskDescription");
            newTask.Logo = new Uri(
                "ms-appx:///Assets/Square44x44Logo.targetsize-48_altform-lightunplated.png");
            jumpList.Items.Add(newTask);

            foreach (string uid in recent)
            {
                if (!lists.TryGetValue(uid, out TodoList? list))
                {
                    continue;
                }

                JumpListItem item = JumpListItem.CreateWithArguments(
                    OpenListArgumentPrefix + uid,
                    list.DisplayName);
                item.GroupName = _resources.GetString("JumpList_RecentLists");
                item.Description = string.Format(
                    DisplayCulture.Current,
                    _resources.GetString("JumpList_OpenListDescription"),
                    list.DisplayName);
                item.Logo = new Uri(
                    "ms-appx:///Assets/Square44x44Logo.targetsize-48_altform-lightunplated.png");
                jumpList.Items.Add(item);
            }

            await jumpList.SaveAsync();
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"WINTODO_JUMP_LIST_REFRESH_EXCEPTION={exception}");
        }
    }

    private async Task RemoveTaskNotificationsAsync(string taskUid)
    {
        ToastNotifier notifier = CreateScheduledToastNotifier();
        foreach (ScheduledToastNotification notification in
                 notifier.GetScheduledToastNotifications())
        {
            if (string.Equals(notification.Group, taskUid, StringComparison.Ordinal))
            {
                notifier.RemoveFromSchedule(notification);
            }
        }

        await AppNotificationManager.Default.RemoveByGroupAsync(taskUid);
    }

    private async Task TryAcknowledgeAlarmAsync(
        string alarmUid,
        CancellationToken cancellationToken)
    {
        try
        {
            await _store.AcknowledgeAlarmAsync(alarmUid, cancellationToken);
        }
        catch (KeyNotFoundException)
        {
        }
    }

    private DateTimeOffset ResolveSnoozeTime(string value)
    {
        return value switch
        {
            "60" => _clock.UtcNow.AddHours(1),
            "tomorrow" => AtLocalTime(_clock.Today.AddDays(1), new TimeOnly(9, 0)),
            _ => _clock.UtcNow.AddMinutes(10),
        };
    }

    private DateTimeOffset StartOfLocalDay(DateOnly date)
    {
        return AtLocalTime(date, TimeOnly.MinValue);
    }

    private DateTimeOffset AtLocalTime(DateOnly date, TimeOnly time)
    {
        DateTime local = DateTime.SpecifyKind(
            date.ToDateTime(time),
            DateTimeKind.Unspecified);
        if (_clock.LocalTimeZone.IsInvalidTime(local))
        {
            local = local.AddHours(1);
        }

        TimeSpan offset = _clock.LocalTimeZone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset);
    }

    // AppNotificationManager has no scheduling API, so reminders deliberately use the
    // UWP ScheduledToastNotification path (PLAN.md M7). WUI1001 recommends the
    // replacement that cannot schedule; the suppression is recorded in docs/PROGRESS.md.
#pragma warning disable WUI1001
    private static ToastNotifier CreateScheduledToastNotifier() =>
        ToastNotificationManager.CreateToastNotifier();
#pragma warning restore WUI1001

    private static string GetArgument(
        IDictionary<string, string> arguments,
        string key)
    {
        return arguments.TryGetValue(key, out string? value)
            ? value
            : string.Empty;
    }

    private static List<string> ParseRecentLists(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(
                    '\n',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.Ordinal)
                .Take(3)
                .ToList();
    }
}
