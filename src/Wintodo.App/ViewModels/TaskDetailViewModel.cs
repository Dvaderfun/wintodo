using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Windows.ApplicationModel.Resources;
using Windows.Storage;
using Windows.System;
using Wintodo.Core.Models;
using Wintodo.Core.Presentation;
using Wintodo.Core.Recurrence;
using Wintodo.Core.Services;
using Wintodo.Services;

namespace Wintodo.ViewModels;

public sealed partial class TaskDetailViewModel : ObservableObject
{
    private const long AttachmentLimitBytes = 25L * 1024L * 1024L;
    private static readonly string[] WeekdayCodes =
        ["SU", "MO", "TU", "WE", "TH", "FR", "SA"];

    private readonly ITaskStore _taskStore;
    private readonly IClock _clock;
    private readonly TaskPreferences _preferences;
    private readonly UndoService _undo;
    private readonly ResourceLoader _resources = new();
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private readonly DetailSaveQueue _textSaves = new();
    private TodoTask? _model;
    private bool _isLoading;
    private int _loadVersion;

    internal TaskDetailViewModel(ITaskStore taskStore, IClock clock, TaskPreferences preferences, UndoService undo)
    {
        _taskStore = taskStore;
        _clock = clock;
        _preferences = preferences;
        _undo = undo;
        preferences.PropertyChanged += OnPreferencesChanged;
        MyDayLabel = _resources.GetString("Detail_AddToMyDay");
        ReminderLabel = _resources.GetString("Detail_RemindMe");
        DueLabel = _resources.GetString("Detail_AddDueDate");
        RepeatLabel = _resources.GetString("Detail_Repeat");
        CustomPeriodOptions =
        [
            _resources.GetString("Detail_Days"),
            _resources.GetString("Detail_Weeks"),
            _resources.GetString("Detail_Months"),
            _resources.GetString("Detail_Years"),
        ];
        RepeatOptions =
        [
            _resources.GetString("Detail_Daily"),
            _resources.GetString("Detail_Weekdays"),
            _resources.GetString("Detail_Weekly"),
            _resources.GetString("Detail_Monthly"),
            _resources.GetString("Detail_Yearly"),
            _resources.GetString("Detail_Custom"),
        ];
    }

    public DayOfWeek FirstDayOfWeek => _preferences.FirstDayOfWeek;

    private void OnPreferencesChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(TaskPreferences.WeekStartIndex))
        {
            OnPropertyChanged(nameof(FirstDayOfWeek));
        }
        if (args.PropertyName == nameof(TaskPreferences.DefaultReminderTime) && !HasReminder)
        {
            PendingReminderTime = _preferences.DefaultReminderTime;
        }
    }

    internal event Action? CloseRequested;

    internal event Action<TodoTask>? TaskChanged;

    internal event Action<string>? TaskDeleted;

    internal event Action<string>? DeleteRequested;

    internal event Action? ShowDuePickerRequested;

    internal event Action? ShowReminderPickerRequested;

    internal event Action? AddAttachmentRequested;

    [ObservableProperty]
    public partial ObservableCollection<object> Steps { get; set; } = [];

    [ObservableProperty]
    public partial IList<object> Attachments { get; set; } = new List<object>();

    public IList<object> CustomPeriodOptions { get; }

    public IList<object> RepeatOptions { get; }

    public string Uid => _model?.Uid ?? string.Empty;

    [ObservableProperty]
    public partial string Summary { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Note { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsCompleted { get; set; }

    [ObservableProperty]
    public partial bool IsImportant { get; set; }

    [ObservableProperty]
    public partial bool IsMyDay { get; set; }

    [ObservableProperty]
    public partial string MyDayLabel { get; set; }

    [ObservableProperty]
    public partial string ReminderLabel { get; set; }

    [ObservableProperty]
    public partial string DueLabel { get; set; }

    [ObservableProperty]
    public partial string RepeatLabel { get; set; }

    [ObservableProperty]
    public partial string CreatedText { get; set; } = string.Empty;

    public bool HasDue => PendingDueDate is not null;

    public bool HasReminder => PendingReminderDate is not null;

    public bool HasRepeat => RepeatModeIndex > 0;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddStepCommand))]
    public partial string NewStepTitle { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDue))]
    public partial DateTimeOffset? PendingDueDate { get; set; }

    [ObservableProperty]
    public partial TimeSpan PendingDueTime { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReminder))]
    public partial DateTimeOffset? PendingReminderDate { get; set; }

    [ObservableProperty]
    public partial TimeSpan PendingReminderTime { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRepeat))]
    public partial int RepeatModeIndex { get; set; }

    [ObservableProperty]
    public partial double CustomInterval { get; set; } = 1;

    [ObservableProperty]
    public partial int CustomPeriodIndex { get; set; } = 1;

    [ObservableProperty]
    public partial bool CustomSunday { get; set; }

    [ObservableProperty]
    public partial bool CustomMonday { get; set; }

    [ObservableProperty]
    public partial bool CustomTuesday { get; set; }

    [ObservableProperty]
    public partial bool CustomWednesday { get; set; }

    [ObservableProperty]
    public partial bool CustomThursday { get; set; }

    [ObservableProperty]
    public partial bool CustomFriday { get; set; }

    [ObservableProperty]
    public partial bool CustomSaturday { get; set; }

    [ObservableProperty]
    public partial bool IsErrorOpen { get; set; }

    [ObservableProperty]
    public partial string ErrorMessage { get; set; } = string.Empty;

    partial void OnSummaryChanged(string value)
    {
        if (_isLoading || _model is null || string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        _model.Summary = value;
        ScheduleSave(TimeSpan.FromMilliseconds(300));
    }

    partial void OnNoteChanged(string value)
    {
        if (_isLoading || _model is null)
        {
            return;
        }

        _model.Description = string.IsNullOrEmpty(value) ? null : value;
        ScheduleSave(TimeSpan.FromMilliseconds(500));
    }
}
