using Wintodo.Core.Models;

namespace Wintodo.ViewModels;

internal sealed record AttachmentCopyTarget(
    Func<Task<TaskAttachment>> CopyAsync, Func<Task> DeleteAsync);

internal static class DetailAttachmentCopy
{
    internal static async Task<bool> CopyAsync(
        TodoTask owner,
        long remainingBytes,
        Func<string, Task<AttachmentCopyTarget>> allocate,
        Func<TodoTask, TaskAttachment, Task<bool>> persist)
    {
        var target = await allocate(owner.Uid);
        bool committed = false;
        try
        {
            TaskAttachment attachment = await target.CopyAsync();
            committed = attachment.SizeBytes <= remainingBytes && await persist(owner, attachment);
            return committed;
        }
        finally
        {
            if (!committed) await target.DeleteAsync();
        }
    }
}
