namespace Wintodo.Core.Services;

public interface ISettingsStore
{
    const string ConfirmBeforeDeleteKey = "tasks.confirm_before_delete";
    const string DefaultReminderTimeKey = "tasks.default_reminder_time";
    const string WeekStartKey = "tasks.week_start";
    const string AddNewToTopKey = "tasks.add_new_to_top";
    const string MoveStarredToTopKey = "tasks.move_starred_to_top";
    const string NotificationsEnabledKey = "notifications.enabled";
    const string DefaultSnoozeKey = "notifications.default_snooze";

    Task<string?> GetSettingAsync(
        string key,
        CancellationToken cancellationToken = default);

    /// <summary>Read the requested keys from one snapshot; missing keys are omitted.</summary>
    Task<IReadOnlyDictionary<string, string>> GetSettingsAsync(
        IReadOnlyList<string> keys,
        CancellationToken cancellationToken = default);

    Task SetSettingAsync(
        string key,
        string value,
        CancellationToken cancellationToken = default);

    /// <summary>Commit related settings together so a reader never sees a partial update.</summary>
    Task SetSettingsAsync(
        IReadOnlyDictionary<string, string> settings,
        CancellationToken cancellationToken = default);

    Task DeleteSettingAsync(
        string key,
        CancellationToken cancellationToken = default);
}
