using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml;
using Wintodo.Core.Models;
using Wintodo.Services;
using Wintodo.ViewModels;

namespace Wintodo.Pages;

public sealed partial class ShellPage
{
    private async void OnDeleteListRequested(ShellListItemViewModel list)
    {
        ViewModel.IsDeleteListDialogLoaded = true;
        await Task.Yield();
        DeleteListDialog.XamlRoot = XamlRoot;
        DeleteListMessage.Text = string.Format(
            DisplayCulture.Current,
            _resources.GetString("DeleteListDialog_Message"),
            list.DisplayName);
        ContentDialogResult result = await DeleteListDialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            await ViewModel.ConfirmDeleteListAsync(list);
        }
    }

    private async void OnDeleteTaskRequested(string summary)
    {
        if (!ViewModel.Preferences.ConfirmBeforeDelete)
        {
            await ViewModel.ConfirmDeleteTaskAsync();
            return;
        }
        ViewModel.IsDeleteTaskDialogLoaded = true;
        await Task.Yield();
        DeleteTaskDialog.XamlRoot = XamlRoot;
        DeleteTaskMessage.Text = string.Format(
            DisplayCulture.Current,
            _resources.GetString(ViewModel.PendingDeleteCount > 1
                ? "DeleteTasksDialog_Message" : "DeleteTaskDialog_Message"),
            ViewModel.PendingDeleteCount > 1 ? ViewModel.PendingDeleteCount : summary);
        ContentDialogResult result = await DeleteTaskDialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            await ViewModel.ConfirmDeleteTaskAsync();
        }
        else
        {
            ViewModel.CancelDeleteTask();
        }
    }

    private void OnDeleteListDialogOpened(
        ContentDialog sender,
        ContentDialogOpenedEventArgs args)
    {
        SetOpenDialogButtonAutomationId(
            sender,
            "PrimaryButton",
            "Todo.Dialog.DeleteList.Confirm");
        SetOpenDialogButtonAutomationId(
            sender,
            "CloseButton",
            "Todo.Dialog.DeleteList.Cancel");
    }

    private void OnDeleteTaskDialogOpened(
        ContentDialog sender,
        ContentDialogOpenedEventArgs args)
    {
        SetOpenDialogButtonAutomationId(
            sender,
            "PrimaryButton",
            "Todo.Dialog.DeleteTask.Confirm");
        SetOpenDialogButtonAutomationId(
            sender,
            "CloseButton",
            "Todo.Dialog.DeleteTask.Cancel");
    }

    private async void OnCustomListIconRequested()
    {
        await Task.Yield();
        ListIconDialog.XamlRoot = XamlRoot;
        ContentDialogResult result = await ListIconDialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            await ViewModel.CommitCustomListIconAsync();
        }
    }

    private void OnListIconDialogOpened(
        ContentDialog sender,
        ContentDialogOpenedEventArgs args)
    {
        SetOpenDialogButtonAutomationId(
            sender,
            "PrimaryButton",
            "Todo.Dialog.ListIcon.Save");
        SetOpenDialogButtonAutomationId(
            sender,
            "CloseButton",
            "Todo.Dialog.ListIcon.Cancel");
        ListIconInput.Focus(FocusState.Programmatic);
        ListIconInput.SelectAll();
    }

    private void OnListIconDialogClosing(
        ContentDialog sender,
        ContentDialogClosingEventArgs args)
    {
        if (args.Result == ContentDialogResult.Primary
            && !ViewModel.ValidateCustomListIconInput())
        {
            args.Cancel = true;
            ListIconInput.Focus(FocusState.Programmatic);
            ListIconInput.SelectAll();
        }
    }

    private void OnImportPreviewDialogOpened(
        ContentDialog sender,
        ContentDialogOpenedEventArgs args)
    {
        SetOpenDialogButtonAutomationId(
            sender,
            "PrimaryButton",
            "Todo.Dialog.ImportPreview.Import");
        SetOpenDialogButtonAutomationId(
            sender,
            "CloseButton",
            "Todo.Dialog.ImportPreview.Cancel");
    }

    private void OnImportErrorDialogOpened(
        ContentDialog sender,
        ContentDialogOpenedEventArgs args)
    {
        SetOpenDialogButtonAutomationId(
            sender,
            "CloseButton",
            "Todo.Dialog.ImportError.Close");
    }

    private void SetOpenDialogButtonAutomationId(
        ContentDialog dialog,
        string generatedId,
        string automationId)
    {
        if (TrySetOpenDialogButtonAutomationId(dialog, generatedId, automationId))
        {
            return;
        }

        DispatcherQueue.TryEnqueue(
            () => TrySetOpenDialogButtonAutomationId(dialog, generatedId, automationId));
    }

    private static bool TrySetOpenDialogButtonAutomationId(
        ContentDialog dialog,
        string generatedId,
        string automationId)
    {
        foreach (Microsoft.UI.Xaml.Controls.Primitives.Popup popup in
                 VisualTreeHelper.GetOpenPopupsForXamlRoot(dialog.XamlRoot))
        {
            if (TrySetDialogButtonAutomationId(popup.Child, generatedId, automationId))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TrySetDialogButtonAutomationId(
        DependencyObject root,
        string generatedId,
        string automationId)
    {
        int childCount = VisualTreeHelper.GetChildrenCount(root);
        for (int index = 0; index < childCount; index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            if (child is Button button
                && (button.Name == generatedId
                    || AutomationProperties.GetAutomationId(button) == generatedId))
            {
                AutomationProperties.SetAutomationId(button, automationId);
                return true;
            }

            if (TrySetDialogButtonAutomationId(child, generatedId, automationId))
            {
                return true;
            }
        }

        return false;
    }

    private async void OnKeyboardShortcutsRequested()
    {
        if (_isShortcutsDialogOpen)
        {
            return;
        }

        _isShortcutsDialogOpen = true;
        try
        {
            ViewModel.IsShortcutsDialogLoaded = true;
            await Task.Yield();
            await ShortcutsDialog.ShowAsync();
        }
        catch (COMException exception)
            when (exception.HResult == unchecked((int)0x80000019))
        {
            Trace.WriteLine($"WINTODO_SHORTCUTS_DIALOG_BUSY={exception.Message}");
        }
        finally
        {
            _isShortcutsDialogOpen = false;
        }
    }

    private void OnShortcutsDialogOpened(ContentDialog sender, ContentDialogOpenedEventArgs args)
    {
        SetOpenDialogButtonAutomationId(
            sender,
            generatedId: "CloseButton",
            automationId: "Todo.Dialog.Shortcuts.Close");
    }

    private async void OnSyncConflictsRequested(IReadOnlyList<SyncConflict> conflicts)
    {
        if (_isSyncConflictDialogOpen || conflicts.Count == 0)
        {
            return;
        }

        _isSyncConflictDialogOpen = true;
        try
        {
            foreach (SyncConflict conflict in conflicts)
            {
                ViewModel.IsSyncConflictDialogLoaded = true;
                await Task.Yield();
                SyncConflictDialog.XamlRoot = XamlRoot;
                SyncConflictMessage.Text = string.Format(
                    DisplayCulture.Current,
                    _resources.GetString(
                        conflict.RemoteDeleted
                            ? "Sync_ConflictDeletedMessage"
                            : "Sync_ConflictMessage"),
                    conflict.LocalTask.Summary,
                    conflict.ListName);
                ContentDialogResult result = await SyncConflictDialog.ShowAsync();
                SyncConflictChoice choice = result switch
                {
                    ContentDialogResult.Primary => SyncConflictChoice.KeepMine,
                    ContentDialogResult.Secondary => SyncConflictChoice.KeepTheirs,
                    _ => SyncConflictChoice.KeepBoth,
                };
                await ViewModel.ResolveSyncConflictAsync(conflict, choice);
            }

            await ViewModel.PushConflictResolutionsAsync();
        }
        catch (COMException exception)
            when (exception.HResult == unchecked((int)0x80000019))
        {
            Trace.WriteLine($"WINTODO_SYNC_DIALOG_BUSY={exception.Message}");
        }
        finally
        {
            _isSyncConflictDialogOpen = false;
        }
    }

    private void OnSyncConflictDialogOpened(
        ContentDialog sender,
        ContentDialogOpenedEventArgs args)
    {
        SetOpenDialogButtonAutomationId(
            sender,
            "PrimaryButton",
            "Todo.Dialog.SyncConflict.KeepMine");
        SetOpenDialogButtonAutomationId(
            sender,
            "SecondaryButton",
            "Todo.Dialog.SyncConflict.KeepTheirs");
        SetOpenDialogButtonAutomationId(
            sender,
            "CloseButton",
            "Todo.Dialog.SyncConflict.KeepBoth");
    }

}
