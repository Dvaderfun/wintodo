using System.Globalization;
using Wintodo.Core.Services;

namespace Wintodo.Core.Tests;

public sealed class TaskPreferencesTests
{
    [Theory]
    [InlineData(null, null, null)]
    [InlineData("perhaps", "25:00", "7")]
    [InlineData("", "9:00", "-1")]
    [InlineData("false!", "09:00:00", "01")]
    public async Task MissingOrMalformedValues_UseDefaults(string? confirm, string? time, string? week)
    {
        var store = new SettingsStore();
        store.Values[ISettingsStore.ConfirmBeforeDeleteKey] = confirm;
        store.Values[ISettingsStore.DefaultReminderTimeKey] = time;
        store.Values[ISettingsStore.WeekStartKey] = week;
        store.Values[ISettingsStore.AddNewToTopKey] = "perhaps";
        var preferences = new TaskPreferences(store);
        await preferences.LoadAsync(TestContext.Current.CancellationToken);
        Assert.True(preferences.ConfirmBeforeDelete);
        Assert.Equal(TimeSpan.FromHours(9), preferences.DefaultReminderTime);
        Assert.Equal(0, preferences.WeekStartIndex);
        Assert.False(preferences.AddNewToTop);
        Assert.False(preferences.MoveStarredToTop);
        Assert.Equal(0, store.Writes);
    }

    [Fact]
    public async Task AllSettings_RoundTripAndNotify_WithInvariantTime()
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("uk-UA");
            var store = new SettingsStore();
            var preferences = new TaskPreferences(store);
            await preferences.LoadAsync(TestContext.Current.CancellationToken);
            var names = new List<string?>();
            preferences.PropertyChanged += (_, args) => names.Add(args.PropertyName);
            preferences.ConfirmBeforeDelete = false;
            preferences.DefaultReminderTime = new TimeSpan(14, 32, 0);
            preferences.WeekStartIndex = 2;
            preferences.AddNewToTop = true;
            preferences.MoveStarredToTop = true;
            await preferences.FlushAsync();
            var restored = new TaskPreferences(store);
            await restored.LoadAsync(TestContext.Current.CancellationToken);
            Assert.False(restored.ConfirmBeforeDelete);
            Assert.Equal(new TimeSpan(14, 32, 0), restored.DefaultReminderTime);
            Assert.Equal(DayOfWeek.Monday, restored.FirstDayOfWeek);
            Assert.True(restored.AddNewToTop);
            Assert.True(restored.MoveStarredToTop);
            Assert.Equal("14:32", store.Values[ISettingsStore.DefaultReminderTimeKey]);
            Assert.Equal(5, names.Count);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public async Task WeekStart_ChangesEndOfWeek_AndSystemUsesRegionalFormat()
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("uk-UA");
            var preferences = new TaskPreferences(new SettingsStore());
            await preferences.LoadAsync(TestContext.Current.CancellationToken);
            DateOnly friday = new(2026, 10, 9);
            Assert.Equal(DayOfWeek.Monday, preferences.FirstDayOfWeek);
            Assert.Equal(new DateOnly(2026, 10, 11), preferences.EndOfWeek(friday));
            preferences.WeekStartIndex = 1;
            Assert.Equal(new DateOnly(2026, 10, 10), preferences.EndOfWeek(friday));
            preferences.WeekStartIndex = 7;
            Assert.Equal(friday, preferences.EndOfWeek(friday));
            Assert.Throws<ArgumentOutOfRangeException>(() => preferences.WeekStartIndex = 8);
            Assert.Throws<ArgumentOutOfRangeException>(() => preferences.DefaultReminderTime = TimeSpan.FromDays(1));
            Assert.Throws<ArgumentOutOfRangeException>(() => preferences.DefaultReminderTime = TimeSpan.FromSeconds(1));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public async Task RapidChanges_SaveInOrder_AndReportFailures()
    {
        var store = new SettingsStore { FailNextWrite = true };
        var preferences = new TaskPreferences(store);
        await preferences.LoadAsync(TestContext.Current.CancellationToken);
        int failures = 0;
        preferences.SaveFailed += _ => failures++;
        preferences.ConfirmBeforeDelete = false;
        preferences.ConfirmBeforeDelete = true;
        preferences.ConfirmBeforeDelete = false;
        await preferences.FlushAsync();
        Assert.Equal(1, failures);
        Assert.Equal("False", store.Values[ISettingsStore.ConfirmBeforeDeleteKey]);
    }

    private sealed class SettingsStore : ISettingsStore
    {
        internal Dictionary<string, string?> Values { get; } = [];
        internal int Writes { get; private set; }
        internal bool FailNextWrite { get; set; }

        public Task<string?> GetSettingAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(Values.GetValueOrDefault(key));

        public async Task SetSettingAsync(string key, string value, CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            if (FailNextWrite)
            {
                FailNextWrite = false;
                throw new IOException("Test save failure");
            }
            Writes++;
            Values[key] = value;
        }

        public Task DeleteSettingAsync(string key, CancellationToken cancellationToken = default)
        {
            Values.Remove(key);
            return Task.CompletedTask;
        }
    }
}
