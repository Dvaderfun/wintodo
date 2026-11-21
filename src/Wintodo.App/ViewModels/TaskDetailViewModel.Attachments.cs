using System.Diagnostics;
using Windows.Storage;
using Windows.System;
using Wintodo.Core.Models;
using Wintodo.Core.Services;

namespace Wintodo.ViewModels;

public sealed partial class TaskDetailViewModel
{
    internal Func<StorageFile, Task>? CaptureAttachmentAdder() =>
        _model is { } owner ? source => AttachFileAsync(owner, source) : null;

    private async Task AttachFileAsync(TodoTask owner, StorageFile source)
    {
        try
        {
            long remainingBytes = AttachmentLimitBytes;
            foreach (TaskAttachment item in owner.Attachments)
            {
                if (item.SizeBytes < 0 || item.SizeBytes > remainingBytes)
                {
                    ShowAttachmentError(owner.Uid, "Detail_AttachmentLimit");
                    return;
                }
                remainingBytes -= item.SizeBytes;
            }

            var properties = await source.GetBasicPropertiesAsync();
            if (properties.Size > (ulong)remainingBytes)
            {
                ShowAttachmentError(owner.Uid, "Detail_AttachmentLimit");
                return;
            }

            bool copied = await DetailAttachmentCopy.CopyAsync(
                owner, remainingBytes, uid => AllocateAttachmentAsync(uid, source), PersistAttachmentAsync);
            if (!copied)
            {
                ShowAttachmentError(owner.Uid, "Detail_AttachmentLimit");
            }
            else if (_model is { } current && current.Uid == owner.Uid)
            {
                RebuildAttachments();
                TaskChanged?.Invoke(CloneTask(current));
                IsErrorOpen = false;
            }
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"WINTODO_ATTACHMENT_COPY_EXCEPTION={DiagnosticException.Describe(exception)}");
            ShowAttachmentError(owner.Uid, "Detail_SaveFailed");
        }
    }

    private async Task<AttachmentCopyTarget> AllocateAttachmentAsync(string ownerUid, StorageFile source)
    {
        StorageFolder attachmentsRoot = await ApplicationData.Current.LocalFolder.CreateFolderAsync(
            "attachments", CreationCollisionOption.OpenIfExists);
        StorageFolder taskFolder = await attachmentsRoot.CreateFolderAsync(
            ownerUid, CreationCollisionOption.OpenIfExists);
        // Reserve a unique output first so a copy failure also has a file we own and can remove.
        StorageFile output = await taskFolder.CreateFileAsync(source.Name, CreationCollisionOption.GenerateUniqueName);
        return new AttachmentCopyTarget(async () =>
        {
            await source.CopyAndReplaceAsync(output);
            var properties = await output.GetBasicPropertiesAsync();
            return new TaskAttachment
            {
                FileName = output.Name,
                RelativePath = Path.Combine("attachments", ownerUid, output.Name),
                SizeBytes = checked((long)properties.Size),
                MimeType = output.ContentType,
                Added = _clock.UtcNow,
            };
        }, async () => await output.DeleteAsync(StorageDeleteOption.PermanentDelete));
    }

    private async Task<bool> PersistAttachmentAsync(TodoTask owner, TaskAttachment attachment)
    {
        await _saveGate.WaitAsync();
        try
        {
            if (!await _taskStore.TryAddTaskAttachmentAsync(owner.Uid, attachment, AttachmentLimitBytes))
                return false;

            // Reject any in-flight detail read whose snapshot predates the insert.
            _attachmentRevision++;
            owner.Attachments.Add(attachment);
            if (_model is { } current && current.Uid == owner.Uid && !ReferenceEquals(current, owner)
                && !current.Attachments.Any(item => item.Id == attachment.Id))
                current.Attachments.Add(attachment);
            return true;
        }
        finally
        {
            _saveGate.Release();
        }
    }

    private void ShowAttachmentError(string ownerUid, string resource)
    {
        if (_model?.Uid == ownerUid) ShowError(_resources.GetString(resource));
    }

    private async Task OpenAttachmentAsync(AttachmentItemViewModel item)
    {
        if (!TryResolveAttachmentPath(item.Model.RelativePath, out string path))
        {
            ShowError(_resources.GetString("Detail_AttachmentOpenFailed"));
            return;
        }

        try
        {
            StorageFile file = await StorageFile.GetFileFromPathAsync(path);
            // Imported and locally picked files are external content. Retain the
            // Windows file-type restrictions and ask Windows to show its warning.
            if (!await Launcher.LaunchFileAsync(file, new LauncherOptions { TreatAsUntrusted = true }))
            {
                ShowError(_resources.GetString("Detail_AttachmentOpenFailed"));
            }
        }
        catch (FileNotFoundException)
        {
            ShowError(_resources.GetString("Detail_AttachmentMissing"));
        }
    }

    private async Task RemoveAttachmentAsync(AttachmentItemViewModel item)
    {
        if (_model is null)
        {
            return;
        }

        int index = _model.Attachments.IndexOf(item.Model);
        if (index < 0)
        {
            return;
        }

        _model.Attachments.RemoveAt(index);
        if (!await PersistModelAsync())
        {
            _model.Attachments.Insert(index, item.Model);
            return;
        }

        var attachments = new List<object>(Attachments);
        attachments.Remove(item);
        Attachments = attachments;
        if (!TryResolveAttachmentPath(item.Model.RelativePath, out string path))
        {
            return;
        }

        try
        {
            StorageFile file = await StorageFile.GetFileFromPathAsync(path);
            await file.DeleteAsync(StorageDeleteOption.PermanentDelete);
        }
        catch (FileNotFoundException)
        {
        }
    }

    private static bool TryResolveAttachmentPath(
        string relativePath,
        out string resolvedPath)
    {
        try
        {
            string localRoot = ApplicationData.Current.LocalFolder.Path;
            string attachmentsRoot = Path.GetFullPath(
                Path.Combine(localRoot, "attachments"));
            string candidate = Path.GetFullPath(Path.Combine(localRoot, relativePath));
            string requiredPrefix = attachmentsRoot + Path.DirectorySeparatorChar;
            if (candidate.StartsWith(requiredPrefix, StringComparison.OrdinalIgnoreCase))
            {
                resolvedPath = candidate;
                return true;
            }
        }
        catch (Exception exception) when (
            exception is ArgumentException
            or NotSupportedException
            or PathTooLongException)
        {
        }

        resolvedPath = string.Empty;
        return false;
    }

    private void RebuildAttachments()
    {
        if (_model is null)
        {
            Attachments = new List<object>();
            return;
        }

        var attachments = new List<object>(_model.Attachments.Count);
        foreach (TaskAttachment attachment in _model.Attachments)
        {
            attachments.Add(new AttachmentItemViewModel(
                attachment,
                OpenAttachmentAsync,
                RemoveAttachmentAsync));
        }
        Attachments = attachments;
    }

}
