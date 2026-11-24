using Wintodo.Core.Models;
using Wintodo.ViewModels;

namespace Wintodo.Data.Tests;

public sealed class DetailStepDraftTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SwitchingDuringAnAddStoresTheStepUnderItsOwnerAndKeepsNewerDrafts(bool retypeSameText)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var fixture = await StoreFixture.CreateAsync();
        var list = await fixture.Store.EnsureDefaultListAsync("Tasks", ct);
        var alpha = await fixture.Store.CreateTaskAsync(new TodoTask { ListId = list.Id, Summary = "Alpha" }, ct);
        var bravo = await fixture.Store.CreateTaskAsync(new TodoTask { ListId = list.Id, Summary = "Bravo" }, ct);
        var drafts = new DetailStepDrafts();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        drafts.SetText(alpha.Uid, "  Alpha step  ");
        var saving = drafts.SubmitAsync(alpha.Uid, async (uid, title) =>
        {
            await release.Task.WaitAsync(ct);
            return await fixture.Store.AddStepAsync(uid, title, ct);
        });

        Assert.Empty(drafts.GetText(bravo.Uid));
        drafts.SetText(bravo.Uid, "Bravo draft");
        if (retypeSameText)
        {
            drafts.SetText(alpha.Uid, string.Empty);
            drafts.SetText(alpha.Uid, "  Alpha step  ");
        }
        release.SetResult();
        var written = Assert.IsType<TodoTask>(await saving);

        var stored = Assert.Single(await fixture.Store.GetChildrenAsync(alpha.Uid, ct));
        Assert.Equal(written.Uid, stored.Uid);
        Assert.Equal("Alpha step", stored.Summary);
        Assert.Empty(await fixture.Store.GetChildrenAsync(bravo.Uid, ct));
        Assert.Equal("Bravo draft", drafts.GetText(bravo.Uid));
        Assert.Equal(retypeSameText ? "  Alpha step  " : string.Empty, drafts.GetText(alpha.Uid));
    }

    [Fact]
    public async Task FailedAddLeavesTheUnsubmittedTextOnItsOriginalTask()
    {
        var drafts = new DetailStepDrafts();
        drafts.SetText("alpha", "try again");
        await Assert.ThrowsAsync<IOException>(async () =>
            await drafts.SubmitAsync("alpha", (_, _) => Task.FromException<TodoTask>(new IOException("save failed"))));
        Assert.Equal("try again", drafts.GetText("alpha"));
        Assert.Empty(drafts.GetText("bravo"));
    }
}
