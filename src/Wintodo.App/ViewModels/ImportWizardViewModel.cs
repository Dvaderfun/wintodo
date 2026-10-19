using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Windows.ApplicationModel.Resources;
using Wintodo.Core.Import;
using Wintodo.Core.Services;
using Wintodo.Import.MicrosoftTodo;
using Wintodo.Services;

namespace Wintodo.ViewModels;

public enum ImportWizardStep
{
    Connect,
    Reading,
    Choose,
    Review,
    Importing,
    Done,
}

/// <summary>
/// The Microsoft To Do import flow, shared by onboarding and Settings:
/// connect → read → choose → review (dry run) → import → done.
/// </summary>
public sealed partial class ImportWizardViewModel : ObservableObject
{
    private readonly ITaskStore _store;
    private readonly IClock _clock;
    private readonly ImportEnvironment _environment;
    private readonly ResourceLoader _resources = new();
    private CancellationTokenSource? _cancellation;
    private MicrosoftTodoReader? _reader;
    private MicrosoftTodoExport? _export;
    private string? _lastRunId;
    private IReadOnlySet<string> _pendingAttachmentIds = new HashSet<string>(StringComparer.Ordinal);
    private bool _resumeAfterExpired;

    internal ImportWizardViewModel(ITaskStore store, IClock clock, ImportEnvironment environment)
    {
        _store = store;
        _clock = clock;
        _environment = environment;
    }

    /// <summary>Raised after the store changed (import or removal), so navigation can refresh.</summary>
    internal event Func<Task>? Imported;

    /// <summary>Raised when the wizard should close; true when the person asked to see their tasks.</summary>
    internal event Action<bool>? CloseRequested;
    internal event Action? MyDayRequested;
    internal event Action<string>? CopyDetailsRequested;
    private DateTimeOffset? _completedAt;
    internal event Func<string, Task<bool>>? RemoveConfirmationRequested;
    private ImportOutcome? _lastOutcome;

    public bool IsSignInConfigured => _environment.Connector.IsConfigured;

    public bool IsSignInNotConfigured => !IsSignInConfigured;

    public ObservableCollection<ImportListOptionViewModel> Lists { get; } = [];

