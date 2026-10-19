using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Wintodo.ViewModels;

public sealed partial class ImportWizardViewModel
{
    private bool _updatingSelectAll;

    [ObservableProperty]
    public partial bool? SelectAll { get; set; }

    [RelayCommand]
    private void ToggleSelectAll() => SelectAll = !Lists.All(list => list.IsSelected);

    public string SelectionTotal => Format("Import_SelectionTotalFormat", Lists.Count(list => list.IsSelected),
        Lists.Where(list => list.IsSelected).Sum(list => list.OpenCount + (IncludeCompleted ? list.CompletedCount : 0)));

    public bool HasNoSelection => !Lists.Any(list => list.IsSelected);

    public string AttachmentDescription => Format("Import_AttachmentSizeFormat",
        SelectedAttachments().Count(), SelectedAttachments().Sum(attachment => attachment.Size) / 1048576d);

    private IEnumerable<Import.MicrosoftTodo.GraphAttachment> SelectedAttachments()
    {
        var ids = Lists.Where(list => list.IsSelected).Select(list => list.ExternalId).ToHashSet(StringComparer.Ordinal);
        return _export?.Lists.Where(list => ids.Contains(list.List.Id)).SelectMany(list => list.Tasks)
            .Where(task => IncludeCompleted || task.Status != "completed").SelectMany(task => task.Attachments) ?? [];
    }

    partial void OnIncludeCompletedChanged(bool value)
    {
        OnPropertyChanged(nameof(SelectionTotal));
        OnPropertyChanged(nameof(AttachmentDescription));
    }

    partial void OnSelectAllChanged(bool? value)
    {
        if (_updatingSelectAll) return;
        _updatingSelectAll = true;
        foreach (var list in Lists) list.IsSelected = value != false;
        _updatingSelectAll = false;
        SelectionChanged();
    }

    private void SelectionChanged()
    {
        if (!_updatingSelectAll)
        {
            _updatingSelectAll = true;
            SelectAll = Lists.All(list => list.IsSelected) ? true : Lists.All(list => !list.IsSelected) ? false : null;
            _updatingSelectAll = false;
        }
        OnPropertyChanged(nameof(SelectionTotal));
        OnPropertyChanged(nameof(HasNoSelection));
        OnPropertyChanged(nameof(AttachmentDescription));
        ContinueCommand.NotifyCanExecuteChanged();
    }
}
