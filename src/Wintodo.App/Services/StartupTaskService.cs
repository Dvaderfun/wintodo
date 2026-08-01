using Windows.ApplicationModel;

namespace Wintodo.Services;

internal readonly record struct StartupTaskSettingState(
    bool IsEnabled,
    bool CanChange);

internal interface IStartupTaskService
{
    Task<StartupTaskSettingState> GetStateAsync();

    Task<StartupTaskSettingState> SetEnabledAsync(bool enabled);
}

internal sealed class StartupTaskService : IStartupTaskService
{
    private const string TaskId = "WintodoStartup";

    public async Task<StartupTaskSettingState> GetStateAsync()
    {
        StartupTask task = await StartupTask.GetAsync(TaskId);
        return ToSettingState(task.State);
    }

    public async Task<StartupTaskSettingState> SetEnabledAsync(bool enabled)
    {
        StartupTask task = await StartupTask.GetAsync(TaskId);
        StartupTaskState state = task.State;
        if (enabled)
        {
            if (state == StartupTaskState.Disabled)
            {
                state = await task.RequestEnableAsync();
            }
        }
        else if (state == StartupTaskState.Enabled)
        {
            task.Disable();
            state = StartupTaskState.Disabled;
        }

        return ToSettingState(state);
    }

    private static StartupTaskSettingState ToSettingState(StartupTaskState state)
    {
        return state switch
        {
            StartupTaskState.Enabled => new StartupTaskSettingState(true, true),
            StartupTaskState.Disabled => new StartupTaskSettingState(false, true),
            StartupTaskState.EnabledByPolicy => new StartupTaskSettingState(true, false),
            _ => new StartupTaskSettingState(false, false),
        };
    }
}
