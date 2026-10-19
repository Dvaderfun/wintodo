using Wintodo.Core.Import;
using Wintodo.Core.Models;

namespace Wintodo.Data.Tests;

public sealed class ImportAttachmentPreviewTests
{
    [Fact]
    public async Task PendingIdsExcludeLinkedAttachmentsAndLocallyDeletedTasks()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        await fixture.Store.EnsureDefaultListAsync("Tasks", token);
        var snapshot = new ImportSnapshot { Provider = "mstodo", AccountKey = "account" };
        var list = new ImportList { ExternalId = "list", DisplayName = "Tasks", Kind = ImportListKind.Default };
        var item = new ImportTask
        {
            ExternalId = "task",
            Task = new TodoTask { Summary = "Task", Created = fixture.Clock.UtcNow, LastModified = fixture.Clock.UtcNow },
        };
        var attachment = new ImportAttachment { ExternalId = "file", FileName = "file.txt", SizeBytes = 4 };
        item.Attachments.Add(attachment);
        list.Tasks.Add(item);
        snapshot.Lists.Add(list);
        var options = new ImportOptions { AttachmentRoot = fixture.DirectoryPath };
        Assert.Equal(["file"], (await fixture.Store.PreviewImportAsync(snapshot, options, token)).PendingAttachmentIds);
        string stage = Path.Combine(fixture.DirectoryPath, "staged.txt");
        await File.WriteAllTextAsync(stage, "test", token);
        attachment.StagedPath = stage;
        await fixture.Store.ApplyImportAsync(snapshot, options, token);
        attachment.StagedPath = null;
        Assert.Empty((await fixture.Store.PreviewImportAsync(snapshot, options, token)).PendingAttachmentIds);
        string uid = ImportIdentity.DeterministicUid("mstodo", "account", ImportEntityType.Task, "task");
        await fixture.Store.DeleteTaskAsync(uid, token);
        item.Attachments.Add(new ImportAttachment { ExternalId = "new-file", FileName = "new.txt" });
        Assert.Empty((await fixture.Store.PreviewImportAsync(snapshot, options, token)).PendingAttachmentIds);
    }
}
