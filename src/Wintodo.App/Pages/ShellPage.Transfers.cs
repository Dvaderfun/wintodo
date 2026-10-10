using System.Diagnostics;
using System.IO.Compression;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.Storage.Pickers;
using Windows.Storage;
using Wintodo.Core.Ical;
using Wintodo.Core.Models;
using Wintodo.Services;
using Wintodo.ViewModels;

namespace Wintodo.Pages;

public sealed partial class ShellPage
{
    private async void OnImportIcalRequested()
    {
        ViewModel.SetTransferBusy(true);
        try
        {
            var picker = new FileOpenPicker(App.WindowId)
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            };
            picker.FileTypeFilter.Add(".ics");
            IReadOnlyList<PickFileResult> files = await picker.PickMultipleFilesAsync();
            if (files.Count == 0)
            {
                return;
            }

            var calendars = new List<IcalCalendar>(files.Count);
            var reader = new IcalReader(_clock.LocalTimeZone);
            long batchBytes = 0;
            foreach (PickFileResult file in files)
            {
                var fileInfo = new FileInfo(file.Path);
                if (fileInfo.Length > MaximumImportFileBytes)
                {
                    await ShowImportErrorAsync(_resources.GetString("Import_FileTooLarge"));
                    return;
                }

                if (fileInfo.Length > MaximumImportBatchBytes - batchBytes)
                {
                    await ShowImportErrorAsync(_resources.GetString("Import_BatchTooLarge"));
                    return;
                }

                batchBytes += fileInfo.Length;

                byte[] content = await File.ReadAllBytesAsync(file.Path);
                IcalCalendar calendar = reader.Read(content);
                calendar.Name = string.IsNullOrWhiteSpace(calendar.Name)
                    ? System.IO.Path.GetFileNameWithoutExtension(file.Path)
                    : calendar.Name.Trim();
                calendars.Add(calendar);
            }

            IcalImportPreview preview = await _taskStore.PreviewIcalImportAsync(calendars);
            ViewModel.IsImportPreviewDialogLoaded = true;
            await Task.Yield();
            ImportPreviewDialog.XamlRoot = XamlRoot;
            ImportPreviewDialog.IsPrimaryButtonEnabled = preview.TaskCount > 0;
            ImportPreviewMessage.Text = string.Format(
                DisplayCulture.Current,
                _resources.GetString("Import_PreviewMessage"),
                preview.TaskCount,
                preview.ListCount,
                preview.ConflictCount);
            ContentDialogResult decision = await ImportPreviewDialog.ShowAsync();
            if (decision != ContentDialogResult.Primary)
            {
                return;
            }

            try
            {
                IcalImportResult result = await _taskStore.ImportIcalAsync(calendars);
                await ViewModel.RefreshNavigationAsync();
                ShowTransferStatus(
                    _resources.GetString("Transfer_StatusTitleSuccess"),
                    string.Format(
                        DisplayCulture.Current,
                        _resources.GetString("Import_SuccessMessage"),
                        result.CreatedTaskCount,
                        result.UpdatedTaskCount),
                    InfoBarSeverity.Success);
            }
            catch (Exception exception)
            {
                Trace.WriteLine($"WINTODO_ICAL_IMPORT_APPLY_EXCEPTION={exception}");
                await ShowImportErrorAsync(_resources.GetString("Import_ApplyFailed"));
            }
        }
        catch (IcalFormatException exception)
        {
            Trace.WriteLine($"WINTODO_ICAL_IMPORT_FORMAT_EXCEPTION={exception}");
            string message = exception.LineNumber > 0
                ? string.Format(
                    DisplayCulture.Current,
                    _resources.GetString("Import_MalformedMessage"),
                    exception.LineNumber)
                : _resources.GetString("Import_MalformedMessageNoLine");
            await ShowImportErrorAsync(message);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Trace.WriteLine($"WINTODO_ICAL_IMPORT_READ_EXCEPTION={exception}");
            await ShowImportErrorAsync(_resources.GetString("Import_ReadFailed"));
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"WINTODO_ICAL_IMPORT_EXCEPTION={exception}");
            await ShowImportErrorAsync(_resources.GetString("Import_ApplyFailed"));
        }
        finally
        {
            ViewModel.SetTransferBusy(false);
        }
    }

    private async void OnExportListRequested(ShellListItemViewModel list)
    {
        ViewModel.SetTransferBusy(true);
        try
        {
            var picker = new FileSavePicker(App.WindowId)
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                SuggestedFileName = ViewModel.SanitizeFileName(list.DisplayName),
            };
            picker.FileTypeChoices.Add(
                _resources.GetString("Ical_FileType"),
                new List<string> { ".ics" });
            PickFileResult? target = await picker.PickSaveFileAsync();
            if (target is null)
            {
                return;
            }

            IReadOnlyList<TodoTask> tasks = await _taskStore.GetTasksForExportAsync(list.Id);
            var model = new TodoList
            {
                Id = list.Id,
                Uid = list.Uid,
                DisplayName = list.DisplayName,
                Color = list.Color,
                IconEmoji = list.IconEmoji,
            };
            byte[] content = new IcalWriter(_clock.LocalTimeZone).WriteCalendar(
                model,
                tasks,
                _clock.UtcNow,
                ApplicationData.Current.LocalFolder.Path);
            await WriteFileAtomicallyAsync(target.Path, content);
            ShowTransferStatus(
                _resources.GetString("Transfer_StatusTitleSuccess"),
                string.Format(
                    DisplayCulture.Current,
                    _resources.GetString("ExportList_SuccessMessage"),
                    list.DisplayName),
                InfoBarSeverity.Success);
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"WINTODO_ICAL_EXPORT_LIST_EXCEPTION={exception}");
            ShowTransferStatus(
                _resources.GetString("Transfer_StatusTitleError"),
                _resources.GetString("Export_Failed"),
                InfoBarSeverity.Error);
        }
        finally
        {
            ViewModel.SetTransferBusy(false);
        }
    }

    private async void OnExportAllRequested()
    {
        ViewModel.SetTransferBusy(true);
        string? temporaryPath = null;
        try
        {
            var picker = new FileSavePicker(App.WindowId)
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                SuggestedFileName = _resources.GetString("ExportAll_SuggestedFileName"),
            };
            picker.FileTypeChoices.Add(
                _resources.GetString("Zip_FileType"),
                new List<string> { ".zip" });
            PickFileResult? target = await picker.PickSaveFileAsync();
            if (target is null)
            {
                return;
            }

            temporaryPath = CreateTemporarySiblingPath(target.Path);
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var writer = new IcalWriter(_clock.LocalTimeZone);
            await using (var file = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 81920,
                             useAsync: true))
            using (var archive = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: false))
            {
                foreach (ShellListItemViewModel list in ViewModel.UserLists)
                {
                    IReadOnlyList<TodoTask> tasks =
                        await _taskStore.GetTasksForExportAsync(list.Id);
                    var model = new TodoList
                    {
                        Id = list.Id,
                        Uid = list.Uid,
                        DisplayName = list.DisplayName,
                        Color = list.Color,
                        IconEmoji = list.IconEmoji,
                    };
                    byte[] content = writer.WriteCalendar(
                        model,
                        tasks,
                        _clock.UtcNow,
                        ApplicationData.Current.LocalFolder.Path);
                    string entryName = ViewModel.GetUniqueFileName(list.DisplayName, usedNames);
                    ZipArchiveEntry entry = archive.CreateEntry(
                        entryName,
                        CompressionLevel.Optimal);
                    await using Stream entryStream = entry.Open();
                    await entryStream.WriteAsync(content);
                }
            }

            File.Move(temporaryPath, target.Path, overwrite: true);
            temporaryPath = null;
            ShowTransferStatus(
                _resources.GetString("Transfer_StatusTitleSuccess"),
                string.Format(
                    DisplayCulture.Current,
                    _resources.GetString("ExportAll_SuccessMessage"),
                    ViewModel.UserLists.Count),
                InfoBarSeverity.Success);
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"WINTODO_ICAL_EXPORT_ALL_EXCEPTION={exception}");
            ShowTransferStatus(
                _resources.GetString("Transfer_StatusTitleError"),
                _resources.GetString("Export_Failed"),
                InfoBarSeverity.Error);
        }
        finally
        {
            TryDeleteTemporaryFile(temporaryPath);
            ViewModel.SetTransferBusy(false);
        }
    }

    private async Task ShowImportErrorAsync(string message)
    {
        ViewModel.IsImportErrorDialogLoaded = true;
        await Task.Yield();
        ImportErrorDialog.XamlRoot = XamlRoot;
        ImportErrorMessage.Text = message;
        await ImportErrorDialog.ShowAsync();
    }

    private void ShowTransferStatus(
        string title,
        string message,
        InfoBarSeverity severity)
    {
        _transferStatusCancellation?.Cancel();
        _transferStatusCancellation?.Dispose();
        _transferStatusCancellation = new CancellationTokenSource();
        ViewModel.ShowTransferStatus(
            title,
            message,
            severity == InfoBarSeverity.Error);
        _ = CloseTransferStatusAsync(_transferStatusCancellation.Token);
    }

    private async Task CloseTransferStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(6), cancellationToken);
            ViewModel.IsTransferStatusOpen = false;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static async Task WriteFileAtomicallyAsync(string targetPath, byte[] content)
    {
        string temporaryPath = CreateTemporarySiblingPath(targetPath);
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, content);
            File.Move(temporaryPath, targetPath, overwrite: true);
        }
        finally
        {
            TryDeleteTemporaryFile(temporaryPath);
        }
    }

    private static void TryDeleteTemporaryFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            Trace.WriteLine($"WINTODO_ICAL_TEMP_CLEANUP_EXCEPTION={exception}");
        }
    }

    private static string CreateTemporarySiblingPath(string targetPath)
    {
        string directory = System.IO.Path.GetDirectoryName(targetPath)
            ?? throw new IOException("The selected destination has no directory.");
        return System.IO.Path.Combine(
            directory,
            $".{System.IO.Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");
    }

}