    public ObservableCollection<string> SummaryLines { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StepCaption))]
    [NotifyPropertyChangedFor(nameof(IsConnectStep))]
    [NotifyPropertyChangedFor(nameof(IsProgressStep))]
    [NotifyPropertyChangedFor(nameof(IsChooseStep))]
    [NotifyPropertyChangedFor(nameof(IsReviewStep))]
    [NotifyPropertyChangedFor(nameof(IsDoneStep))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    public partial ImportWizardStep Step { get; set; }

    public bool IsConnectStep => Step == ImportWizardStep.Connect;

    public bool IsProgressStep => Step is ImportWizardStep.Reading or ImportWizardStep.Importing;

    public bool IsChooseStep => Step == ImportWizardStep.Choose;

    public bool IsReviewStep => Step == ImportWizardStep.Review;

    public bool IsDoneStep => Step == ImportWizardStep.Done;

    public string StepCaption => Format("Import_StepFormat", Step switch
    {
        ImportWizardStep.Connect or ImportWizardStep.Reading => 1,
        ImportWizardStep.Choose => 2,
        ImportWizardStep.Review => 3,
        _ => 4,
    }, _resources.GetString(Step switch
    {
        ImportWizardStep.Connect or ImportWizardStep.Reading => "Import_StepSignIn",
        ImportWizardStep.Choose => "Import_StepChoose",
        ImportWizardStep.Review => "Import_StepReview",
        _ => "Import_StepImport",
    }));

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand))]
    [NotifyCanExecuteChangedFor(nameof(ContinueCommand))]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    [NotifyCanExecuteChangedFor(nameof(BackCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveImportedCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial bool IsErrorOpen { get; set; }

    [ObservableProperty]
    public partial string ErrorMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string AccountText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IncludeCompleted { get; set; } = true;

    [ObservableProperty]
    public partial bool IncludeAttachments { get; set; } = true;

    [ObservableProperty]
    public partial bool OverwriteLocalEdits { get; set; }

    [ObservableProperty]
    public partial bool HasPreviousImport { get; set; }

    /// <summary>The signed-in account returned no To Do lists at all.</summary>
    [ObservableProperty]
    public partial bool HasNoLists { get; set; }

    [ObservableProperty]
    public partial double ProgressValue { get; set; }

    [ObservableProperty]
    public partial double ProgressMaximum { get; set; } = 1;

    [ObservableProperty]
    public partial bool IsProgressIndeterminate { get; set; } = true;

    [ObservableProperty]
    public partial string ProgressText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ResultTitle { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveImportedCommand))]
    public partial bool CanRemoveImported { get; set; }

    [ObservableProperty]
    public partial string LastImportText { get; set; } = string.Empty;

    /// <summary>Back to the first step with nothing read; called whenever the wizard opens.</summary>
    internal void Reset()
    {
        if (IsBusy)
        {
            return;
        }

        Step = ImportWizardStep.Connect;
        IsErrorOpen = false;
        AccountText = string.Empty;
        OverwriteLocalEdits = false;
        CanRemoveImported = false;
        _reader = null;
        _export = null;
        _lastRunId = null;
        _lastOutcome = null;
        _completedAt = null;
        ImportDuplicates = false;
        _lastFailure = null;
        _workTenantId = null;
        ErrorActionText = string.Empty;
        ResumeMessage = string.Empty;
        HasNoLists = false;
        Lists.Clear();
        SummaryLines.Clear();
        _pendingAttachmentIds = new HashSet<string>(StringComparer.Ordinal);
        _resumeAfterExpired = false;
        _ = RefreshLastImportAsync();
    }

    internal async Task RefreshLastImportAsync()
    {
        try
        {
            IReadOnlyList<ImportSourceInfo> sources = await _store.GetImportSourcesAsync(GraphTodoMapper.Provider);
            ImportSourceInfo? latest = sources.FirstOrDefault();
            LastImportText = latest?.LastImport is DateTimeOffset when
                ? string.Format(
                    DisplayCulture.Current,
                    _resources.GetString("Import_LastImportFormat"),
                    latest.DisplayName,
                    TimeZoneInfo.ConvertTime(when, _clock.LocalTimeZone).ToString("d", DisplayCulture.Current))
                : string.Empty;
        }
        catch (Exception exception) when (exception is InvalidOperationException or ObjectDisposedException)
        {
            Trace.WriteLine($"WINTODO_IMPORT_SOURCES_EXCEPTION={exception}");
        }
    }

    private bool CanSignIn() => !IsBusy && IsSignInConfigured;

    [RelayCommand(CanExecute = nameof(CanSignIn))]
    private async Task SignInAsync()
    {
        CancellationToken token = BeginWork(ImportWizardStep.Reading, _resources.GetString("Import_ProgressSigningIn"));
        try
        {
            GraphTodoClient client = await _environment.Connector.ConnectAsync(token);
            _workTenantId = client.WorkTenantId;
            var retryProgress = new Progress<TimeSpan>(delay => SetProgress(Format("Import_RetryFormat", (int)Math.Ceiling(delay.TotalSeconds)), true));
            client.Retrying = delay => ((IProgress<TimeSpan>)retryProgress).Report(delay);
            SetProgress(_resources.GetString("Import_ProgressAccount"), indeterminate: true);
            if (_resumeAfterExpired && _export is not null && await MicrosoftTodoReader.CanResumeAsync(client, _export, token))
            {
                _resumeAfterExpired = false;
                _reader = new MicrosoftTodoReader(client);
                Step = ImportWizardStep.Review;
                ErrorMessage = _resources.GetString("Import_SignedInAgain");
                IsErrorOpen = false;
                ResumeMessage = ErrorMessage;
                return;
            }
            _resumeAfterExpired = false;
            _reader = new MicrosoftTodoReader(client);
            _export = await _reader.ReadAsync(new Progress<MicrosoftTodoProgress>(ReportProgress), token);
            IReadOnlyList<ImportSourceInfo> sources = await _store.GetImportSourcesAsync(GraphTodoMapper.Provider, token);
            HasPreviousImport = sources.Any(source => source.AccountKey == _export.User.Id);
            // Counts only: list and task names are personal data.
            Trace.WriteLine(
                $"WINTODO_IMPORT_READ lists={_export.Lists.Count} tasks={_export.Lists.Sum(list => list.Tasks.Count)}");
            ShowLists(_export);
            ImportSnapshot allLists = GraphTodoMapper.Map(_export, new MicrosoftTodoMapOptions
            {
                Now = _clock.UtcNow, LocalTimeZone = _clock.LocalTimeZone,
                UntitledTaskTitle = _resources.GetString("Import_UntitledTask"),
            });
            ImportOutcome hints = await _store.PreviewImportAsync(allLists, BuildOptions(), token);
            foreach (var option in Lists)
            {
                if (hints.PreviouslyImportedListIds.Contains(option.ExternalId))
                    option.Detail += "\n" + _resources.GetString("Import_PreviouslyImported");
                else if (hints.ListMergeNames.TryGetValue(option.ExternalId, out string? name))
                    option.Detail += "\n" + Format("Import_MergeHintFormat", name);
            }
            SelectionChanged();
            OnPropertyChanged(nameof(AttachmentDescription));
            Step = ImportWizardStep.Choose;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            Step = ImportWizardStep.Connect;
        }
        catch (Exception exception)
        {
            Step = ImportWizardStep.Connect;
            ShowError(DescribeReadFailure(exception), exception);
        }
        finally
        {
            EndWork();
        }
    }

    private bool CanContinue() => !IsBusy && Lists.Any(list => list.IsSelected);

    [RelayCommand(CanExecute = nameof(CanContinue))]
    private async Task ContinueAsync()
    {
        CancellationToken token = BeginWork(ImportWizardStep.Choose, _resources.GetString("Import_Checking"));
        IsPreviewing = true;
        try
        {
            ImportOutcome preview = await _store.PreviewImportAsync(await BuildSnapshotAsync(), BuildOptions(), token);
            _pendingAttachmentIds = preview.PendingAttachmentIds;
            DescribeOutcome(preview);
            Step = ImportWizardStep.Review;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            ShowError(_resources.GetString("Import_ErrorSave"), exception);
        }
        finally
        {
            IsPreviewing = false;
            EndWork();
        }
    }

    private bool CanImport() => !IsBusy && _export is not null;

    [RelayCommand(CanExecute = nameof(CanImport))]
    private async Task ImportAsync()
    {
        CancellationToken token = BeginWork(ImportWizardStep.Importing, _resources.GetString("Import_ProgressSaving"));
        string? staging = null;
        try
        {
            ImportSnapshot snapshot = await BuildSnapshotAsync();
            if (IncludeAttachments && _reader is not null)
            {
                staging = Path.Combine(_environment.AttachmentRoot, "import-staging", Guid.NewGuid().ToString("N"));
                await _reader.DownloadAttachmentsAsync(snapshot, staging, _pendingAttachmentIds,
                    new Progress<MicrosoftTodoProgress>(ReportProgress), token);
            }

            SetProgress(_resources.GetString("Import_ProgressSaving"), indeterminate: true);

            // Saving is one transaction; it is not cancelled half-way.
            ImportOutcome outcome = await _store.ApplyImportAsync(snapshot, BuildOptions(), CancellationToken.None);
            _lastRunId = outcome.RunId;
            _lastOutcome = outcome;
            _completedAt = _clock.UtcNow;
            CanRemoveImported = outcome.NewTasks + outcome.NewSteps + outcome.NewLists > 0;
            ResultTitle = _resources.GetString(outcome.HasChanges ? "Import_DoneTitle" : "Import_DoneNothingTitle");
            DescribeOutcome(outcome);
            Step = ImportWizardStep.Done;
            await RefreshLastImportAsync();
            await RaiseImportedAsync();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            Step = ImportWizardStep.Review;
        }
        catch (GraphAuthorizationException exception) when (exception.IsSignInExpired)
        {
            // Keep the read and preview. A new sign-in checks account identity
            // before reusing them; no token is refreshed or persisted.
            _reader = null;
            _resumeAfterExpired = true;
            Step = ImportWizardStep.Connect;
            ShowError(DescribeReadFailure(exception), exception);
        }
        catch (Exception exception)
        {
            Step = ImportWizardStep.Review;
            ShowError(
                exception is GraphRequestException or HttpRequestException or TaskCanceledException
                    ? DescribeReadFailure(exception)
                    : _resources.GetString("Import_ErrorSave"),
                exception);
        }
        finally
        {
            DeleteStaging(staging);
            EndWork();
        }
    }

    private bool CanGoBack() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void Back()
    {
        IsErrorOpen = false;
        Step = Step == ImportWizardStep.Review ? ImportWizardStep.Choose : ImportWizardStep.Connect;
    }

    [RelayCommand]
    private void Cancel()
    {
        if (IsBusy)
        {
            _cancellation?.Cancel();
            return;
        }

        CloseRequested?.Invoke(false);
    }

    [RelayCommand]
    private void Finish()
    {
        CloseRequested?.Invoke(true);
    }

    private bool CanRemove() => !IsBusy && CanRemoveImported && _lastRunId is not null;

    [RelayCommand(CanExecute = nameof(CanRemove))]
    private async Task RemoveImportedAsync()
    {
        if (_lastOutcome is null || RemoveConfirmationRequested is null
            || !await RemoveConfirmationRequested.Invoke(Format("Import_RemoveConfirmFormat",
                _lastOutcome.NewTasks, _lastOutcome.NewLists, _lastOutcome.NewSteps)))
        {
            return;
        }
        IsBusy = true;
        try
        {
            int removed = await _store.RemoveImportRunAsync(_lastRunId!);
            CanRemoveImported = false;
            ResultTitle = _resources.GetString("Import_RemovedTitle");
            SummaryLines.Clear();
            SummaryLines.Add(Format("Import_RemovedFormat", removed));
            await RaiseImportedAsync();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ShowError(_resources.GetString("Import_ErrorSave"), exception);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Reads the choices on the UI thread, then maps off it (large accounts).</summary>
    private Task<ImportSnapshot> BuildSnapshotAsync()
    {
        MicrosoftTodoExport export = _export!;
        var options = new MicrosoftTodoMapOptions
        {
            IncludeCompleted = IncludeCompleted,
            IncludeAttachments = IncludeAttachments,
            UntitledTaskTitle = _resources.GetString("Import_UntitledTask"),
            LocalTimeZone = _clock.LocalTimeZone,
            Now = _clock.UtcNow,
            SelectedListIds = Lists.Where(list => list.IsSelected)
                .Select(list => list.ExternalId)
                .ToHashSet(StringComparer.Ordinal),
        };
        return Task.Run(() => GraphTodoMapper.Map(export, options));
    }

    private ImportOptions BuildOptions()
    {
        return new ImportOptions
        {
            OverwriteLocalEdits = HasPreviousImport && OverwriteLocalEdits,
            ImportPossibleDuplicates = ImportDuplicates,
            AttachmentRoot = _environment.AttachmentRoot,
        };
    }

    private void ShowLists(MicrosoftTodoExport export)
    {
        // Signing in again within one wizard session (an expired sign-in) keeps
        // the person's choices; Reset() clears them when the wizard reopens.
        Dictionary<string, bool> previousSelection = new(StringComparer.Ordinal);
        foreach (ImportListOptionViewModel option in Lists)
        {
            option.SelectionChanged -= SelectionChanged;
            previousSelection[option.ExternalId] = option.IsSelected;
        }

        Lists.Clear();
        HasNoLists = export.Lists.Count == 0;
        AccountText = Format(
            "Import_AccountFormat",
            new[] { export.User.Mail, export.User.UserPrincipalName, export.User.DisplayName, export.User.Id }
                .First(value => !string.IsNullOrWhiteSpace(value))!);
        for (int index = 0; index < export.Lists.Count; index++)
        {
            MicrosoftTodoListData data = export.Lists[index];
            (string? emoji, string name) = GraphTodoMapper.SplitListEmoji(data.List.DisplayName);
            string label = emoji is null ? name : emoji + " " + name;
            int completedCount = data.Tasks.Count(task => task.Status == "completed");
            string detail = Format("Import_ListOpenCompletedFormat", data.Tasks.Count - completedCount, completedCount);
            if (data.List.IsShared) detail += "\n" + _resources.GetString("Import_SharedList");
            if (GraphTodoMapper.GetListKind(data.List) == ImportListKind.FlaggedEmail)
                detail += "\n" + _resources.GetString("Import_FlaggedDescription");
            bool selected = previousSelection.TryGetValue(data.List.Id, out bool previous)
                ? previous
                : GraphTodoMapper.GetListKind(data.List) != ImportListKind.FlaggedEmail;
            var option = new ImportListOptionViewModel(data.List.Id, label, detail, index, selected);
            option.OpenCount = data.Tasks.Count - completedCount;
            option.CompletedCount = completedCount;
            option.SelectionChanged += SelectionChanged;
            Lists.Add(option);
        }

        ContinueCommand.NotifyCanExecuteChanged();
    }

    private void ReportProgress(MicrosoftTodoProgress progress)
    {
        switch (progress.Phase)
        {
            case MicrosoftTodoReadPhase.Tasks when progress.Completed < progress.Total:
                SetProgress(
                    Format("Import_ProgressListsFormat", progress.ListName ?? string.Empty, progress.Completed + 1, progress.Total),
                    indeterminate: false,
                    progress.Completed,
                    progress.Total);
                break;
            case MicrosoftTodoReadPhase.Attachments when progress.Completed < progress.Total:
                SetProgress(
                    Format("Import_ProgressAttachmentBytesFormat", progress.CompletedBytes, progress.TotalBytes),
                    indeterminate: false,
                    progress.CompletedBytes,
                    Math.Max(1, progress.TotalBytes));
                break;
            case MicrosoftTodoReadPhase.Account or MicrosoftTodoReadPhase.Lists:
                SetProgress(_resources.GetString("Import_ProgressAccount"), indeterminate: true);
                break;
        }
    }

    private void SetProgress(string text, bool indeterminate, double value = 0, double maximum = 1)
    {
        ProgressText = text;
        IsProgressIndeterminate = indeterminate;
        ProgressMaximum = Math.Max(1, maximum);
        ProgressValue = value;
    }

    private CancellationToken BeginWork(ImportWizardStep step, string progressText)
    {
        _cancellation?.Dispose();
        _cancellation = new CancellationTokenSource();
        IsErrorOpen = false;
        IsBusy = true;
        SetProgress(progressText, indeterminate: true);
        Step = step;
        return _cancellation.Token;
    }

    private void EndWork()
    {
        IsBusy = false;
        _cancellation?.Dispose();
        _cancellation = null;
    }

    private string DescribeReadFailure(Exception exception)
    {
        return exception switch
        {
            MicrosoftSignInException { Failure: MicrosoftSignInFailure.Cancelled } => _resources.GetString("Import_ErrorSignInCancelled"),
            MicrosoftSignInException => _resources.GetString("Import_ErrorSignIn"),
            GraphAuthorizationException { IsSignInExpired: true } => _resources.GetString("Import_ErrorSignInExpired"),
            GraphAuthorizationException => _resources.GetString("Import_ErrorAccess"),
            HttpRequestException or TaskCanceledException => _resources.GetString("Import_ErrorOffline"),
            _ => _resources.GetString("Import_ErrorRead"),
        };
    }

    private void ShowError(string message, Exception exception)
    {
        Trace.WriteLine($"WINTODO_IMPORT_EXCEPTION={exception}");
        ErrorMessage = message;
        _lastFailure = exception;
        ErrorActionText = _resources.GetString(exception switch
        {
            GraphAuthorizationException { IsSignInExpired: true } => "Import_ErrorSignInAction",
            GraphAuthorizationException when _workTenantId is not null => "Import_ErrorAdminAction",
            _ => "Import_ErrorRetryAction",
        });
        IsErrorOpen = true;
    }

    private async Task RaiseImportedAsync()
    {
        if (Imported is not null)
        {
            await Imported.Invoke();
        }
    }

    private string Format(string key, params object[] values)
    {
        return string.Format(DisplayCulture.Current, _resources.GetString(key), values);
    }

    private static void DeleteStaging(string? staging)
    {
        if (staging is null)
        {
            return;
        }

        try
        {
            Directory.Delete(staging, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Trace.WriteLine($"WINTODO_IMPORT_STAGING_CLEANUP_EXCEPTION={exception}");
        }
    }
}
