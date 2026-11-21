using Microsoft.Data.Sqlite;
using Wintodo.Core.Models;
using Wintodo.ViewModels;

namespace Wintodo.Data.Tests;

public sealed class DetailAttachmentCopyTests
{
    [Theory]
    [InlineData("success")]
    [InlineData("copy failure")]
    [InlineData("write failure")]
    [InlineData("over limit at commit")]
    public async Task SelectionChangeDoesNotChangeCopyOwner(string outcome)
    {
        var ct = TestContext.Current.CancellationToken;
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        var list = await fixture.Store.EnsureDefaultListAsync("Tasks", ct);
        var a = await fixture.Store.CreateTaskAsync(new TodoTask { ListId = list.Id, Summary = "A" }, ct);
        var b = new TodoTask { ListId = list.Id, Summary = "B", IcalUnknown = "X-FIXTURE:keep" };
        b.Attachments.Add(new TaskAttachment { FileName = "existing.txt", RelativePath = "existing.txt", SizeBytes = 3 });
        await fixture.Store.CreateTaskAsync(b, ct);
        int initialBSequence = b.Sequence;
        string sibling = Path.Combine(fixture.DirectoryPath, "attachments", a.Uid, "existing.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(sibling)!);
        await File.WriteAllTextAsync(sibling, "A sentinel", ct);
        string unrelated = Path.Combine(fixture.DirectoryPath, "existing.txt");
        await File.WriteAllTextAsync(unrelated, "old", ct);
        if (outcome == "write failure")
        {
            using var connection = TestSqliteConnections.Create(fixture.DatabasePath);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TRIGGER reject_fixture_attachment BEFORE INSERT ON attachments
                BEGIN SELECT RAISE(ABORT, 'fixture write failed'); END;
                """;
            command.ExecuteNonQuery();
        }

        // Capture the current selection before starting any file I/O.
        TodoTask selected = a;
        var copyStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCopy = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        const string name = "fixture.txt";
        string? output = null;
        var operation = DetailAttachmentCopy.CopyAsync(selected, remainingBytes: 1024,
            async uid =>
            {
                string relative = Path.Combine("attachments", uid, name);
                string path = Path.Combine(fixture.DirectoryPath, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllTextAsync(path, "partial", ct);
                output = path;
                return new AttachmentCopyTarget(async () =>
                {
                    copyStarted.SetResult();
                    await releaseCopy.Task.WaitAsync(ct);
                    if (outcome == "copy failure") throw new IOException("fixture copy failed");
                    await File.WriteAllTextAsync(path, "fixture", ct);
                    return new TaskAttachment { FileName = name, RelativePath = relative, SizeBytes = 7 };
                }, () =>
                {
                    File.Delete(path);
                    return Task.CompletedTask;
                });
            },
            (owner, attachment) => fixture.Store.TryAddTaskAttachmentAsync(
                owner.Uid, attachment, outcome == "over limit at commit" ? 6 : 1024, ct));

        await copyStarted.Task.WaitAsync(ct);
        selected = b;
        Assert.Same(b, selected);
        releaseCopy.SetResult();
        if (outcome == "success") Assert.True(await operation);
        else if (outcome == "copy failure") await Assert.ThrowsAsync<IOException>(() => operation);
        else if (outcome == "write failure") await Assert.ThrowsAsync<SqliteException>(() => operation);
        else Assert.False(await operation);

        await fixture.Store.DisposeAsync();
        await using var reopened = new SqliteTaskStore(fixture.DatabasePath, fixture.Clock);
        await reopened.InitializeAsync(ct);
        var savedA = (await reopened.GetTaskAsync(a.Uid, ct))!;
        var savedB = (await reopened.GetTaskAsync(b.Uid, ct))!;
        Assert.Equal("B", savedB.Summary);
        Assert.Equal("X-FIXTURE:keep", savedB.IcalUnknown);
        Assert.Equal(initialBSequence, savedB.Sequence);
        Assert.Equal("existing.txt", Assert.Single(savedB.Attachments).RelativePath);
        Assert.Equal("old", await File.ReadAllTextAsync(unrelated, ct));
        Assert.Equal("A sentinel", await File.ReadAllTextAsync(sibling, ct));
        Assert.Equal(Path.Combine(fixture.DirectoryPath, "attachments", a.Uid, name), output);
        if (outcome == "success")
        {
            var attached = Assert.Single(savedA.Attachments);
            Assert.Equal(Path.Combine("attachments", a.Uid, name), attached.RelativePath);
            Assert.Equal("fixture", await File.ReadAllTextAsync(output!, ct));
        }
        else
        {
            Assert.Empty(savedA.Attachments);
            Assert.False(File.Exists(output));
        }
    }
}
