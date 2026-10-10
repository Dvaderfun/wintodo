using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Wintodo.Core.Models;
using Wintodo.Services;

namespace Wintodo.ViewModels;

public sealed partial class TaskListViewModel
{
    [RelayCommand]
    private void BeginRenameList()
    {
        if (_selectedList is null or { IsDefault: true })
        {
            return;
        }

        RenameListName = _selectedList.DisplayName;
        IsRenamingList = true;
    }

    private bool CanCommitRenameList()
    {
        return _selectedList is { IsDefault: false }
            && !string.IsNullOrWhiteSpace(RenameListName);
    }

    [RelayCommand(CanExecute = nameof(CanCommitRenameList))]
    private async Task CommitRenameListAsync()
    {
        await Task.Yield();
        ShellListItemViewModel? list = _selectedList;
        if (list is null)
        {
            return;
        }

        string displayName = RenameListName.Trim();
        await _taskStore.RenameListAsync(list.Id, displayName);
        list.DisplayName = displayName;
        Title = displayName;
        IsRenamingList = false;
        SetLists(_lists);
        NavigationDataChanged?.Invoke();
    }

    [RelayCommand]
    private void CancelRenameList()
    {
        IsRenamingList = false;
    }

    partial void OnIsUserListSelectedChanged(bool value)
    {
        if (!value)
        {
            CanEditSelectedList = false;
        }
    }

    [RelayCommand]
    private void RequestDeleteList()
    {
        if (_selectedList is { IsDefault: false })
        {
            DeleteListRequested?.Invoke(_selectedList);
        }
    }

    private bool CanRequestExportList()
    {
        return _selectedList is not null && IsUserListSelected && !IsTransferBusy;
    }

    [RelayCommand(CanExecute = nameof(CanRequestExportList))]
    private void RequestExportList()
    {
        if (_selectedList is not null)
        {
            ExportListRequested?.Invoke(_selectedList);
        }
    }

    [RelayCommand]
    private async Task ChangeListColorAsync(string value)
    {
        await Task.Yield();
        ShellListItemViewModel? list = _selectedList;
        if (list is null
            || !Enum.TryParse(value, ignoreCase: false, out ListColor color))
        {
            return;
        }

        await _taskStore.SetListColorAsync(list.Id, color);
        list.Color = color;
        OnPropertyChanged(nameof(SelectedColor));
        NavigationDataChanged?.Invoke();
    }

    [RelayCommand]
    private void RequestCustomListIcon()
    {
        if (_selectedList is not { IsDefault: false } list)
        {
            return;
        }

        CustomListIconRequested?.Invoke(list.IconEmoji);
    }

    [RelayCommand]
    private async Task ChangeListIconAsync(string? value)
    {
        if (!ListEmoji.TryNormalize(value, out string? normalized))
        {
            return;
        }

        await SetSelectedListIconAsync(normalized);
    }

    internal async Task SetSelectedListIconAsync(string? iconEmoji)
    {
        ShellListItemViewModel? list = _selectedList;
        if (list is null || list.IsDefault)
        {
            return;
        }

        await _taskStore.SetListIconEmojiAsync(list.Id, iconEmoji);
        list.IconEmoji = iconEmoji;
        NotifySelectedListIconChanged();
        ListIconChanged?.Invoke();
        NavigationDataChanged?.Invoke();
    }

    private void NotifySelectedListIconChanged()
    {
        OnPropertyChanged(nameof(SelectedIconEmoji));
        OnPropertyChanged(nameof(HasSelectedIconEmoji));
    }

    [RelayCommand]
    private async Task ChangeListBackgroundAsync(string value)
    {
        await Task.Yield();
        ShellListItemViewModel? list = _selectedList;
        string? theme = string.Equals(value, "None", StringComparison.Ordinal)
            ? null
            : value;
        if (_selectedNavigationKey is null || !IsKnownBackgroundTheme(theme))
        {
            return;
        }

        if (list is not null)
        {
            await _taskStore.SetListBackgroundThemeAsync(list.Id, theme);
            list.BackgroundTheme = theme;
        }
        else if (_selectedBackgroundSettingKey is not null)
        {
            await _taskStore.SetSettingAsync(
                _selectedBackgroundSettingKey,
                theme ?? string.Empty);
        }
        else
        {
            return;
        }

        BackgroundTheme = theme;
        BackgroundThemeChanged?.Invoke(_selectedNavigationKey, theme);
    }

    [RelayCommand]
    private void RequestShortcuts()
    {
        ShortcutsRequested?.Invoke();
    }

    [RelayCommand]
    private async Task SortByAsync(string value)
    {
        await Task.Yield();
        if (!Enum.TryParse(value, ignoreCase: false, out SortMode sortMode))
        {
            return;
        }

        if (_selectedList is not null)
        {
            _selectedList.SortMode = sortMode;
            await _taskStore.SetListSortAsync(
                _selectedList.Id,
                sortMode,
                _selectedList.SortDescending);
            SortLabel = FormatSortLabel(sortMode, _selectedList.SortDescending);
            CanReorderTasks = sortMode == SortMode.Manual;
        }

        if (_currentQuery is not null)
        {
            _currentQuery.SortMode = sortMode;
            SortLabel = FormatSortLabel(sortMode, _currentQuery.SortDescending);
            await ReloadAsync();
        }
    }

