using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wintodo.Core.Services;

namespace Wintodo.ViewModels;

public sealed partial class ShellViewModel
{
    internal const string OnboardingCompletedSetting = "onboarding.completed";
    private Services.ImportEnvironment _importEnvironment = null!;
    private bool _importOpenedFromWelcome;
    private IClock _importClock = null!;
    private Task _importDismissal = Task.CompletedTask;

    public ImportWizardViewModel ImportWizard { get; private set; } = null!;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTaskListVisible))]
    [NotifyPropertyChangedFor(nameof(IsWelcomeContentVisible))]
    public partial bool IsWelcomeVisible { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTaskListVisible))]
    [NotifyPropertyChangedFor(nameof(IsWelcomeContentVisible))]
    [NotifyPropertyChangedFor(nameof(IsSettingsContentVisible))]
    public partial bool IsImportVisible { get; set; }

    public bool IsTaskListVisible => !IsSettingsVisible && !IsWelcomeVisible && !IsImportVisible;

    /// <summary>The import wizard covers whichever surface opened it.</summary>
    public bool IsWelcomeContentVisible => IsWelcomeVisible && !IsImportVisible;

    public bool IsSettingsContentVisible => IsSettingsVisible && !IsImportVisible;


    partial void OnIsSettingsVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(IsTaskListVisible));
        OnPropertyChanged(nameof(IsSettingsContentVisible));
    }

    private void ShowDefaultList()
    {
        if (DefaultList is not null)
        {
            NavigationRequested?.Invoke(DefaultList.NavigationKey);
        }
    }

    private void InitializeImport(ITaskStore taskStore, IClock clock, Services.ImportEnvironment environment)
    {
        _importEnvironment = environment;
        _importClock = clock;
        ImportWizard = new ImportWizardViewModel(taskStore, clock, environment);
        ImportWizard.Imported += OnImportCompletedAsync;
        ImportWizard.CloseRequested += OnImportCloseRequested;
        ImportWizard.MyDayRequested += () =>
        {
            IsSettingsVisible = false;
            IsImportVisible = false;
            NavigationRequested?.Invoke(MyDayNavigationKey);
        };
    }

    /// <summary>
    /// Shows the Welcome surface on a fresh database. A database that already
    /// holds tasks or lists is treated as onboarded without asking.
    /// </summary>
    private async Task InitializeOnboardingAsync(CancellationToken cancellationToken)
    {
        if (!_importEnvironment.OnboardingAllowed)
        {
            return;
        }

        try
        {
            StoreStats stats = await _taskStore.GetStatsAsync(cancellationToken);
            if (stats.TaskCount == 0 && stats.ListCount <= 1)
            {
                IsWelcomeVisible = true;
                return;
            }

            await _taskStore.SetSettingAsync(OnboardingCompletedSetting, "true", cancellationToken);
        }
        catch (Exception exception) when (exception is InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        {
            Trace.WriteLine($"WINTODO_ONBOARDING_EXCEPTION={DiagnosticException.Describe(exception)}");
        }
    }

    [RelayCommand]
    private async Task StartMicrosoftTodoImportAsync()
    {
        await _importDismissal;
        _importOpenedFromWelcome = IsWelcomeVisible;
        CloseDetail();
        ImportWizard.Reset();
        IsImportVisible = true;
    }

    [RelayCommand]
    private async Task DismissWelcomeAsync()
    {
        await CompleteOnboardingAsync();
        ShowDefaultList();
    }

    [RelayCommand]
    private Task WelcomeImportIcalAsync()
    {
        ImportIcalRequested?.Invoke();
        return Task.CompletedTask;
    }

    internal async Task OnIcalImportAppliedAsync()
    {
        if (IsWelcomeVisible)
        {
            await CompleteOnboardingAsync();
            ShowDefaultList();
        }
    }

    /// <summary>Navigating anywhere leaves the Welcome and import surfaces.</summary>
    internal void LeaveImportSurfaces()
    {

        if (IsWelcomeVisible)
        {
            _ = CompleteOnboardingAsync();
        }

        if (IsImportVisible)
        {
            IsImportVisible = false;
            _importDismissal = ImportWizard.DismissAsync();
        }
    }

    internal async Task ShutdownImportAsync()
    {
        await _importDismissal;
        await ImportWizard.DismissAsync();
    }

    private async Task CompleteOnboardingAsync()
    {
        IsWelcomeVisible = false;
        try
        {
            await _taskStore.SetSettingAsync(OnboardingCompletedSetting, "true");
        }
        catch (Exception exception) when (exception is InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        {
            Trace.WriteLine($"WINTODO_ONBOARDING_SAVE_EXCEPTION={DiagnosticException.Describe(exception)}");
        }
    }

    private async Task OnImportCompletedAsync()
    {
        if (IsWelcomeVisible || _importOpenedFromWelcome)
        {
            await CompleteOnboardingAsync();
        }

        await RefreshNavigationAsync();
    }

    private void OnImportCloseRequested(bool showTasks)
    {
        if (showTasks)
        {
            // Leave Settings first so closing the wizard doesn't briefly re-load it.
            IsSettingsVisible = false;
            IsImportVisible = false;
            ShowDefaultList();
            return;
        }

        IsImportVisible = false;
        if (IsSettingsVisible)
        {
            _ = RefreshDatabaseStatsAsync();
        }
    }
}
