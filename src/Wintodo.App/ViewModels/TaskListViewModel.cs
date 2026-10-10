using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml;
using Microsoft.Windows.ApplicationModel.Resources;
using Wintodo.Core.Models;
using Wintodo.Core.Presentation;
using Wintodo.Core.Queries;
using Wintodo.Core.Services;
using Wintodo.Services;

namespace Wintodo.ViewModels;

public sealed partial class TaskListViewModel : ObservableObject
{
    private readonly ITaskStore _taskStore;
    private readonly IClock _clock;
    private readonly TaskPreferences _preferences;
    private readonly ResourceLoader _resources = new();
    private readonly Dictionary<string, TaskItemViewModel> _itemCache =
        new(StringComparer.Ordinal);
    private readonly List<TaskItemViewModel> _activeTasks = [];
    private readonly List<TaskItemViewModel> _completedTasks = [];
    private readonly Dictionary<string, TaskListGroupViewModel> _groupCache = new(StringComparer.Ordinal);
    private IReadOnlyList<ShellListItemViewModel> _lists = [];
    private ShellListItemViewModel? _selectedList;
    private ListQuery? _currentQuery;
    private DateOnly _lastObservedToday;
    private string _searchText = string.Empty;
    private bool _completedExpanded;
    private int _loadVersion;
    private IReadOnlyList<string>? _lastDeletedTaskUids;
    private CancellationTokenSource? _undoCancellation;
    private DateTimeOffset? _composerDue;
    private DateTimeOffset? _composerReminder;
    private string? _composerRecurrence;
    private bool _composerImportant;
    private bool _composerMyDay;
    private long? _composerListOverrideId;
    private ListQueryKind _composerKind = ListQueryKind.MyDay;
    private string? _selectedNavigationKey;
    private string? _selectedBackgroundSettingKey;

    internal TaskListViewModel(ITaskStore taskStore, IClock clock, TaskPreferences preferences)
    {
        _taskStore = taskStore;
        _clock = clock;
        _preferences = preferences;
        preferences.PropertyChanged += OnPreferencesChanged;
        _lastObservedToday = clock.Today;
        Title = _resources.GetString("Shell_MyDayTitle");
        Subtitle = FormatToday();
        EmptyGlyph = "\uE706";
        EmptyTitle = _resources.GetString("Empty_MyDayTitle");
        EmptySubtitle = _resources.GetString("Empty_MyDaySubtitle");
        SortLabel = _resources.GetString("TaskList_Sort");
        ShowCompletedMenuLabel = _resources.GetString("TaskList_HideCompleted");
        ComposerDueLabel = _resources.GetString("Composer_DueDate");
        ComposerReminderLabel = _resources.GetString("Composer_RemindMe");
        ComposerRepeatLabel = _resources.GetString("Composer_Repeat");
        ListIconChoices = CreateListIconChoices(ChangeListIconCommand);
    }