    [RelayCommand]
    private async Task ToggleSortDirectionAsync()
    {
        await Task.Yield();
        if (_currentQuery is null)
        {
            return;
        }

        _currentQuery.SortDescending = !_currentQuery.SortDescending;
        if (_selectedList is not null)
        {
            _selectedList.SortDescending = _currentQuery.SortDescending;
            await _taskStore.SetListSortAsync(
                _selectedList.Id,
                _selectedList.SortMode,
                _selectedList.SortDescending);
            SortLabel = FormatSortLabel(
                _selectedList.SortMode,
                _selectedList.SortDescending);
        }
        else
        {
            SortLabel = FormatSortLabel(
                _currentQuery.SortMode,
                _currentQuery.SortDescending);
        }

        await ReloadAsync();
    }

    [RelayCommand]
    private async Task ToggleShowCompletedAsync()
    {
        await Task.Yield();
        if (_selectedList is null)
        {
            return;
        }

        _selectedList.ShowCompleted = !_selectedList.ShowCompleted;
        await _taskStore.SetListShowCompletedAsync(
            _selectedList.Id,
            _selectedList.ShowCompleted);
        ShowCompletedMenuLabel = _selectedList.ShowCompleted
            ? _resources.GetString("TaskList_HideCompleted")
            : _resources.GetString("TaskList_ShowCompleted");
        RebuildVisibleItems();
    }

    private async Task ToggleCompletedAsync()
    {
        await Task.Yield();
        bool expanded = !_completedExpanded;
        _completedExpanded = expanded;
        if (_selectedList is not null)
        {
            _selectedList.CompletedExpanded = expanded;
            await _taskStore.SetListCompletedExpandedAsync(
                _selectedList.Id,
                expanded);
        }

        RebuildVisibleItems();
    }

    private static bool IsKnownBackgroundTheme(string? value)
    {
        return value is null
            or "Coast"
            or "Forest"
            or "Mist"
            or "Sunrise"
            or "Indigo"
            or "Petals"
            or "Graphite"
            or "Field";
    }

    private IList<object> CreateListIconChoices(
        ICommand selectCommand)
    {
        (string Emoji, string ResourceKey)[] choices =
        [
            ("\U0001F686", "ListIconChoice_Train"),
            ("\U0001F3E2", "ListIconChoice_Office"),
            ("\U0001F3E5", "ListIconChoice_Hospital"),
            ("\U0001F527", "ListIconChoice_Tools"),
            ("\U0001F5A5\uFE0F", "ListIconChoice_Computer"),
            ("\U0001F4BC", "ListIconChoice_Briefcase"),
            ("\U0001F4C1", "ListIconChoice_Folder"),
            ("\U0001F4DE", "ListIconChoice_Phone"),
            ("\U0001F512", "ListIconChoice_Lock"),
            ("\U0001F37F", "ListIconChoice_Popcorn"),
            ("\u2615", "ListIconChoice_Coffee"),
            ("\U0001F377", "ListIconChoice_Wine"),
            ("\U0001F355", "ListIconChoice_Pizza"),
            ("\U0001F4E6", "ListIconChoice_Package"),
            ("\U0001F34C", "ListIconChoice_Banana"),
            ("\U0001F4DA", "ListIconChoice_Books"),
            ("\U0001F4F7", "ListIconChoice_Camera"),
            ("\U0001F388", "ListIconChoice_Balloon"),
            ("\U0001F4A1", "ListIconChoice_LightBulb"),
            ("\U0001F6D2", "ListIconChoice_ShoppingCart"),
            ("\U0001F392", "ListIconChoice_Backpack"),
            ("\U0001F3D5\uFE0F", "ListIconChoice_Camping"),
            ("\U0001F525", "ListIconChoice_Fire"),
            ("\u2600\uFE0F", "ListIconChoice_Sun"),
            ("\U0001F389", "ListIconChoice_Celebration"),
            ("\u2764\uFE0F", "ListIconChoice_Heart"),
            ("\U0001F495", "ListIconChoice_Hearts"),
            ("\U0001F46A", "ListIconChoice_Family"),
            ("\U0001F919", "ListIconChoice_CallMe"),
            ("\u2708\uFE0F", "ListIconChoice_Airplane"),
            ("\U0001F3AF", "ListIconChoice_Target"),
            ("\U0001F3AE", "ListIconChoice_Game"),
            ("\U0001F3E0", "ListIconChoice_Home"),
            ("\U0001F4DD", "ListIconChoice_Note"),
            ("\U0001F4B0", "ListIconChoice_Money"),
            ("\U0001F381", "ListIconChoice_Gift"),
        ];
        var result = new List<object>(choices.Length);
        for (int index = 0; index < choices.Length; index++)
        {
            (string emoji, string resourceKey) = choices[index];
            result.Add(new ListEmojiChoiceViewModel(
                emoji,
                _resources.GetString(resourceKey),
                $"Todo.List.Icon.Choice.{(index + 1).ToString("00", CultureInfo.InvariantCulture)}",
                selectCommand));
        }

        return result;
    }

    private string FormatSortLabel(SortMode sortMode, bool descending)
    {
        string mode = _resources.GetString($"Sort_{sortMode}");
        string direction = descending
            ? _resources.GetString("Sort_Descending")
            : _resources.GetString("Sort_Ascending");
        return string.Format(
            DisplayCulture.Current,
            _resources.GetString("Sort_Label"),
            mode,
            direction);
    }

}
