namespace Wintodo.Core.Services;

public interface ISettingsStore
{
    Task<string?> GetSettingAsync(
        string key,
        CancellationToken cancellationToken = default);

    Task SetSettingAsync(
        string key,
        string value,
        CancellationToken cancellationToken = default);

    Task DeleteSettingAsync(
        string key,
        CancellationToken cancellationToken = default);
}
