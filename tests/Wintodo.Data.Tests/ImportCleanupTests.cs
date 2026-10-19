using Wintodo.Core.Models;

namespace Wintodo.Data.Tests;

public sealed class ImportCleanupTests
{
    [Fact]
    public async Task RecoveryRemovesAbandonedStagingAndOrphansButKeepsReferencedFiles()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        var list = await fixture.Store.EnsureDefaultListAsync("Tasks", token);
        var task = await fixture.Store.CreateTaskAsync(new TodoTask { ListId = list.Id, Summary = "Kept" }, token);
        string referenced = Path.Combine(fixture.DirectoryPath, "attachments", task.Uid);
        string orphan = Path.Combine(fixture.DirectoryPath, "attachments", Guid.NewGuid().ToString("D"));
        string staging = Path.Combine(fixture.DirectoryPath, "import-staging", "interrupted");
        foreach (string folder in new[] { referenced, orphan, staging }) Directory.CreateDirectory(folder);
        string relative = Path.Combine("attachments", task.Uid, "kept.txt");
        await File.WriteAllTextAsync(Path.Combine(fixture.DirectoryPath, relative), "keep", token);
        task.Attachments.Add(new TaskAttachment { FileName = "kept.txt", RelativePath = relative, SizeBytes = 4 });
        await fixture.Store.UpdateTaskAsync(task, token);
        await fixture.Store.CleanupInterruptedImportsAsync(fixture.DirectoryPath, token);
        Assert.False(Directory.Exists(staging));
        Assert.False(Directory.Exists(orphan));
        Assert.True(File.Exists(Path.Combine(fixture.DirectoryPath, relative)));
    }
}
