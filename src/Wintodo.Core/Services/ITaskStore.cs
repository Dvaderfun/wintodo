using Wintodo.Core.Import;
using Wintodo.Core.Ical;
using Wintodo.Core.Models;
using Wintodo.Core.Queries;

namespace Wintodo.Core.Services;

public interface ITaskStore : ISettingsStore, ISyncStore, IImportStore, IAsyncDisposable
{
    event EventHandler<StoreChangedEventArgs>? StoreChanged;

    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TodoList>> GetListsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<long, int>> GetIncompleteTaskCountsAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<ListQueryKind, int>> GetSmartListTaskCountsAsync(
        CancellationToken cancellationToken = default);

    Task<TodoList> CreateListAsync(
        string displayName,
        ListColor color = ListColor.Blue,
        long? groupId = null,
        CancellationToken cancellationToken = default);

    Task<TodoList> EnsureDefaultListAsync(
        string displayName,
        CancellationToken cancellationToken = default);

    Task RenameListAsync(long listId, string displayName, CancellationToken cancellationToken = default);

    Task SetListColorAsync(long listId, ListColor color, CancellationToken cancellationToken = default);

    Task SetListIconEmojiAsync(
        long listId,
        string? iconEmoji,
        CancellationToken cancellationToken = default);

    Task SetListBackgroundThemeAsync(
        long listId,
        string? backgroundTheme,
        CancellationToken cancellationToken = default);

    Task SetListSortAsync(
        long listId,
        SortMode sortMode,
        bool descending,
        CancellationToken cancellationToken = default);

    Task SetListShowCompletedAsync(
        long listId,
        bool showCompleted,
        CancellationToken cancellationToken = default);

    Task SetListCompletedExpandedAsync(
        long listId,
        bool completedExpanded,
        CancellationToken cancellationToken = default);

    Task ReorderListAsync(long listId, double sortOrder, CancellationToken cancellationToken = default);

    Task MoveListToGroupAsync(long listId, long? groupId, CancellationToken cancellationToken = default);

    Task DeleteListAsync(long listId, CancellationToken cancellationToken = default);

    Task RestoreListAsync(long listId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ListGroup>> GetGroupsAsync(CancellationToken cancellationToken = default);

    Task<ListGroup> CreateGroupAsync(string name, CancellationToken cancellationToken = default);

    Task RenameGroupAsync(long groupId, string name, CancellationToken cancellationToken = default);

    Task DeleteGroupAsync(long groupId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TodoTask>> GetTasksAsync(
        ListQuery query,
        CancellationToken cancellationToken = default);

    Task<TodoTask?> GetTaskAsync(string uid, CancellationToken cancellationToken = default);

    Task<TodoTask> CreateTaskAsync(TodoTask task, CancellationToken cancellationToken = default, bool addToTop = false);

    Task UpdateTaskAsync(TodoTask task, CancellationToken cancellationToken = default, bool moveStarredToTop = false);

    Task RenameTaskAsync(string uid, string summary, CancellationToken cancellationToken = default);

    Task SetTaskTextAsync(string uid, string summary, string? description, CancellationToken cancellationToken = default);

    Task<bool> TryAddTaskAttachmentAsync(
        string uid, TaskAttachment attachment, long maxTotalBytes, CancellationToken cancellationToken = default);

    Task<double> SetTaskImportanceAsync(
        string uid, bool isImportant, bool moveStarredToTop, CancellationToken cancellationToken = default);

    Task CompleteTaskAsync(string uid, CancellationToken cancellationToken = default);

    Task UncompleteTaskAsync(string uid, CancellationToken cancellationToken = default);

    Task DeleteTaskAsync(string uid, CancellationToken cancellationToken = default);

    Task RestoreTaskAsync(string uid, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TaskUndoState>> CaptureTaskUndoAsync(
        IReadOnlyList<string> uids, TaskUndoFields fields, CancellationToken cancellationToken = default);

    Task RestoreTaskFieldsAsync(IReadOnlyList<TaskUndoState> states, CancellationToken cancellationToken = default);

    /// <summary>Commits the entire selection together and raises one store change event.</summary>
    Task ApplyTaskBatchAsync(
        IReadOnlyList<string> uids,
        TaskBatchAction action,
        CancellationToken cancellationToken = default,
        DateOnly? date = null,
        long? destinationListId = null,
        bool moveStarredToTop = false);

    Task ReorderTaskAsync(string uid, double sortOrder, CancellationToken cancellationToken = default);

    Task MoveTaskAsync(
        string uid,
        long destinationListId,
        double? sortOrder = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TodoTask>> GetChildrenAsync(
        string parentUid,
        CancellationToken cancellationToken = default);

    Task<TodoTask> AddStepAsync(
        string parentUid,
        string summary,
        CancellationToken cancellationToken = default);

    Task ReorderStepAsync(string uid, double sortOrder, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TodoTask>> SearchAsync(
        string text,
        int limit = 100,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TodoTask>> GetTasksForExportAsync(
        long listId,
        CancellationToken cancellationToken = default);

    Task<IcalImportPreview> PreviewIcalImportAsync(
        IReadOnlyList<IcalCalendar> calendars,
        CancellationToken cancellationToken = default);

    Task<IcalImportResult> ImportIcalAsync(
        IReadOnlyList<IcalCalendar> calendars,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TaskAlarm>> GetPendingAlarmsAsync(
        DateTimeOffset throughUtc,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PendingReminder>> GetPendingRemindersAsync(
        DateTimeOffset throughUtc,
        CancellationToken cancellationToken = default);

    Task<TaskAlarm> SetAlarmAsync(
        string taskUid,
        TaskAlarm alarm,
        CancellationToken cancellationToken = default);

    Task SnoozeAlarmAsync(
        string alarmUid,
        DateTimeOffset untilUtc,
        CancellationToken cancellationToken = default);

    Task AcknowledgeAlarmAsync(string alarmUid, CancellationToken cancellationToken = default);

    Task SetAlarmScheduledToastTagAsync(
        string alarmUid,
        string? scheduledToastTag,
        CancellationToken cancellationToken = default);

    Task<int> GetActiveDueTaskCountAsync(
        DateTimeOffset throughUtc,
        CancellationToken cancellationToken = default);

    Task VacuumAsync(CancellationToken cancellationToken = default);

    Task<StoreStats> GetStatsAsync(CancellationToken cancellationToken = default);
}
