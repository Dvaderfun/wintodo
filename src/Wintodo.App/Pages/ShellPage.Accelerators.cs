using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml;
using Windows.System;
using Wintodo.ViewModels;

namespace Wintodo.Pages;

public sealed partial class ShellPage
{
    private void OnNewListNameBoxKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key == VirtualKey.Enter
            && ViewModel.CreateListCommand.CanExecute(null))
        {
            ViewModel.CreateListCommand.Execute(null);
            args.Handled = true;
        }
        else if (args.Key == VirtualKey.Escape)
        {
            ViewModel.CancelCreateListCommand.Execute(null);
            args.Handled = true;
        }
    }

    private void OnNewGroupNameBoxKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key == VirtualKey.Enter
            && ViewModel.CreateGroupCommand.CanExecute(null))
        {
            ViewModel.CreateGroupCommand.Execute(null);
            args.Handled = true;
        }
        else if (args.Key == VirtualKey.Escape)
        {
            ViewModel.CancelCreateGroupCommand.Execute(null);
            args.Handled = true;
        }
    }

    // Clicking anywhere outside leaves the editor, the same way the inline list title
    // editor behaves. A blank name has nothing to commit, so it reverts instead.
    private void OnNewListNameBoxLostFocus(object sender, RoutedEventArgs args)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!ViewModel.IsCreatingList || !HasFocusMovedInsideApp())
            {
                return;
            }

            if (ViewModel.CreateListCommand.CanExecute(null))
            {
                ViewModel.CreateListCommand.Execute(null);
                return;
            }

            ViewModel.CancelCreateListCommand.Execute(null);
        });
    }

    private void OnNewGroupNameBoxLostFocus(object sender, RoutedEventArgs args)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!ViewModel.IsCreatingGroup || !HasFocusMovedInsideApp())
            {
                return;
            }

            if (ViewModel.CreateGroupCommand.CanExecute(null))
            {
                ViewModel.CreateGroupCommand.Execute(null);
                return;
            }

            ViewModel.CancelCreateGroupCommand.Execute(null);
        });
    }

    // Switching away from the window also raises LostFocus, and closing the editor
    // there would drop a half-typed name on every alt-tab. Focus lands on nothing
    // inside the app in that case, so the editor stays open until focus moves to
    // another element in the window.
    private bool HasFocusMovedInsideApp()
    {
        return XamlRoot is { } xamlRoot
            && FocusManager.GetFocusedElement(xamlRoot) is not null;
    }

    // Closing the editor takes the focused element out of the tree. Without a
    // landing spot focus flies on to the next tab stop — the footer menu — where
    // the key-up half of the committing Enter press opens Settings.
    private void ReturnFocusToPaneFooter(Control button)
    {
        if (NewItemActionsHost.Visibility != Visibility.Visible
            || button.Visibility != Visibility.Visible)
        {
            return;
        }

        button.Focus(FocusState.Programmatic);
    }

    private IList<object> CreateKeyboardShortcuts()
    {
        var shortcuts = new List<object>(21);
        for (int index = 1; index <= 21; index++)
        {
            string key = index.ToString("00", CultureInfo.InvariantCulture);
            shortcuts.Add(new ShortcutEntryViewModel(
                _resources.GetString($"Shortcut{key}_Key"),
                _resources.GetString($"Shortcut{key}_Action")));
        }

        return shortcuts;
    }

    private void AddPunctuationAccelerators()
    {
        var undo = new KeyboardAccelerator { Key = VirtualKey.Z, Modifiers = VirtualKeyModifiers.Control };
        undo.Invoked += OnUndoAccelerator;
        KeyboardAccelerators.Add(undo);
        var settings = new KeyboardAccelerator
        {
            Key = (VirtualKey)188,
            Modifiers = VirtualKeyModifiers.Control,
        };
        settings.Invoked += OnSettingsAccelerator;
        KeyboardAccelerators.Add(settings);

        var shortcuts = new KeyboardAccelerator
        {
            Key = (VirtualKey)191,
            Modifiers = VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift,
        };
        shortcuts.Invoked += OnShowShortcutsAccelerator;
        KeyboardAccelerators.Add(shortcuts);
    }

    private void OnUndoAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (FocusManager.GetFocusedElement(XamlRoot) is TextBox or RichEditBox or PasswordBox) return;
        if (!ViewModel.TaskList.UndoDeleteCommand.CanExecute(null)) return;
        ViewModel.TaskList.UndoDeleteCommand.Execute(null);
        args.Handled = true;
    }

    private void OnNewListAccelerator(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        ViewModel.BeginCreateListCommand.Execute(null);
        args.Handled = true;
    }

    private void OnNewGroupAccelerator(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        ViewModel.BeginCreateGroupCommand.Execute(null);
        args.Handled = true;
    }

    private void OnFocusSearchAccelerator(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        SearchFocusRequested?.Invoke();
        args.Handled = true;
    }

    private void OnSettingsAccelerator(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        ShellNavigation.SelectedItem = SettingsNavigationItem;
        args.Handled = true;
    }

    private void OnShellKeyDown(object sender, KeyRoutedEventArgs args)
    {
        const int virtualKeyControl = 0x11;
        if ((GetKeyState(virtualKeyControl) & 0x8000) == 0)
        {
            return;
        }

        if (args.Key == (VirtualKey)188)
        {
            ShellNavigation.SelectedItem = SettingsNavigationItem;
            args.Handled = true;
        }
        else if (args.Key == (VirtualKey)191)
        {
            OnKeyboardShortcutsRequested();
            args.Handled = true;
        }
    }

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int virtualKey);

    private void OnShowShortcutsAccelerator(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        OnKeyboardShortcutsRequested();
        args.Handled = true;
    }

    private async void OnDueDateAccelerator(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        TaskItemViewModel? selected = ViewModel.TaskList.SelectedTask;
        if (selected is null)
        {
            return;
        }

        await ViewModel.OpenDetailAsync(selected);
        DispatcherQueue.TryEnqueue(TaskDetail.OpenDuePicker);
        args.Handled = true;
    }

    private async void OnReminderAccelerator(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        TaskItemViewModel? selected = ViewModel.TaskList.SelectedTask;
        if (selected is null)
        {
            return;
        }

        await ViewModel.OpenDetailAsync(selected);
        DispatcherQueue.TryEnqueue(TaskDetail.OpenReminderPicker);
        args.Handled = true;
    }

    private void OnSmartListAccelerator(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        ShellNavigation.SelectedItem = sender.Key switch
        {
            VirtualKey.Number2 => ImportantNavigationItem,
            VirtualKey.Number3 => PlannedNavigationItem,
            VirtualKey.Number4 => AllNavigationItem,
            VirtualKey.Number5 => CompletedNavigationItem,
            _ => MyDayNavigationItem,
        };
        args.Handled = true;
    }

    private void OnEscapeAccelerator(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {

        if (ViewModel.TaskList.SelectionCount > 0)
        {
            TaskList.ClearTaskSelection();
            ViewModel.CloseDetail();
            args.Handled = true;
            return;
        }
        if (ViewModel.IsDetailPaneOpen)
        {
            ViewModel.CloseDetail();
            args.Handled = true;
            return;
        }

        if (SearchClearRequested?.Invoke() == true)
        {
            args.Handled = true;
            return;
        }

        if (ViewModel.TaskList.IsComposerExpanded)
        {
            ViewModel.TaskList.CancelComposerCommand.Execute(null);
            args.Handled = true;
        }
    }

}
