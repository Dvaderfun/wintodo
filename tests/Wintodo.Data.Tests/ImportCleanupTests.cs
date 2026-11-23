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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RecoveryNeverUsesAnUnrelatedAttachmentRoot(bool scratchHasTasks)
    {
        var token = TestContext.Current.CancellationToken;
        await using StoreFixture primary = await StoreFixture.CreateAsync();
        await using StoreFixture scratch = await StoreFixture.CreateAsync();
        string primaryFile = await SeedReferencedAttachmentAsync(primary, token);
        string? scratchFile = scratchHasTasks ? await SeedReferencedAttachmentAsync(scratch, token) : null;
        string primaryStage = await WriteSentinelAsync(primary.DirectoryPath, "import-staging", token);
        string scratchStage = await WriteSentinelAsync(scratch.DirectoryPath, "import-staging", token);
        string scratchOrphan = await WriteSentinelAsync(
            Path.Combine(scratch.DirectoryPath, "attachments"), Guid.NewGuid().ToString("D"), token);

        await scratch.Store.CleanupInterruptedImportsAsync(primary.DirectoryPath, token);
        await scratch.Store.CleanupInterruptedImportsAsync(scratch.DirectoryPath, token);

        Assert.Equal("sentinel", await File.ReadAllTextAsync(primaryFile, token));
        Assert.Equal("sentinel", await File.ReadAllTextAsync(primaryStage, token));
        Assert.False(File.Exists(scratchStage));
        Assert.False(File.Exists(scratchOrphan));
        if (scratchFile is not null) Assert.Equal("sentinel", await File.ReadAllTextAsync(scratchFile, token));
    }

    private static async Task<string> SeedReferencedAttachmentAsync(StoreFixture fixture, CancellationToken token)
    {
        var list = await fixture.Store.EnsureDefaultListAsync("Tasks", token);
        var task = await fixture.Store.CreateTaskAsync(new TodoTask { ListId = list.Id, Summary = "Keep" }, token);
        string path = await WriteSentinelAsync(Path.Combine(fixture.DirectoryPath, "attachments"), task.Uid, token);
        task.Attachments.Add(new TaskAttachment
        {
            FileName = Path.GetFileName(path),
            RelativePath = Path.GetRelativePath(fixture.DirectoryPath, path),
            SizeBytes = 8,
        });
        await fixture.Store.UpdateTaskAsync(task, token);
        return path;
    }

    private static async Task<string> WriteSentinelAsync(string root, string directory, CancellationToken token)
    {
        string folder = Path.Combine(root, directory);
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "sentinel.txt");
        await File.WriteAllTextAsync(path, "sentinel", token);
        return path;
    }
}