    private void OnPreferencesChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(TaskPreferences.WeekStartIndex))
        {
            RebuildVisibleItems();
        }
    }

    internal event Action? NavigationDataChanged;
    internal event Action<bool>? VisibleItemsUpdating;
    internal event Action? CompletionReordering;
    internal event Action? VisibleItemsUpdated;

    internal event Action<ShellListItemViewModel>? DeleteListRequested;

    internal event Action<ShellListItemViewModel>? ExportListRequested;

    internal event Action<TaskItemViewModel>? TaskCompletionChanged;

    internal event Action<TaskItemViewModel>? DeleteTaskRequested;

    internal event Action? ShortcutsRequested;

    internal event Action<string?>? CustomListIconRequested;

    internal event Action? ListIconChanged;

    internal event Action<string, string?>? BackgroundThemeChanged;

    public ObservableCollection<TaskListEntryViewModel> VisibleItems { get; } = [];

    private readonly TaskGroupCollection _taskGroups = [];
    public IList<object> TaskGroups => _taskGroups;

    public ObservableCollection<MoveTargetViewModel> MoveTargets { get; } = [];

    // ItemsRepeater requires a WinRT vector; the object projection is AOT-safe.
    public IList<object> ListIconChoices { get; }

    [ObservableProperty]
    public partial string Title { get; set; }

    [ObservableProperty]
    public partial string Subtitle { get; set; }

    [ObservableProperty]
    public partial bool HasSubtitle { get; set; } = true;

    [ObservableProperty]
    public partial int ActiveCount { get; set; }

    [ObservableProperty]
    public partial string FixedHeaderGlyph { get; set; } = "\uE706";

    [ObservableProperty]
    public partial bool HasFixedHeaderGlyph { get; set; } = true;

    [ObservableProperty]
    public partial bool CanChangeSelectedBackground { get; set; }

    [ObservableProperty]
    public partial string? BackgroundTheme { get; set; }

    [ObservableProperty]
    public partial Brush FixedHeaderIconBrush { get; set; } =
        (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RequestExportListCommand))]
    public partial bool IsUserListSelected { get; set; }

    [ObservableProperty]
    public partial bool CanEditSelectedList { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RequestExportListCommand))]
    public partial bool IsTransferBusy { get; set; }

    [ObservableProperty]
    public partial bool IsTaskListVisible { get; set; } = true;

    [ObservableProperty]
    public partial bool IsComposerVisible { get; set; } = true;

    [ObservableProperty]
    public partial bool AreListCommandsVisible { get; set; } = true;

    [ObservableProperty]
    public partial bool CanReorderTasks { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddTaskCommand))]
    public partial bool CanCreateTask { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddTaskCommand))]
    public partial string NewTaskTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsComposerExpanded { get; set; }

    [ObservableProperty]
    public partial string ComposerDueLabel { get; set; }

    [ObservableProperty]
    public partial string ComposerReminderLabel { get; set; }

    [ObservableProperty]
    public partial string ComposerRepeatLabel { get; set; }

    [ObservableProperty]
    public partial string ComposerTargetLabel { get; set; } = string.Empty;

    [ObservableProperty]
    public partial long? ComposerTargetListId { get; set; }

    [ObservableProperty]
    public partial bool IsComposerTargetVisible { get; set; }

    [ObservableProperty]
    public partial bool HasComposerDue { get; set; }

    [ObservableProperty]
    public partial bool HasComposerReminder { get; set; }

    [ObservableProperty]
    public partial bool HasComposerRepeat { get; set; }

    [ObservableProperty]
    public partial bool IsEmpty { get; set; } = true;

    [ObservableProperty]
    public partial string EmptyGlyph { get; set; }

    [ObservableProperty]
    public partial string EmptyTitle { get; set; }

    [ObservableProperty]
    public partial string EmptySubtitle { get; set; }

    [ObservableProperty]
    public partial string SortLabel { get; set; }

    [ObservableProperty]
    public partial string ShowCompletedMenuLabel { get; set; }

    [ObservableProperty]
    public partial bool IsRenamingList { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CommitRenameListCommand))]
    public partial string RenameListName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial TaskItemViewModel? SelectedTask { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UndoDeleteCommand))]
    public partial bool IsUndoVisible { get; set; }

    [ObservableProperty]
    public partial string UndoMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string CompletionAnnouncement { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsCompactDensity { get; set; }

    internal Func<bool> AreAnimationsEnabled { get; set; } = static () => true;

    internal ListColor SelectedColor => _selectedList?.Color ?? ListColor.Blue;

    public string? SelectedIconEmoji => _selectedList?.IconEmoji;

    public bool HasSelectedIconEmoji => !string.IsNullOrEmpty(SelectedIconEmoji);

    internal TaskItemViewModel? FindTask(string uid)
    {
        return _itemCache.GetValueOrDefault(uid);
    }

    internal void RefreshFixedHeaderIconBrush()
    {
        OnPropertyChanged(nameof(FixedHeaderIconBrush));
    }

    internal void SetLists(IReadOnlyList<ShellListItemViewModel> lists)
    {
        _lists = lists;
        MoveTargets.Clear();
        foreach (ShellListItemViewModel list in lists)
        {
            MoveTargets.Add(new MoveTargetViewModel(
                list.Id,
                list.Uid,
                list.DisplayName));
        }

        ResolveComposerTarget();
    }
}
