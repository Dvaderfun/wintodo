using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wintodo.Core.Import;

namespace Wintodo.ViewModels;

public sealed partial class ImportWizardViewModel
{
    [ObservableProperty]
    public partial bool IsPreviewing { get; set; }

    [ObservableProperty]
    public partial bool ImportDuplicates { get; set; }

    partial void OnImportDuplicatesChanged(bool value)
    {
        if (IsReviewStep && !IsBusy) _ = ContinueCommand.ExecuteAsync(null);
    }

    public ObservableCollection<string> AddLines { get; } = [];
    public ObservableCollection<string> UpdateLines { get; } = [];
    public ObservableCollection<string> KeepLines { get; } = [];
    public ObservableCollection<string> DuplicateTitles { get; } = [];
    public ObservableCollection<string> ListSummaryLines { get; } = [];

    private void DescribeOutcome(ImportOutcome outcome)
    {
        SummaryLines.Clear();
        AddLines.Clear(); UpdateLines.Clear(); KeepLines.Clear(); DuplicateTitles.Clear(); ListSummaryLines.Clear();
        foreach (string title in outcome.PossibleDuplicateTitles) DuplicateTitles.Add(title);
        foreach (var list in outcome.ListOutcomes)
            ListSummaryLines.Add(Format("Import_PerListFormat", list.Key, list.Value.NewTasks, list.Value.UpdatedTasks, list.Value.UnchangedTasks));
        AddLine("Import_SummaryNewLists", outcome.NewLists);
        AddLine("Import_SummaryNewTasks", outcome.NewTasks);
        AddLine("Import_SummaryNewSteps", outcome.NewSteps);
        AddLine("Import_SummaryAttachments", outcome.NewAttachments);
        if (outcome.NewAttachments > 0) SummaryLines.Add(Format("Import_AttachmentSizeFormat", outcome.NewAttachments, outcome.NewAttachmentBytes / 1048576d));
        foreach (string line in SummaryLines) AddLines.Add(line);
        AddLine("Import_SummaryUpdated", outcome.UpdatedTasks);
        if (outcome.UpdatedTasks > 0) UpdateLines.Add(SummaryLines[^1]);
        int keepStart = SummaryLines.Count;
        AddLine("Import_SummaryExistingLists", outcome.ExistingLists);
        AddLine("Import_SummaryUnchanged", outcome.UnchangedTasks);
        AddLine("Import_SummaryKeptLocal", outcome.KeptLocalTasks);
        AddLine("Import_SummaryDeleted", outcome.SkippedDeletedTasks);
        AddLine("Import_SummarySkippedLists", outcome.SkippedLists);
        AddLine("Import_SummaryDuplicates", outcome.PossibleDuplicates);
        AddLine("Import_SummaryPreservedRepeats", outcome.PreservedRepeats);
        AddLine("Import_SummaryRenamedTitles", outcome.RenamedEmptyTitles);
        AddLine("Import_SummaryAcknowledged", outcome.AcknowledgedReminders);
        foreach (string line in SummaryLines.Skip(keepStart)) KeepLines.Add(line);
        if (!outcome.HasChanges)
        {
            SummaryLines.Add(_resources.GetString("Import_SummaryNothing"));
            KeepLines.Add(_resources.GetString("Import_SummaryNothing"));
        }
    }

    private void AddLine(string key, int count)
    {
        if (count > 0)
        {
            SummaryLines.Add(Format(key + "Format", count));
        }
    }

}
