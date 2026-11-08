using System.Diagnostics;
using Wintodo.Import.MicrosoftTodo;

namespace Wintodo.ViewModels;

public sealed partial class ImportWizardViewModel
{
    internal async Task DismissAsync()
    {
        await _session.EndAsync();
        ClearSensitiveState();
        Trace.WriteLine("WINTODO_IMPORT_SESSION=ENDED");
    }

    private void ClearSensitiveState()
    {
        _session.ReleaseConnection();
        _export = null;
        _lastFailure = null;
        _workTenantId = null;
        _lastRunId = null;
        _lastOutcome = null;
        _completedAt = null;
        _resumeAfterExpired = false;
        _pendingAttachmentIds = new HashSet<string>(StringComparer.Ordinal);
        AccountText = string.Empty;
        LastImportText = string.Empty;
        ErrorMessage = string.Empty;
        ErrorActionText = string.Empty;
        ResumeMessage = string.Empty;
        ProgressText = string.Empty;
        ResultTitle = string.Empty;
        IsErrorOpen = false;
        CanRemoveImported = false;
        Lists.Clear();
        SummaryLines.Clear();
        AddLines.Clear();
        UpdateLines.Clear();
        KeepLines.Clear();
        DuplicateTitles.Clear();
        ListSummaryLines.Clear();
        OnPropertyChanged(nameof(DidChange));
        SelectionChanged();
        ImportCommand.NotifyCanExecuteChanged();
    }

    private IProgress<MicrosoftTodoProgress> CreateProgress(int generation) =>
        new Progress<MicrosoftTodoProgress>(progress =>
        {
            if (_session.IsCurrent(generation)) ReportProgress(progress);
        });

    private CancellationToken BeginWork(ImportWizardStep step, string progressText)
    {
        CancellationToken token = _session.BeginWork();
        IsErrorOpen = false;
        IsBusy = true;
        SetProgress(progressText, indeterminate: true);
        Step = step;
        return token;
    }

    private void EndWork()
    {
        _session.EndWork();
        IsSaving = false;
        IsBusy = false;
    }
}
