using Wintodo.Core.Services;

namespace Wintodo.Core.Tests;

public sealed class NotificationPreferencesTests
{
    [Theory]
    [InlineData(null, null, true, 0)]
    [InlineData("invalid", "90", true, 0)]
    [InlineData("false", "60", false, 1)]
    [InlineData("true", "tomorrow", true, 2)]
    public async Task StoredValues_ParseWithSafeDefaults(string? enabled, string? snooze, bool expected, int index)
    {
        var store = new TestSettingsStore();
        store.Values[ISettingsStore.NotificationsEnabledKey] = enabled;
        store.Values[ISettingsStore.DefaultSnoozeKey] = snooze;
        var preferences = new NotificationPreferences(store);
        await preferences.LoadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(expected, preferences.Enabled);
        Assert.Equal(index, preferences.DefaultSnoozeIndex);
        Assert.Equal(index switch { 1 => "60", 2 => "tomorrow", _ => "10" }, preferences.DefaultSnooze);
        Assert.Equal(0, store.Writes);
    }

    [Fact]
    public async Task RapidUpdates_PersistInOrderAndRoundTrip()
    {
        var store = new TestSettingsStore();
        var preferences = new NotificationPreferences(store);
        await preferences.LoadAsync(TestContext.Current.CancellationToken);
        int notifications = 0;
        preferences.PropertyChanged += (_, _) => notifications++;
        preferences.Enabled = false;
        preferences.DefaultSnoozeIndex = 1;
        preferences.DefaultSnoozeIndex = 2;
        await preferences.FlushAsync();
        var restored = new NotificationPreferences(store);
        await restored.LoadAsync(TestContext.Current.CancellationToken);
        Assert.False(restored.Enabled);
        Assert.Equal("tomorrow", restored.DefaultSnooze);
        Assert.Equal(3, notifications);
        Assert.Throws<ArgumentOutOfRangeException>(() => preferences.DefaultSnoozeIndex = 3);
    }
}
