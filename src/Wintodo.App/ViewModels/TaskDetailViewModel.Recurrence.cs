using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using Wintodo.Core.Recurrence;
using Wintodo.Services;

namespace Wintodo.ViewModels;

public sealed partial class TaskDetailViewModel
{
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

    [RelayCommand]
    private async Task ClearRepeatAsync()
    {
        if (_model is null)
        {
            return;
        }

        if (_model.RRule is null) return;
        var states = await CaptureUndoAsync(Core.Services.TaskUndoFields.Repeat);
        _isLoading = true;
        _model.RRule = null;
        RepeatModeIndex = -1;
        _isLoading = false;
        UpdateActionLabels();
        if (await PersistModelAsync()) RecordUndo(states, "Undo_RepeatCleared");
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
        ScheduleSave(TimeSpan.FromMilliseconds(200), writesWholeTask: true);
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

}
