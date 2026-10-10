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
    private readonly ResourceLoader _resources = new();
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private CancellationTokenSource? _saveCancellation;
    private TodoTask? _model;
    private bool _isLoading;
    private int _loadVersion;

    internal TaskDetailViewModel(ITaskStore taskStore, IClock clock)
    {
        _taskStore = taskStore;
        _clock = clock;
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

    partial void OnRepeatModeIndexChanged(int value)
    {
        if (!_isLoading)
        {
            UpdateRepeatRule();
        }
    }

    partial void OnCustomIntervalChanged(double value)
    {
        if (!_isLoading && RepeatModeIndex == 5)
        {
            UpdateRepeatRule();
        }
    }

    partial void OnCustomPeriodIndexChanged(int value)
    {
        if (!_isLoading && RepeatModeIndex == 5)
        {
            UpdateRepeatRule();
        }
    }

    partial void OnCustomSundayChanged(bool value) => OnCustomWeekdayChanged();

    partial void OnCustomMondayChanged(bool value) => OnCustomWeekdayChanged();

    partial void OnCustomTuesdayChanged(bool value) => OnCustomWeekdayChanged();

    partial void OnCustomWednesdayChanged(bool value) => OnCustomWeekdayChanged();

    partial void OnCustomThursdayChanged(bool value) => OnCustomWeekdayChanged();

    partial void OnCustomFridayChanged(bool value) => OnCustomWeekdayChanged();

    partial void OnCustomSaturdayChanged(bool value) => OnCustomWeekdayChanged();

    internal async Task LoadAsync(string uid, CancellationToken cancellationToken = default)
    {
        int loadVersion = ++_loadVersion;
        CancelPendingSave();
        Task<TodoTask?> taskRequest = _taskStore.GetTaskAsync(uid, cancellationToken);
        Task<IReadOnlyList<TodoTask>> stepsRequest =
            _taskStore.GetChildrenAsync(uid, cancellationToken);
        await Task.WhenAll(taskRequest, stepsRequest);
        if (loadVersion != _loadVersion)
        {
            return;
        }

        TodoTask task = await taskRequest
            ?? throw new KeyNotFoundException($"Task '{uid}' was not found.");
        _model = task;
        ApplyModelToEditor(task);

        var steps = new ObservableCollection<object>();
        foreach (TodoTask step in await stepsRequest)
        {
            steps.Add(CreateStepViewModel(step));
        }
        Steps = steps;

        RebuildAttachments();
        OnPropertyChanged(nameof(Uid));
    }

    internal async Task AttachFileAsync(StorageFile source)
    {
        if (_model is null)
        {
            return;
        }

        Windows.Storage.FileProperties.BasicProperties properties =
            await source.GetBasicPropertiesAsync();
        long currentSize = 0;
        foreach (TaskAttachment item in _model.Attachments)
        {
            if (item.SizeBytes < 0
                || item.SizeBytes > AttachmentLimitBytes - currentSize)
            {
                ShowError(_resources.GetString("Detail_AttachmentLimit"));
                return;
            }

            currentSize += item.SizeBytes;
        }

        ulong remainingBytes = (ulong)(AttachmentLimitBytes - currentSize);
        if (properties.Size > remainingBytes)
        {
            ShowError(_resources.GetString("Detail_AttachmentLimit"));
            return;
        }

        StorageFolder attachmentsRoot =
            await ApplicationData.Current.LocalFolder.CreateFolderAsync(
                "attachments",
                CreationCollisionOption.OpenIfExists);
        StorageFolder taskFolder = await attachmentsRoot.CreateFolderAsync(
            _model.Uid,
            CreationCollisionOption.OpenIfExists);
        StorageFile copied = await source.CopyAsync(
            taskFolder,
            source.Name,
            NameCollisionOption.GenerateUniqueName);
        Windows.Storage.FileProperties.BasicProperties copiedProperties =
            await copied.GetBasicPropertiesAsync();
        if (copiedProperties.Size > remainingBytes)
        {
            await copied.DeleteAsync(StorageDeleteOption.PermanentDelete);
            ShowError(_resources.GetString("Detail_AttachmentLimit"));
            return;
        }

        var attachment = new TaskAttachment
        {
            FileName = copied.Name,
            RelativePath = Path.Combine("attachments", _model.Uid, copied.Name),
            SizeBytes = (long)copiedProperties.Size,
            MimeType = copied.ContentType,
            Added = _clock.UtcNow,
        };
        _model.Attachments.Add(attachment);
        if (await PersistModelAsync())
        {
            RebuildAttachments();
        }
        else
        {
            _model.Attachments.Remove(attachment);
            await copied.DeleteAsync(StorageDeleteOption.PermanentDelete);
        }
    }

    internal async Task PersistStepOrderAsync(CancellationToken cancellationToken = default)
    {
        for (int index = 0; index < Steps.Count; index++)
        {
            var step = (StepItemViewModel)Steps[index];
            double sortOrder = (index + 1) * 1024d;
            step.Model.SortOrder = sortOrder;
            await _taskStore.ReorderStepAsync(step.Uid, sortOrder, cancellationToken);
        }
    }

    internal void ReportError(string message)
    {
        ShowError(message);
    }

    [RelayCommand]
    private async Task CloseAsync()
    {
        CancelPendingSave();
        if (_model is not null
            && !string.IsNullOrWhiteSpace(Summary)
            && !string.Equals(_model.Summary, Summary, StringComparison.Ordinal))
        {
            _model.Summary = Summary;
        }

        await PersistModelAsync();
        CloseRequested?.Invoke();
    }

    [RelayCommand]
    private async Task ToggleCompletionAsync()
    {
        if (_model is null)
        {
            return;
        }

        if (IsCompleted)
        {
            await _taskStore.UncompleteTaskAsync(_model.Uid);
        }
        else
        {
            await _taskStore.CompleteTaskAsync(_model.Uid);
        }

        TodoTask updated = await _taskStore.GetTaskAsync(_model.Uid)
            ?? throw new InvalidOperationException("The edited task no longer exists.");
        _model = updated;
        ApplyModelToEditor(updated);
        OnPropertyChanged(nameof(IsCompleted));
        RebuildAttachments();
        TaskChanged?.Invoke(CloneTask(updated));
    }

    [RelayCommand]
    private async Task ToggleImportanceAsync()
    {
        if (_model is null)
        {
            return;
        }

        _model.Priority = IsImportant
            ? (int)TaskPriority.Undefined
            : (int)TaskPriority.High;
        IsImportant = !IsImportant;
        await PersistModelAsync();
    }

    [RelayCommand]
    private async Task ToggleMyDayAsync()
    {
        if (_model is null)
        {
            return;
        }

        _model.MyDayDate = IsMyDay ? null : _clock.Today;
        IsMyDay = _model.MyDayDate is not null;
        UpdateActionLabels();
        await PersistModelAsync();
    }

    [RelayCommand]
    private async Task SetDueQuickAsync(string option)
    {
        if (_model is null)
        {
            return;
        }

        DateOnly? date = option switch
        {
            "Today" => _clock.Today,
            "Tomorrow" => _clock.Today.AddDays(1),
            "NextWeek" => _clock.Today.AddDays(7),
            _ => null,
        };
        if (date is null)
        {
            ShowDuePickerRequested?.Invoke();
            return;
        }

        _model.Due = StartOfLocalDay(date.Value);
        _model.DueIsDateOnly = true;
        PendingDueDate = _model.Due;
        PendingDueTime = TimeSpan.Zero;
        UpdateActionLabels();
        await PersistModelAsync();
    }

    [RelayCommand]
    private async Task ApplyCustomDueAsync()
    {
        if (_model is null || PendingDueDate is null)
        {
            return;
        }

        _model.Due = CombineLocal(PendingDueDate.Value, PendingDueTime);
        _model.DueIsDateOnly = PendingDueTime == TimeSpan.Zero;
        UpdateActionLabels();
        await PersistModelAsync();
    }

    [RelayCommand]
    private async Task ClearDueAsync()
    {
        if (_model is null)
        {
            return;
        }

        _model.Due = null;
        _model.DueIsDateOnly = false;
        PendingDueDate = null;
        PendingDueTime = TimeSpan.Zero;
        UpdateActionLabels();
        await PersistModelAsync();
    }

    [RelayCommand]
    private async Task SetReminderQuickAsync(string option)
    {
        if (_model is null)
        {
            return;
        }

        DateTimeOffset localNow = _clock.UtcNow.ToLocalTime();
        DateTimeOffset? reminder = option switch
        {
            "Today" => localNow.AddHours(3),
            "Tomorrow" => CombineLocal(
                StartOfLocalDay(_clock.Today.AddDays(1)),
                new TimeSpan(9, 0, 0)),
            "NextWeek" => CombineLocal(
                StartOfLocalDay(_clock.Today.AddDays(7)),
                new TimeSpan(9, 0, 0)),
            _ => null,
        };
        if (reminder is null)
        {
            ShowReminderPickerRequested?.Invoke();
            return;
        }

        SetReminder(reminder.Value);
        UpdateActionLabels();
        await PersistModelAsync();
    }

    [RelayCommand]
    private async Task ApplyCustomReminderAsync()
    {
        if (_model is null || PendingReminderDate is null)
        {
            return;
        }

        SetReminder(CombineLocal(PendingReminderDate.Value, PendingReminderTime));
        UpdateActionLabels();
        await PersistModelAsync();
    }

    [RelayCommand]
    private async Task ClearReminderAsync()
    {
        if (_model is null)
        {
            return;
        }

        _model.Alarms.Clear();
        PendingReminderDate = null;
        PendingReminderTime = new TimeSpan(9, 0, 0);
        UpdateActionLabels();
        await PersistModelAsync();
    }

    [RelayCommand]
    private async Task ClearRepeatAsync()
    {
        if (_model is null)
        {
            return;
        }

        _isLoading = true;
        _model.RRule = null;
        RepeatModeIndex = -1;
        _isLoading = false;
        UpdateActionLabels();
        await PersistModelAsync();
    }

    private bool CanAddStep()
    {
        return _model is not null && !string.IsNullOrWhiteSpace(NewStepTitle);
    }

    [RelayCommand(CanExecute = nameof(CanAddStep))]
    private async Task AddStepAsync()
    {
        if (_model is null || string.IsNullOrWhiteSpace(NewStepTitle))
        {
            return;
        }

        TodoTask step = await _taskStore.AddStepAsync(_model.Uid, NewStepTitle.Trim());
        Steps.Add(CreateStepViewModel(step));
        NewStepTitle = string.Empty;
        TaskChanged?.Invoke(CloneTask(_model));
    }

    [RelayCommand]
    private void RequestAddAttachment()
    {
        AddAttachmentRequested?.Invoke();
    }

    [RelayCommand]
    private async Task DeleteTaskAsync()
    {
        await Task.Yield();
        if (_model is not null)
        {
            DeleteRequested?.Invoke(Summary);
        }
    }

    internal async Task ConfirmDeleteAsync(
        CancellationToken cancellationToken = default)
    {
        if (_model is null)
        {
            return;
        }

        string uid = _model.Uid;
        await _taskStore.DeleteTaskAsync(uid, cancellationToken);
        TaskDeleted?.Invoke(uid);
        CloseRequested?.Invoke();
    }

    [RelayCommand]
    private void ClearError()
    {
        IsErrorOpen = false;
    }

    private async Task RenameStepAsync(StepItemViewModel item, string summary)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(300));
        if (string.IsNullOrWhiteSpace(summary)
            || !string.Equals(item.Summary, summary, StringComparison.Ordinal))
        {
            return;
        }

        item.Model.Summary = summary.Trim();
        await _taskStore.UpdateTaskAsync(item.Model);
    }

    private async Task ToggleStepCompletionAsync(StepItemViewModel item)
    {
        if (item.IsCompleted)
        {
            await _taskStore.UncompleteTaskAsync(item.Uid);
        }
        else
        {
            await _taskStore.CompleteTaskAsync(item.Uid);
        }

        TodoTask updated = await _taskStore.GetTaskAsync(item.Uid)
            ?? throw new InvalidOperationException("The edited step no longer exists.");
        item.Update(updated);
        TaskChanged?.Invoke(CloneTask(_model!));
    }

    private async Task DeleteStepAsync(StepItemViewModel item)
    {
        await _taskStore.DeleteTaskAsync(item.Uid);
        Steps.Remove(item);
        TaskChanged?.Invoke(CloneTask(_model!));
    }

    private async Task OpenAttachmentAsync(AttachmentItemViewModel item)
    {
        if (!TryResolveAttachmentPath(item.Model.RelativePath, out string path))
        {
            ShowError(_resources.GetString("Detail_AttachmentOpenFailed"));
            return;
        }

        try
        {
            StorageFile file = await StorageFile.GetFileFromPathAsync(path);
            if (!await Launcher.LaunchFileAsync(file))
            {
                ShowError(_resources.GetString("Detail_AttachmentOpenFailed"));
            }
        }
        catch (FileNotFoundException)
        {
            ShowError(_resources.GetString("Detail_AttachmentMissing"));
        }
    }

    private async Task RemoveAttachmentAsync(AttachmentItemViewModel item)
    {
        if (_model is null)
        {
            return;
        }

        int index = _model.Attachments.IndexOf(item.Model);
        if (index < 0)
        {
            return;
        }

        _model.Attachments.RemoveAt(index);
        if (!await PersistModelAsync())
        {
            _model.Attachments.Insert(index, item.Model);
            return;
        }

        var attachments = new List<object>(Attachments);
        attachments.Remove(item);
        Attachments = attachments;
        if (!TryResolveAttachmentPath(item.Model.RelativePath, out string path))
        {
            return;
        }

        try
        {
            StorageFile file = await StorageFile.GetFileFromPathAsync(path);
            await file.DeleteAsync(StorageDeleteOption.PermanentDelete);
        }
        catch (FileNotFoundException)
        {
        }
    }

    private static bool TryResolveAttachmentPath(
        string relativePath,
        out string resolvedPath)
    {
        try
        {
            string localRoot = ApplicationData.Current.LocalFolder.Path;
            string attachmentsRoot = Path.GetFullPath(
                Path.Combine(localRoot, "attachments"));
            string candidate = Path.GetFullPath(Path.Combine(localRoot, relativePath));
            string requiredPrefix = attachmentsRoot + Path.DirectorySeparatorChar;
            if (candidate.StartsWith(requiredPrefix, StringComparison.OrdinalIgnoreCase))
            {
                resolvedPath = candidate;
                return true;
            }
        }
        catch (Exception exception) when (
            exception is ArgumentException
            or NotSupportedException
            or PathTooLongException)
        {
        }

        resolvedPath = string.Empty;
        return false;
    }

    private StepItemViewModel CreateStepViewModel(TodoTask step)
    {
        return new StepItemViewModel(
            step,
            RenameStepAsync,
            ToggleStepCompletionAsync,
            DeleteStepAsync);
    }

    private void ApplyModelToEditor(TodoTask task)
    {
        _isLoading = true;
        Summary = task.Summary;
        Note = task.Description ?? string.Empty;
        IsCompleted = task.Status == Wintodo.Core.Models.TaskStatus.Completed;
        IsImportant = task.Priority is >= 1 and <= 4;
        IsMyDay = task.MyDayDate is not null;
        CreatedText = TaskDateLabels.FormatCreated(
            task.Created.ToLocalTime(),
            _resources.GetString("Detail_Created"),
            DisplayCulture.Current);

        PendingDueDate = task.Due?.ToLocalTime();
        PendingDueTime = task.DueIsDateOnly
            ? TimeSpan.Zero
            : task.Due?.ToLocalTime().TimeOfDay ?? TimeSpan.Zero;
        TaskAlarm? reminder = task.Alarms.FirstOrDefault(alarm => !alarm.IsAcknowledged);
        PendingReminderDate = reminder?.Trigger?.ToLocalTime();
        PendingReminderTime =
            reminder?.Trigger?.ToLocalTime().TimeOfDay ?? new TimeSpan(9, 0, 0);
        ConfigureRepeatEditor(task.RRule);
        UpdateActionLabels();
        _isLoading = false;
    }

    private void ConfigureRepeatEditor(string? rrule)
    {
        ResetCustomWeekdays();
        CustomInterval = 1;
        CustomPeriodIndex = 1;
        RepeatModeIndex = rrule switch
        {
            null or "" => -1,
            "FREQ=DAILY" => 0,
            "FREQ=WEEKLY;BYDAY=MO,TU,WE,TH,FR" => 1,
            "FREQ=WEEKLY" => 2,
            "FREQ=MONTHLY" => 3,
            "FREQ=YEARLY" => 4,
            _ => 5,
        };
        if (RepeatModeIndex != 5 || string.IsNullOrWhiteSpace(rrule))
        {
            return;
        }

        Dictionary<string, string> parts = rrule
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .Where(part => part.Length == 2)
            .ToDictionary(part => part[0], part => part[1], StringComparer.Ordinal);
        if (parts.TryGetValue("INTERVAL", out string? interval)
            && double.TryParse(interval, NumberStyles.Integer, CultureInfo.InvariantCulture, out double value))
        {
            CustomInterval = Math.Clamp(value, 1, 999);
        }

        CustomPeriodIndex = parts.GetValueOrDefault("FREQ") switch
        {
            "DAILY" => 0,
            "MONTHLY" => 2,
            "YEARLY" => 3,
            _ => 1,
        };
        if (parts.TryGetValue("BYDAY", out string? byDay))
        {
            HashSet<string> days = byDay.Split(',').ToHashSet(StringComparer.Ordinal);
            CustomSunday = days.Contains("SU");
            CustomMonday = days.Contains("MO");
            CustomTuesday = days.Contains("TU");
            CustomWednesday = days.Contains("WE");
            CustomThursday = days.Contains("TH");
            CustomFriday = days.Contains("FR");
            CustomSaturday = days.Contains("SA");
        }
    }

    private void UpdateRepeatRule()
    {
        if (_model is null)
        {
            return;
        }

        _model.RRule = RepeatModeIndex switch
        {
            0 => "FREQ=DAILY",
            1 => "FREQ=WEEKLY;BYDAY=MO,TU,WE,TH,FR",
            2 => "FREQ=WEEKLY",
            3 => "FREQ=MONTHLY",
            4 => "FREQ=YEARLY",
            5 => BuildCustomRepeatRule(),
            _ => null,
        };
        UpdateActionLabels();
        ScheduleSave(TimeSpan.FromMilliseconds(200));
    }

    private string BuildCustomRepeatRule()
    {
        int interval = Math.Clamp((int)Math.Round(CustomInterval), 1, 999);
        string frequency = CustomPeriodIndex switch
        {
            0 => "DAILY",
            2 => "MONTHLY",
            3 => "YEARLY",
            _ => "WEEKLY",
        };
        string rule = $"FREQ={frequency};INTERVAL={interval}";
        if (frequency != "WEEKLY")
        {
            return rule;
        }

        bool[] selectedDays =
        [
            CustomSunday,
            CustomMonday,
            CustomTuesday,
            CustomWednesday,
            CustomThursday,
            CustomFriday,
            CustomSaturday,
        ];
        string byDay = string.Join(
            ',',
            WeekdayCodes.Where((_, index) => selectedDays[index]));
        return string.IsNullOrEmpty(byDay) ? rule : $"{rule};BYDAY={byDay}";
    }

    private void OnCustomWeekdayChanged()
    {
        if (!_isLoading && RepeatModeIndex == 5)
        {
            UpdateRepeatRule();
        }
    }

    private void UpdateActionLabels()
    {
        if (_model is null)
        {
            return;
        }

        MyDayLabel = _model.MyDayDate is null
            ? _resources.GetString("Detail_AddToMyDay")
            : _resources.GetString("Detail_AddedToMyDay");
        DueLabel = _model.Due is null
            ? _resources.GetString("Detail_AddDueDate")
            : string.Format(
                DisplayCulture.Current,
                _resources.GetString("Detail_Due"),
                FormatDate(_model.Due.Value));

        TaskAlarm? reminder = _model.Alarms.FirstOrDefault(alarm => !alarm.IsAcknowledged);
        ReminderLabel = reminder?.Trigger is null
            ? _resources.GetString("Detail_RemindMe")
            : string.Format(
                DisplayCulture.Current,
                _resources.GetString("Detail_Reminder"),
                reminder.Trigger.Value.ToLocalTime());
        RepeatLabel = FormatRepeat(_model.RRule);
    }

    private string FormatRepeat(string? rrule)
    {
        if (string.IsNullOrWhiteSpace(rrule))
        {
            return _resources.GetString("Detail_Repeat");
        }

        string? knownLabel = rrule switch
        {
            "FREQ=DAILY" => _resources.GetString("Detail_RepeatsDaily"),
            "FREQ=WEEKLY;BYDAY=MO,TU,WE,TH,FR" =>
                _resources.GetString("Detail_RepeatsWeekdays"),
            "FREQ=WEEKLY" => _resources.GetString("Detail_RepeatsWeekly"),
            "FREQ=MONTHLY" => _resources.GetString("Detail_RepeatsMonthly"),
            "FREQ=YEARLY" => _resources.GetString("Detail_RepeatsYearly"),
            _ => null,
        };
        if (knownLabel is not null)
        {
            return knownLabel;
        }

        try
        {
            RecurrenceRule rule = RecurrenceRule.Parse(rrule);
            string period = rule.Frequency switch
            {
                RecurrenceFrequency.Daily => _resources.GetString("Detail_Days"),
                RecurrenceFrequency.Monthly => _resources.GetString("Detail_Months"),
                RecurrenceFrequency.Yearly => _resources.GetString("Detail_Years"),
                _ => _resources.GetString("Detail_Weeks"),
            };
            string daySuffix = rule.ByDay.Count == 0
                ? string.Empty
                : string.Format(
                    DisplayCulture.Current,
                    _resources.GetString("Detail_OnDays"),
                    string.Join(
                        ", ",
                        rule.ByDay.Select(day =>
                            DisplayCulture.Current.DateTimeFormat
                                .GetAbbreviatedDayName(day.Day))));
            return string.Format(
                DisplayCulture.Current,
                _resources.GetString("Detail_RepeatsEvery"),
                rule.Interval,
                period,
                daySuffix);
        }
        catch (FormatException)
        {
            return string.Format(
                DisplayCulture.Current,
                _resources.GetString("Detail_RepeatsCustom"),
                rrule);
        }
    }

    private string FormatDate(DateTimeOffset value)
    {
        DateOnly date = DateOnly.FromDateTime(value.LocalDateTime);
        return TaskDateLabels.FormatDetailDue(
            date,
            _clock,
            DisplayCulture.Current,
            _resources.GetString("Task_Metadata_Today"),
            _resources.GetString("Task_Metadata_Tomorrow"),
            _resources.GetString("Detail_NextWeek"));
    }

    private void SetReminder(DateTimeOffset value)
    {
        _model!.Alarms.Clear();
        _model.Alarms.Add(new TaskAlarm
        {
            Action = AlarmAction.Display,
            Trigger = value,
            Description = _model.Summary,
        });
        PendingReminderDate = value;
        PendingReminderTime = value.TimeOfDay;
    }

    private void RebuildAttachments()
    {
        if (_model is null)
        {
            Attachments = new List<object>();
            return;
        }

        var attachments = new List<object>(_model.Attachments.Count);
        foreach (TaskAttachment attachment in _model.Attachments)
        {
            attachments.Add(new AttachmentItemViewModel(
                attachment,
                OpenAttachmentAsync,
                RemoveAttachmentAsync));
        }
        Attachments = attachments;
    }

    private void ScheduleSave(TimeSpan delay)
    {
        _saveCancellation?.Cancel();
        _saveCancellation?.Dispose();
        _saveCancellation = new CancellationTokenSource();
        _ = SaveAfterDelayAsync(delay, _saveCancellation.Token);
    }

    private async Task SaveAfterDelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken);
            await PersistModelAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task<bool> PersistModelAsync(CancellationToken cancellationToken = default)
    {
        TodoTask? current = _model;
        if (current is null || string.IsNullOrWhiteSpace(current.Summary))
        {
            return false;
        }

        await _saveGate.WaitAsync(cancellationToken);
        try
        {
            TodoTask snapshot = CloneTask(current);
            snapshot.Summary = snapshot.Summary.Trim();
            await _taskStore.UpdateTaskAsync(snapshot, cancellationToken);
            current.Sequence = snapshot.Sequence;
            current.LastModified = snapshot.LastModified;
            CopyCollectionIdentities(snapshot, current);
            TaskChanged?.Invoke(CloneTask(current));
            IsErrorOpen = false;
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"WINTODO_TASK_SAVE_EXCEPTION={exception}");
            ShowError(_resources.GetString("Detail_SaveFailed"));
            return false;
        }
        finally
        {
            _saveGate.Release();
        }
    }

    private void CancelPendingSave()
    {
        _saveCancellation?.Cancel();
        _saveCancellation?.Dispose();
        _saveCancellation = null;
    }

    private void ShowError(string message)
    {
        ErrorMessage = message;
        IsErrorOpen = true;
    }

    private void ResetCustomWeekdays()
    {
        CustomSunday = false;
        CustomMonday = false;
        CustomTuesday = false;
        CustomWednesday = false;
        CustomThursday = false;
        CustomFriday = false;
        CustomSaturday = false;
    }

    private static DateTimeOffset StartOfLocalDay(DateOnly date)
    {
        DateTime local = date.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
    }

    private static DateTimeOffset CombineLocal(DateTimeOffset date, TimeSpan time)
    {
        DateTime local = date.LocalDateTime.Date + time;
        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
    }

    private static TodoTask CloneTask(TodoTask source)
    {
        var clone = new TodoTask
        {
            Id = source.Id,
            Uid = source.Uid,
            ListId = source.ListId,
            ParentUid = source.ParentUid,
            Summary = source.Summary,
            Description = source.Description,
            Status = source.Status,
            PercentComplete = source.PercentComplete,
            Priority = source.Priority,
            Due = source.Due,
            DueIsDateOnly = source.DueIsDateOnly,
            DueTzid = source.DueTzid,
            Start = source.Start,
            StartIsDateOnly = source.StartIsDateOnly,
            CompletedAt = source.CompletedAt,
            Created = source.Created,
            LastModified = source.LastModified,
            Sequence = source.Sequence,
            RRule = source.RRule,
            MyDayDate = source.MyDayDate,
            SortOrder = source.SortOrder,
            Location = source.Location,
            Url = source.Url,
            Class = source.Class,
            IcalUnknown = source.IcalUnknown,
            RemoteHref = source.RemoteHref,
            RemoteETag = source.RemoteETag,
            IsDirty = source.IsDirty,
            IsDeleted = source.IsDeleted,
        };
        clone.Categories.AddRange(source.Categories);
        clone.Alarms.AddRange(source.Alarms.Select(alarm => new TaskAlarm
        {
            Id = alarm.Id,
            TaskId = alarm.TaskId,
            Uid = alarm.Uid,
            Action = alarm.Action,
            Trigger = alarm.Trigger,
            TriggerOffsetSeconds = alarm.TriggerOffsetSeconds,
            TriggerRelated = alarm.TriggerRelated,
            Description = alarm.Description,
            SnoozedUntil = alarm.SnoozedUntil,
            IsAcknowledged = alarm.IsAcknowledged,
            ScheduledToastTag = alarm.ScheduledToastTag,
        }));
        clone.Attachments.AddRange(source.Attachments.Select(attachment => new TaskAttachment
        {
            Id = attachment.Id,
            TaskId = attachment.TaskId,
            FileName = attachment.FileName,
            RelativePath = attachment.RelativePath,
            SizeBytes = attachment.SizeBytes,
            MimeType = attachment.MimeType,
            Added = attachment.Added,
        }));
        return clone;
    }

    private static void CopyCollectionIdentities(TodoTask source, TodoTask destination)
    {
        foreach (TaskAlarm destinationAlarm in destination.Alarms)
        {
            TaskAlarm? sourceAlarm = string.IsNullOrWhiteSpace(destinationAlarm.Uid)
                ? source.Alarms.FirstOrDefault(alarm =>
                    alarm.Trigger == destinationAlarm.Trigger
                    && alarm.TriggerOffsetSeconds == destinationAlarm.TriggerOffsetSeconds)
                : source.Alarms.FirstOrDefault(alarm => string.Equals(
                    alarm.Uid,
                    destinationAlarm.Uid,
                    StringComparison.Ordinal));
            if (sourceAlarm is null)
            {
                continue;
            }

            destinationAlarm.Id = sourceAlarm.Id;
            destinationAlarm.TaskId = sourceAlarm.TaskId;
            destinationAlarm.Uid = sourceAlarm.Uid;
        }

        foreach (TaskAttachment destinationAttachment in destination.Attachments)
        {
            TaskAttachment? sourceAttachment = source.Attachments.FirstOrDefault(
                attachment => string.Equals(
                    attachment.RelativePath,
                    destinationAttachment.RelativePath,
                    StringComparison.Ordinal));
            if (sourceAttachment is null)
            {
                continue;
            }

            destinationAttachment.Id = sourceAttachment.Id;
            destinationAttachment.TaskId = sourceAttachment.TaskId;
            destinationAttachment.Added = sourceAttachment.Added;
        }
    }
}
