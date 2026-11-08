using System.Text;
using Wintodo.Core.Ical;
using Wintodo.Core.Models;

namespace Wintodo.Core.Tests;

public sealed class IcalMoveRevisionTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void MoveAddsAuthoredRevisionAndParentWithoutDestroyingUnknownRelations()
    {
        var reader = new IcalReader(TimeZoneInfo.Utc);
        TodoTask task = Assert.Single(reader.Read(File.ReadAllBytes(FixturePath)).Tasks);
        string? unknownBefore = task.IcalUnknown;
        task.Sequence = 17;
        task.ParentUid = "new-parent";
        task.SortOrder = 2048;
        task.LastModified = Timestamp;
        var writer = new IcalWriter(TimeZoneInfo.Utc);

        byte[] payload = writer.WriteTaskMoveCalendar(new TodoList(), task, Timestamp);
        TodoTask moved = Assert.Single(reader.Read(payload).Tasks);

        Assert.Equal(17, moved.Sequence);
        Assert.Equal("new-parent", moved.ParentUid);
        Assert.Equal(2048, moved.SortOrder);
        Assert.Equal(Timestamp, moved.LastModified);
        Assert.Equal(unknownBefore, task.IcalUnknown);
        AssertPreservedRelations(payload);
        Assert.Equal(0, Assert.Single(reader.Read(writer.WriteCalendar(new TodoList(), [task], Timestamp)).Tasks).Sequence);
    }

    [Fact]
    public void MoveRetainsNewlyAuthoredFieldsAbsentFromImportedPresenceMetadata()
    {
        var reader = new IcalReader(TimeZoneInfo.Utc);
        TodoTask task = Assert.Single(reader.Read(File.ReadAllBytes(FixturePath)).Tasks);
        task.Description = "Synthetic newly added note";
        task.Due = Timestamp.AddDays(1);
        task.Priority = 1;
        task.Sequence = 1;
        task.IsDirty = true;

        TodoTask moved = Assert.Single(reader.Read(new IcalWriter(TimeZoneInfo.Utc)
            .WriteTaskMoveCalendar(new TodoList(), task, Timestamp)).Tasks);

        Assert.Equal(task.Description, moved.Description);
        Assert.Equal(task.Due, moved.Due);
        Assert.Equal(task.Priority, moved.Priority);
    }

    [Fact]
    public void MoveUpdatesCategoryValuesWithoutDroppingPreservedParameters()
    {
        var reader = new IcalReader(TimeZoneInfo.Utc);
        string input = File.ReadAllText(FixturePath).Replace("SUMMARY:Synthetic move",
            "SUMMARY:Synthetic move\nCATEGORIES;LANGUAGE=en-US;X-VENDOR=\"opaque:retained\":old", StringComparison.Ordinal);
        TodoTask task = Assert.Single(reader.Read(input).Tasks);
        task.Categories.Clear();
        task.Categories.Add("new,one");
        task.Categories.Add("second");
        task.Sequence = 1;

        byte[] payload = new IcalWriter(TimeZoneInfo.Utc).WriteTaskMoveCalendar(new TodoList(), task, Timestamp);

        Assert.Equal(task.Categories, Assert.Single(reader.Read(payload).Tasks).Categories);
        Assert.Contains("LANGUAGE=en-US;X-VENDOR=\"opaque:retained\":new\\,one,second",
            Encoding.UTF8.GetString(payload), StringComparison.Ordinal);
        AssertPreservedRelations(payload);
    }

    [Fact]
    public void PromotedStepRemovesOnlyModeledParentRelation()
    {
        var reader = new IcalReader(TimeZoneInfo.Utc);
        string withParent = File.ReadAllText(FixturePath).Replace("RELATED-TO;RELTYPE=SIBLING", "RELATED-TO;RELTYPE=PARENT:old-parent\nRELATED-TO;RELTYPE=SIBLING", StringComparison.Ordinal);
        TodoTask task = Assert.Single(reader.Read(withParent).Tasks);
        Assert.Equal("old-parent", task.ParentUid);
        task.ParentUid = null;
        task.Sequence = 3;

        byte[] payload = new IcalWriter(TimeZoneInfo.Utc).WriteTaskMoveCalendar(new TodoList(), task, Timestamp);

        Assert.Null(Assert.Single(reader.Read(payload).Tasks).ParentUid);
        Assert.DoesNotContain("RELTYPE=PARENT", Encoding.UTF8.GetString(payload), StringComparison.Ordinal);
        AssertPreservedRelations(payload);
    }

    private static void AssertPreservedRelations(byte[] payload)
    {
        string text = Encoding.UTF8.GetString(payload);
        Assert.Contains("RELATED-TO;RELTYPE=SIBLING:sibling-uid", text, StringComparison.Ordinal);
        Assert.Contains("RELATED-TO;RELTYPE=X-OPAQUE;X-VENDOR=retained:opaque-uid", text, StringComparison.Ordinal);
        Assert.Contains("X-SYNTHETIC-MOVE-UNKNOWN:retained", text, StringComparison.Ordinal);
    }

    private static string FixturePath => Path.Combine(AppContext.BaseDirectory,
        "fixtures", "ical", "task-move-missing-revision.ics");
}
