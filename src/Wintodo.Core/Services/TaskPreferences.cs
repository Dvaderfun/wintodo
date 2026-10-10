using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Wintodo.Core.Services;

/// <summary>Typed task preferences shared by settings and task interactions.</summary>
public sealed class TaskPreferences(ISettingsStore settingsStore) : INotifyPropertyChanged
{
    private bool _loaded;
    private bool _confirmBeforeDelete = true;
    private TimeSpan _defaultReminderTime = TimeSpan.FromHours(9);
    private int _weekStartIndex;
    private bool _addNewToTop;
    private bool _moveStarredToTop;
    private Task _pendingSave = Task.CompletedTask;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action<Exception>? SaveFailed;

    public bool ConfirmBeforeDelete
    {
        get => _confirmBeforeDelete;
        set => Set(ref _confirmBeforeDelete, value, ISettingsStore.ConfirmBeforeDeleteKey, value.ToString());
    }

    public TimeSpan DefaultReminderTime
    {
        get => _defaultReminderTime;
        set
        {
            if (value < TimeSpan.Zero || value >= TimeSpan.FromDays(1) || value.Ticks % TimeSpan.TicksPerMinute != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            Set(ref _defaultReminderTime, value, ISettingsStore.DefaultReminderTimeKey,
                TimeOnly.FromTimeSpan(value).ToString("HH:mm", CultureInfo.InvariantCulture));
        }
    }

    /// <summary>Zero is the system setting; one through seven are Sunday through Saturday.</summary>
    public int WeekStartIndex
    {
        get => _weekStartIndex;
        set
        {
            if (value is < 0 or > 7)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            Set(ref _weekStartIndex, value, ISettingsStore.WeekStartKey,
                value == 0 ? "system" : (value - 1).ToString(CultureInfo.InvariantCulture));
        }
    }

    public DayOfWeek FirstDayOfWeek => WeekStartIndex == 0
        ? CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek
        : (DayOfWeek)(WeekStartIndex - 1);

    public DateOnly EndOfWeek(DateOnly today)
    {
        int dayIndex = ((int)today.DayOfWeek - (int)FirstDayOfWeek + 7) % 7;
        return today.AddDays(6 - dayIndex);
    }

    public bool AddNewToTop
    {
        get => _addNewToTop;
        set => Set(ref _addNewToTop, value, ISettingsStore.AddNewToTopKey, value.ToString());
    }

    public bool MoveStarredToTop
    {
        get => _moveStarredToTop;
        set => Set(ref _moveStarredToTop, value, ISettingsStore.MoveStarredToTopKey, value.ToString());
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        _loaded = false;
        ConfirmBeforeDelete = !bool.TryParse(
            await settingsStore.GetSettingAsync(ISettingsStore.ConfirmBeforeDeleteKey, cancellationToken), out bool confirm) || confirm;
        string? time = await settingsStore.GetSettingAsync(ISettingsStore.DefaultReminderTimeKey, cancellationToken);
        DefaultReminderTime = TimeOnly.TryParseExact(time, "HH:mm", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out TimeOnly parsedTime) ? parsedTime.ToTimeSpan() : TimeSpan.FromHours(9);
        string? week = await settingsStore.GetSettingAsync(ISettingsStore.WeekStartKey, cancellationToken);
        WeekStartIndex = week is { Length: 1 } && week[0] is >= '0' and <= '6' ? week[0] - '0' + 1 : 0;
        AddNewToTop = bool.TryParse(
            await settingsStore.GetSettingAsync(ISettingsStore.AddNewToTopKey, cancellationToken), out bool add) && add;
        MoveStarredToTop = bool.TryParse(
            await settingsStore.GetSettingAsync(ISettingsStore.MoveStarredToTopKey, cancellationToken), out bool move) && move;
        _loaded = true;
    }

    public Task FlushAsync() => _pendingSave;

    private void Set<T>(ref T field, T value, string key, string text, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        if (_loaded)
        {
            _pendingSave = SaveAfterAsync(_pendingSave, key, text);
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    private async Task SaveAfterAsync(Task previous, string key, string text)
    {
        await previous;
        try
        {
            await settingsStore.SetSettingAsync(key, text);
        }
        catch (Exception exception)
        {
            SaveFailed?.Invoke(exception);
        }
    }
}
