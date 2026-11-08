using System.Diagnostics;
using Windows.Storage;
using Windows.System;
using Wintodo.Core.Models;

namespace Wintodo.ViewModels;

public sealed partial class TaskDetailViewModel
{
    internal async Task AttachFileAsync(StorageFile source)
    {
        if (_model is null)
        {
            return;
        }

        Windows.Storage.FileProperties.BasicProperties properties =
            await source.GetBasicPropertiesAsync();
        long currentSize = 0;
        foreach (TaskAttachment item in _model.Attachments)
        {
            if (item.SizeBytes < 0
                || item.SizeBytes > AttachmentLimitBytes - currentSize)
            {
                ShowError(_resources.GetString("Detail_AttachmentLimit"));
                return;
            }

            currentSize += item.SizeBytes;
        }

        ulong remainingBytes = (ulong)(AttachmentLimitBytes - currentSize);
        if (properties.Size > remainingBytes)
        {
            ShowError(_resources.GetString("Detail_AttachmentLimit"));
            return;
        }

        StorageFolder attachmentsRoot =
            await ApplicationData.Current.LocalFolder.CreateFolderAsync(
                "attachments",
                CreationCollisionOption.OpenIfExists);
        StorageFolder taskFolder = await attachmentsRoot.CreateFolderAsync(
            _model.Uid,
            CreationCollisionOption.OpenIfExists);
        StorageFile copied = await source.CopyAsync(
            taskFolder,
            source.Name,
            NameCollisionOption.GenerateUniqueName);
        Windows.Storage.FileProperties.BasicProperties copiedProperties =
            await copied.GetBasicPropertiesAsync();
        if (copiedProperties.Size > remainingBytes)
        {
            await copied.DeleteAsync(StorageDeleteOption.PermanentDelete);
            ShowError(_resources.GetString("Detail_AttachmentLimit"));
            return;
        }

        var attachment = new TaskAttachment
        {
            FileName = copied.Name,
            RelativePath = Path.Combine("attachments", _model.Uid, copied.Name),
            SizeBytes = (long)copiedProperties.Size,
            MimeType = copied.ContentType,
            Added = _clock.UtcNow,
        };
        _model.Attachments.Add(attachment);
        if (await PersistModelAsync())
        {
            RebuildAttachments();
        }
        else
        {
            _model.Attachments.Remove(attachment);
            await copied.DeleteAsync(StorageDeleteOption.PermanentDelete);
        }
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
