using Wintodo.Core.Services;

namespace Wintodo.Core.Tests;

internal sealed class TestSettingsStore : ISettingsStore
{
    internal Dictionary<string, string?> Values { get; } = [];
    internal int Writes { get; private set; }
    internal bool FailNextWrite { get; set; }

    public Task<string?> GetSettingAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(Values.GetValueOrDefault(key));

    public Task<IReadOnlyDictionary<string, string>> GetSettingsAsync(IReadOnlyList<string> keys,
        CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, string>();
        foreach (string key in keys)
            if (Values.GetValueOrDefault(key) is string value) result[key] = value;
        return Task.FromResult<IReadOnlyDictionary<string, string>>(result);
    }

    public Task SetSettingsAsync(IReadOnlyDictionary<string, string> settings,
        CancellationToken cancellationToken = default)
    {
        foreach ((string key, string value) in settings) Values[key] = value;
        return Task.CompletedTask;
    }

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
