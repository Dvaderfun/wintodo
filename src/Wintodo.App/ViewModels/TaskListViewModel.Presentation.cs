using System.Collections.ObjectModel;
using Wintodo.Core.Models;
using Wintodo.Core.Presentation;
using Wintodo.Core.Queries;
using Wintodo.Services;

namespace Wintodo.ViewModels;

public sealed partial class TaskListViewModel
{
    private void RebuildVisibleItems(bool preserveViewport = true, bool viewportAlreadyCaptured = false)
    {
        IReadOnlyList<TaskGroup> groups = TaskGrouping.Build(
            _currentQuery,
            _activeTasks.Select(item => item.Model).ToArray(),
            _completedTasks.Select(item => item.Model).ToArray(),
            _selectedList?.ShowCompleted ?? true,
            _searchText,
            _clock,
            _preferences.FirstDayOfWeek,
            _lists.Select(list => new TaskGroupList(list.Id, list.Uid, list.SortOrder)).ToArray(),
            _completedExpanded);
        if (!viewportAlreadyCaptured) VisibleItemsUpdating?.Invoke(preserveViewport);
        try
        {
            var targetGroups = new List<object>(groups.Count);
            var targetItems = new List<TaskListEntryViewModel>();
            foreach (TaskGroup descriptor in groups)
            {
                string key = $"{descriptor.Kind}:{descriptor.Key}";
                if (!_groupCache.TryGetValue(key, out TaskListGroupViewModel? group))
                {
                    group = CreateTaskGroup(descriptor);
                    _groupCache.Add(key, group);
                }
                group.HeaderCount = descriptor.Count;
                group.IsExpanded = descriptor.IsExpanded;
                if (descriptor.Kind == TaskGroupKind.Completed)
                {
                    group.AccessibleName = string.Format(DisplayCulture.Current,
                        _resources.GetString("CompletedHeader_AccessibleName"), descriptor.Count);
                }
                else if (descriptor.Kind == TaskGroupKind.Search)
                {
                    group.Title = FindListName(descriptor.ListId!.Value) ?? string.Empty;
                }
                ApplySequence(group.Items,
                    descriptor.TaskUids.Select(uid => (object)_itemCache[uid]).ToArray());
                targetGroups.Add(group.Items);
                foreach (string uid in descriptor.TaskUids) targetItems.Add(_itemCache[uid]);
            }
            ApplySequence(_taskGroups, targetGroups);
            ApplySequence(VisibleItems, targetItems);
            int activeCount = _activeTasks.Count;
            bool activeCountChanged = ActiveCount != activeCount;
            ActiveCount = activeCount;
            IsEmpty = _hasLoadedRows && _activeTasks.Count == 0 && _completedTasks.Count == 0;
            SetSelection(preserveViewport ? SelectedTasks.Where(VisibleItems.Contains).ToArray() : []);
            if (activeCountChanged) NavigationDataChanged?.Invoke();
        }
        finally
        {
            VisibleItemsUpdated?.Invoke();
            RefreshOpenSuggestions();
        }
    }

    private static void ApplySequence<T>(ObservableCollection<T> collection, IReadOnlyList<T> target)
        where T : notnull
    {
        foreach (SequenceChange<T> change in SequenceDiff.Build(collection, target))
        {
            switch (change.Kind)
            {
                case SequenceChangeKind.Insert: collection.Insert(change.Index, change.Key); break;
                case SequenceChangeKind.Remove: collection.RemoveAt(change.Index); break;
                case SequenceChangeKind.Move: collection.Move(change.Index, change.Destination); break;
            }
        }
    }

    private TaskListGroupViewModel CreateTaskGroup(TaskGroup group)
    {
        return group.Kind switch
        {
            TaskGroupKind.Hidden => new(TaskListGroupKind.Hidden, string.Empty, string.Empty),
            TaskGroupKind.Search => new(TaskListGroupKind.Named,
                FindListName(group.ListId!.Value) ?? string.Empty,
                $"Todo.List.SearchGroup.{group.Key}"),
            TaskGroupKind.Planned => new(TaskListGroupKind.Named,
                _resources.GetString($"Planned_{group.Key}"),
                $"Todo.List.PlannedGroup.{group.Key}", group.Key == "Overdue"),
            TaskGroupKind.Completed => new(TaskListGroupKind.Completed,
                _resources.GetString("CompletedHeader/Text"), "Todo.List.CompletedHeader",
                toggle: ToggleCompletedAsync,
                accessibleName: string.Format(DisplayCulture.Current,
                    _resources.GetString("CompletedHeader_AccessibleName"), group.Count))
                { HeaderCount = group.Count, IsExpanded = group.IsExpanded },
            _ => throw new InvalidOperationException("Unknown task group kind."),
        };
    }

