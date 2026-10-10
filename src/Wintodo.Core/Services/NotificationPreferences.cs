using System.ComponentModel;

namespace Wintodo.Core.Services;

/// <summary>Notification preferences shared by Settings and toast scheduling.</summary>
public sealed class NotificationPreferences(ISettingsStore settingsStore) : INotifyPropertyChanged
{
    private bool _loaded;
    private bool _enabled = true;
    private int _defaultSnoozeIndex;
    private Task _pendingSave = Task.CompletedTask;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action<Exception>? SaveFailed;

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value)
            {
                return;
            }

            _enabled = value;
            Save(ISettingsStore.NotificationsEnabledKey, value.ToString());
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Enabled)));
        }
    }

    public int DefaultSnoozeIndex
    {
        get => _defaultSnoozeIndex;
        set
        {
            if (value is < 0 or > 2)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
            if (_defaultSnoozeIndex == value)
            {
                return;
            }

            _defaultSnoozeIndex = value;
            Save(ISettingsStore.DefaultSnoozeKey, DefaultSnooze);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DefaultSnoozeIndex)));
        }
    }

    public string DefaultSnooze => DefaultSnoozeIndex switch
    {
        1 => "60",
        2 => "tomorrow",
        _ => "10",
    };

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        _loaded = false;
        Enabled = !bool.TryParse(await settingsStore.GetSettingAsync(
            ISettingsStore.NotificationsEnabledKey, cancellationToken), out bool enabled) || enabled;
        DefaultSnoozeIndex = await settingsStore.GetSettingAsync(
            ISettingsStore.DefaultSnoozeKey, cancellationToken) switch
        {
            "60" => 1,
            "tomorrow" => 2,
            _ => 0,
        };
        _loaded = true;
    }

    public Task FlushAsync() => _pendingSave;

    private void Save(string key, string value)
    {
        if (_loaded)
        {
            _pendingSave = SaveAfterAsync(_pendingSave, key, value);
        }
    }

    private async Task SaveAfterAsync(Task previous, string key, string value)
    {
        await previous;
        try
        {
            await settingsStore.SetSettingAsync(key, value);
        }
        catch (Exception exception)
        {
            SaveFailed?.Invoke(exception);
        }
    }
}
