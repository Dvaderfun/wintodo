using System.Net;
using System.Text;
using Wintodo.Core.Import;
using Wintodo.Core.Models;
using Wintodo.Core.Queries;
using Wintodo.Import.MicrosoftTodo;

namespace Wintodo.Import.Tests;

public sealed class AttachmentProvenanceTests
{
    private const string InternetZone = "[ZoneTransfer]\r\nZoneId=3\r\n";
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task DownloadedAttachmentHasOnlyInternetZoneMetadataAndUnchangedBytes()
    {
        await using TempStore temp = await TempStore.CreateAsync();
        ImportSnapshot snapshot = Snapshot(temp.Clock.UtcNow, "document.txt");
        using var http = new HttpClient(new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes("Synthetic benign document\n")),
        }));
        using var client = new GraphTodoClient(http, "synthetic-token");

        await new MicrosoftTodoReader(client).DownloadAttachmentsAsync(snapshot,
            Path.Combine(temp.Directory, "stage"), new HashSet<string> { "attachment" }, cancellationToken: Token);

        ImportAttachment attachment = Assert.Single(Assert.Single(snapshot.Lists).Tasks).Attachments.Single();
        Assert.Equal("Synthetic benign document\n", await File.ReadAllTextAsync(attachment.StagedPath!, Token));
        if (OperatingSystem.IsWindows())
            Assert.Equal(InternetZone, await File.ReadAllTextAsync(attachment.StagedPath + ":Zone.Identifier", Token));
    }

    [Theory]
    [InlineData("document.txt")]
    [InlineData("document.pdf")]
    [InlineData("document.csv")]
    public async Task FinalCopyAppliesInternetZoneEvenWhenStagingMetadataWasLost(string fileName)
    {
        await using TempStore temp = await TempStore.CreateAsync();
        ImportSnapshot snapshot = Snapshot(temp.Clock.UtcNow, fileName);
        string staged = Path.Combine(temp.Directory, "staged");
        await File.WriteAllTextAsync(staged, "Synthetic benign attachment", Token);
        Assert.Single(Assert.Single(snapshot.Lists).Tasks).Attachments.Single().StagedPath = staged;

        ImportOutcome result = await temp.Store.ApplyImportAsync(snapshot,
            new ImportOptions { AttachmentRoot = temp.Directory }, Token);

        Assert.Equal(1, result.NewAttachments);
        TodoList list = (await temp.Store.GetListsAsync(Token)).Single(item => item.IsDefault);
        TodoTask reference = Assert.Single(await temp.Store.GetTasksAsync(ListQuery.ForList(list.Id), Token));
        TodoTask task = (await temp.Store.GetTaskAsync(reference.Uid, Token))!;
        TaskAttachment attachment = Assert.Single(task.Attachments);
        string finalPath = Path.Combine(temp.Directory, attachment.RelativePath);
        Assert.Equal("Synthetic benign attachment", await File.ReadAllTextAsync(finalPath, Token));
        if (OperatingSystem.IsWindows())
            Assert.Equal(InternetZone, await File.ReadAllTextAsync(finalPath + ":Zone.Identifier", Token));
    }

    [Fact]
    public async Task FailedImportRemovesEarlierCopiedFilesAndTheirZoneMetadata()
    {
        await using TempStore temp = await TempStore.CreateAsync();
        ImportSnapshot snapshot = Snapshot(temp.Clock.UtcNow, "document.txt");
        string staged = Path.Combine(temp.Directory, "staged");
        await File.WriteAllTextAsync(staged, "Synthetic benign attachment", Token);
        ImportTask task = Assert.Single(Assert.Single(snapshot.Lists).Tasks);
        task.Attachments.Single().StagedPath = staged;
        task.Attachments.Add(new ImportAttachment
        {
            ExternalId = "missing", FileName = "missing.txt", StagedPath = Path.Combine(temp.Directory, "missing"),
        });
        int events = 0;
        temp.Store.StoreChanged += (_, _) => events++;

        await Assert.ThrowsAsync<FileNotFoundException>(() => temp.Store.ApplyImportAsync(snapshot,
            new ImportOptions { AttachmentRoot = temp.Directory }, Token));

        Assert.Empty(Directory.GetFiles(Path.Combine(temp.Directory, "attachments"), "*", SearchOption.AllDirectories));
        Assert.Empty(await temp.Store.GetImportSourcesAsync("mstodo", Token));
        TodoList list = (await temp.Store.GetListsAsync(Token)).Single(item => item.IsDefault);
        Assert.Empty(await temp.Store.GetTasksAsync(ListQuery.ForList(list.Id), Token));
        Assert.Equal(0, events);
        Assert.True(File.Exists(staged));
    }

    [Fact]
    public async Task FailedDownloadLeavesNoAttachmentOrZoneMetadata()
    {
        await using TempStore temp = await TempStore.CreateAsync();
        ImportSnapshot snapshot = Snapshot(temp.Clock.UtcNow, "document.txt");
        using var content = new ByteArrayContent([1, 2]);
        content.Headers.ContentLength = 10;
        using var http = new HttpClient(new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = content }));
        using var client = new GraphTodoClient(http, "synthetic-token");
        string stage = Path.Combine(temp.Directory, "stage");

        await Assert.ThrowsAsync<InvalidDataException>(() => new MicrosoftTodoReader(client).DownloadAttachmentsAsync(
            snapshot, stage, new HashSet<string> { "attachment" }, cancellationToken: Token));

        Assert.Empty(Directory.GetFiles(stage));
        Assert.Null(Assert.Single(Assert.Single(snapshot.Lists).Tasks).Attachments.Single().StagedPath);
        if (OperatingSystem.IsWindows())
            Assert.False(File.Exists(Path.Combine(stage, "0") + ":Zone.Identifier"));
    }

    [Fact]
    public async Task ZoneWriteIoFailureFailsDownloadAndRemovesOnlyItsOwnedFile()
    {
        if (!OperatingSystem.IsWindows()) return;
        await using TempStore temp = await TempStore.CreateAsync();
        string stage = Path.Combine(temp.Directory, "stage");
        Directory.CreateDirectory(stage);
        string existing = Path.Combine(stage, "existing.txt");
        await File.WriteAllTextAsync(existing, "Keep existing attachment", Token);
        FileStream? lockedZone = null;
        using var http = new HttpClient(new ScriptedHandler(_ =>
        {
            // The reader has created its numbered file. Lock only the zone stream
            // while allowing deletion, so marking fails without blocking cleanup.
            lockedZone = new FileStream(Path.Combine(stage, "0") + ":Zone.Identifier",
                FileMode.Create, FileAccess.ReadWrite, FileShare.Delete);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2]) };
        }));
        using var client = new GraphTodoClient(http, "synthetic-token");
        try
        {
            await Assert.ThrowsAsync<IOException>(() => new MicrosoftTodoReader(client).DownloadAttachmentsAsync(
                Snapshot(temp.Clock.UtcNow, "document.txt"), stage, new HashSet<string> { "attachment" }, cancellationToken: Token));
            Assert.False(File.Exists(Path.Combine(stage, "0")));
            Assert.False(File.Exists(Path.Combine(stage, "0") + ":Zone.Identifier"));
            Assert.Equal("Keep existing attachment", await File.ReadAllTextAsync(existing, Token));
        }
        finally
        {
            lockedZone?.Dispose();
        }
    }

    [Fact]
    public async Task ZoneWritePermissionFailureIsNotTreatedAsUnsupportedFilesystem()
    {
        if (!OperatingSystem.IsWindows()) return;
        await using TempStore temp = await TempStore.CreateAsync();
        string path = Path.Combine(temp.Directory, "readonly.txt");
        await File.WriteAllTextAsync(path, "Synthetic benign attachment", Token);
        File.SetAttributes(path, FileAttributes.ReadOnly);
        try
        {
            Assert.Throws<UnauthorizedAccessException>(() => DownloadedAttachmentProvenance.MarkInternet(path));
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }
    }

    [Fact]
    public async Task MissingAttachmentCannotBeCreatedByWritingItsZoneStream()
    {
        if (!OperatingSystem.IsWindows()) return;
        await using TempStore temp = await TempStore.CreateAsync();
        string path = Path.Combine(temp.Directory, "missing.txt");
        Assert.Throws<FileNotFoundException>(() => DownloadedAttachmentProvenance.MarkInternet(path));
        Assert.False(File.Exists(path));
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("document.txt:alternate")]
    public async Task ImportedFilenameCannotEscapeOrSelectAnAlternateStream(string fileName)
    {
        await using TempStore temp = await TempStore.CreateAsync();
        ImportSnapshot snapshot = Snapshot(temp.Clock.UtcNow, fileName);
        string staged = Path.Combine(temp.Directory, "staged");
        await File.WriteAllTextAsync(staged, "Synthetic benign attachment", Token);
        Assert.Single(Assert.Single(snapshot.Lists).Tasks).Attachments.Single().StagedPath = staged;

        await temp.Store.ApplyImportAsync(snapshot, new ImportOptions { AttachmentRoot = temp.Directory }, Token);

        string root = Path.Combine(temp.Directory, "attachments");
        string finalPath = Assert.Single(Directory.GetFiles(root, "*", SearchOption.AllDirectories));
        Assert.StartsWith(root + Path.DirectorySeparatorChar, finalPath, StringComparison.Ordinal);
        Assert.DoesNotContain(Path.DirectorySeparatorChar, Path.GetFileName(finalPath));
        if (OperatingSystem.IsWindows())
        {
            Assert.DoesNotContain(':', Path.GetFileName(finalPath));
            Assert.Equal(InternetZone, await File.ReadAllTextAsync(finalPath + ":Zone.Identifier", Token));
        }
        Assert.False(File.Exists(Path.Combine(temp.Directory, "outside.txt")));
    }

    private static ImportSnapshot Snapshot(DateTimeOffset now, string fileName)
    {
        var snapshot = new ImportSnapshot { Provider = "mstodo", AccountKey = "synthetic-account" };
        var list = new ImportList { ExternalId = "list", DisplayName = "Tasks", Kind = ImportListKind.Default };
        var task = new ImportTask
        {
            ExternalId = "task", Task = new TodoTask { Summary = "Synthetic attachment task", Created = now, LastModified = now },
        };
        task.Attachments.Add(new ImportAttachment
        {
            ExternalId = "attachment", FileName = fileName, SourcePath = "list\ntask\nattachment",
        });
        list.Tasks.Add(task);
        snapshot.Lists.Add(list);
        return snapshot;
    }
}