    private void UpdateItemDisplay(TaskItemViewModel item)
    {
        TodoTask task = item.Model;
        item.Summary = task.Summary;
        item.IsCompleted =
            task.Status == Wintodo.Core.Models.TaskStatus.Completed;
        item.IsImportant = task.Priority is >= 1 and <= 4;
        TaskMetadataResult metadata = TaskMetadataLine.Build(
            task, _currentQuery?.Kind, _clock, FindListName(task.ListId));
        item.IsOverdue = metadata.IsOverdue;
        item.IsDueToday = metadata.IsDueToday;
        string? dueLabel = null;
        var labels = new List<string>(metadata.Parts.Count);
        foreach (TaskMetadataPart part in metadata.Parts)
        {
            string label = part.Kind switch
            {
                TaskMetadataKind.ListName => part.Text ?? string.Empty,
                TaskMetadataKind.DueDate => FormatDueDate(part.Date!.Value),
                TaskMetadataKind.Repeat => _resources.GetString("Task_Metadata_Repeats"),
                TaskMetadataKind.MyDay => _resources.GetString("Task_Metadata_MyDay"),
                _ => throw new InvalidOperationException("Unknown metadata part kind."),
            };
            labels.Add(label);
            if (part.Kind == TaskMetadataKind.DueDate) dueLabel = label;
        }
        item.Metadata = string.Join(" · ", labels);
        item.CompletionDateText = task.CompletedAt is null
            ? string.Empty
            : string.Format(
                DisplayCulture.Current,
                _resources.GetString("Task_Metadata_Completed"),
                task.CompletedAt.Value.ToLocalTime());

        var accessibleParts = new List<string>(5) { task.Summary };
        if (dueLabel is not null)
        {
            accessibleParts.Add(string.Format(
                DisplayCulture.Current,
                _resources.GetString("Task_Accessible_Due"),
                dueLabel));
        }

        if (item.IsImportant)
        {
            accessibleParts.Add(_resources.GetString("Task_Accessible_Important"));
        }

        string? accessibleListName = FindListName(task.ListId);
        if (!string.IsNullOrEmpty(accessibleListName))
        {
            accessibleParts.Add(string.Format(
                DisplayCulture.Current,
                _resources.GetString("Task_Accessible_InList"),
                accessibleListName));
        }

        if (item.IsCompleted)
        {
            accessibleParts.Add(_resources.GetString("Task_Accessible_Completed"));
        }
        else
        {
            accessibleParts.Add(_resources.GetString("Task_Accessible_NotCompleted"));
        }

        item.AccessibleName = string.Join(", ", accessibleParts);
    }

    partial void OnIsCompactDensityChanged(bool value)
    {
        foreach (TaskItemViewModel item in _itemCache.Values)
        {
            item.IsCompactDensity = value;
        }
    }

    private void ConfigureEmptyState(ListQueryKind kind)
    {
        (EmptyGlyph, EmptyTitle, EmptySubtitle) = kind switch
        {
            ListQueryKind.MyDay => (
                "\uE706",
                _resources.GetString("Empty_MyDayTitle"),
                _resources.GetString("Empty_MyDaySubtitle")),
            ListQueryKind.Important => (
                "\uE734",
                _resources.GetString("Empty_ImportantTitle"),
                _resources.GetString("Empty_ImportantSubtitle")),
            ListQueryKind.Planned => (
                "\uE787",
                _resources.GetString("Empty_PlannedTitle"),
                _resources.GetString("Empty_PlannedSubtitle")),
            ListQueryKind.Completed => (
                "\uE930",
                _resources.GetString("Empty_CompletedTitle"),
                _resources.GetString("Empty_CompletedSubtitle")),
            ListQueryKind.All => (
                "\uE8FD",
                _resources.GetString("Empty_AllTitle"),
                _resources.GetString("Empty_AllSubtitle")),
            _ => throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "A user list does not have a smart-list empty state."),
        };
    }

    private string FormatDueDate(DateOnly date)
    {
        return TaskDateLabels.FormatListDue(
            date,
            _clock,
            DisplayCulture.Current,
            _resources.GetString("Task_Metadata_Yesterday"),
            _resources.GetString("Task_Metadata_Today"),
            _resources.GetString("Task_Metadata_Tomorrow"));
    }

    private string? FindListName(long listId)
    {
        foreach (ShellListItemViewModel list in _lists)
        {
            if (list.Id == listId)
            {
                return list.DisplayName;
            }
        }

        return null;
    }

    private string FormatToday()
    {
        return _clock.Today.ToString("D", DisplayCulture.Current);
    }

    private static DateTimeOffset StartOfLocalDay(DateOnly date)
    {
        DateTime local = date.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
    }

    private void InsertManualItem(List<TaskItemViewModel> items, TaskItemViewModel item)
    {
        bool descending = _currentQuery?.SortDescending == true;
        int index = items.FindIndex(candidate =>
            TaskSorting.CompareManualOrder(candidate.SortOrder, item.SortOrder, descending) > 0);
        items.Insert(index < 0 ? items.Count : index, item);
    }

    private static void InsertBySortOrder(
        List<TaskItemViewModel> items,
        TaskItemViewModel item)
    {
        int index = items.FindIndex(candidate =>
            TaskSorting.CompareManualOrder(candidate.SortOrder, item.SortOrder, reverse: false) > 0);
        if (index < 0)
        {
            items.Add(item);
        }
        else
        {
            items.Insert(index, item);
        }
    }

}
