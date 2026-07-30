using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Windows.ApplicationModel.Resources;
using Wintodo.Core.Services;
using Wintodo.Services;

namespace Wintodo.ViewModels;

public sealed partial class ShellViewModel : ObservableObject
{
    private const string MyDayNavigationKey = "smart:my-day";
    private readonly ITaskStore _taskStore;
    private readonly IClock _clock;
    private readonly ShellStateService _stateService;
    private readonly ResourceLoader _resources = new();

    internal ShellViewModel(
        ITaskStore taskStore,
        IClock clock,
        ShellStateService stateService)
    {
        _taskStore = taskStore;
        _clock = clock;
        _stateService = stateService;
        SelectedTitle = _resources.GetString("Shell_MyDayTitle");
        SelectedSubtitle = FormatToday();
    }

    public ObservableCollection<ShellListItemViewModel> UserLists { get; } = [];

    [ObservableProperty]
    public partial string SelectedTitle { get; set; }

    [ObservableProperty]
    public partial string SelectedSubtitle { get; set; }

    [ObservableProperty]
    public partial bool IsMyDaySelected { get; set; } = true;

    internal async Task<string> InitializeAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Core.Models.TodoList> lists = await _taskStore.GetListsAsync(cancellationToken);
        UserLists.Clear();
        foreach (Core.Models.TodoList list in lists)
        {
            UserLists.Add(new ShellListItemViewModel
            {
                Uid = list.Uid,
                DisplayName = list.DisplayName,
            });
        }

        return await _stateService.LoadSelectedNavigationAsync(cancellationToken)
            ?? MyDayNavigationKey;
    }

    internal Task SelectAsync(
        string navigationKey,
        string title,
        bool isMyDay,
        CancellationToken cancellationToken = default)
    {
        SelectedTitle = title;
        IsMyDaySelected = isMyDay;
        SelectedSubtitle = isMyDay ? FormatToday() : string.Empty;
        return _stateService.SaveSelectedNavigationAsync(navigationKey, cancellationToken);
    }

    internal void SelectSettings()
    {
        SelectedTitle = _resources.GetString("Shell_SettingsTitle");
        SelectedSubtitle = string.Empty;
        IsMyDaySelected = false;
    }

    private string FormatToday()
    {
        return _clock.Today.ToString("D", CultureInfo.CurrentCulture);
    }
}
