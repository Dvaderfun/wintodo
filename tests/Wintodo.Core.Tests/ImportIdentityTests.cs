using System.Text;
using Wintodo.Core.Ical;
using Wintodo.Core.Import;
using Wintodo.Core.Models;

namespace Wintodo.Core.Tests;

public sealed class ImportIdentityTests
{
    [Fact]
    public void DeterministicUid_IsStableVersion5AndScopedPerAccountAndType()
    {
        string first = ImportIdentity.DeterministicUid("mstodo", "user-1", ImportEntityType.Task, "AAMk=1");
        string again = ImportIdentity.DeterministicUid("mstodo", "user-1", ImportEntityType.Task, "AAMk=1");
        string otherAccount = ImportIdentity.DeterministicUid("mstodo", "user-2", ImportEntityType.Task, "AAMk=1");
        string otherType = ImportIdentity.DeterministicUid("mstodo", "user-1", ImportEntityType.Step, "AAMk=1");

        Assert.Equal(first, again);
        Assert.NotEqual(first, otherAccount);
        Assert.NotEqual(first, otherType);
        Guid parsed = Guid.Parse(first);
        Assert.Equal(first, parsed.ToString("D"));
        Assert.Equal('5', first[14]);
        Assert.Contains(first[19], "89ab");
    }

    [Theory]
    [InlineData("  Buy   milk ", "buy milk")]
    [InlineData("Buy\tMILK", "buy milk")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void NormalizeTitle_CollapsesWhitespaceAndCase(string? title, string expected)
    {
        Assert.Equal(expected, ImportIdentity.NormalizeTitle(title));
    }

    [Fact]
    public void Fingerprint_UsesSecondsAndNormalizedTitle()
    {
        var created = new DateTimeOffset(2026, 9, 1, 10, 15, 30, 123, TimeSpan.FromHours(2));

        Assert.Equal(
            "2026-09-01T08:15:30Z|water the plants",
            ImportIdentity.Fingerprint(created, " Water  the plants"));
        Assert.Equal(
            ImportIdentity.Fingerprint(created, "Water the plants"),
            ImportIdentity.Fingerprint(created.AddMilliseconds(500), "water THE plants"));
    }

    [Fact]
    public void ExtensionProperties_RoundTripEscapedFoldedValues()
    {
        string longValue = string.Concat(Enumerable.Repeat("Notes, with; specials\\ and ünïcode ", 6)) + "\nline two";
        string? unknown = IcalExtensionProperties.Merge(
            null,
            [
                new("X-MSTODO-ID", "AAMk=="),
                new("X-ALT-DESC", longValue, [new KeyValuePair<string, string>("FMTTYPE", "text/html")]),
            ],
            []);

        Assert.Equal(["AAMk=="], IcalExtensionProperties.ReadValues(unknown, "X-MSTODO-ID"));
        Assert.Equal([longValue], IcalExtensionProperties.ReadValues(unknown, "x-alt-desc"));
    }

    [Fact]
    public void Overlay_ReplacesManagedPropertiesAndKeepsOtherClientsProperties()
    {
        string? local = IcalExtensionProperties.Merge(
            null,
            [new("X-MSTODO-ID", "old"), new("X-NEXTCLOUD-PINNED", "true")],
            []);
        string? incoming = IcalExtensionProperties.Merge(null, [new("X-MSTODO-ID", "new")], []);

        string? merged = IcalExtensionProperties.Overlay(local, incoming, ["X-MSTODO-ID"]);

        Assert.Equal(["new"], IcalExtensionProperties.ReadValues(merged, "X-MSTODO-ID"));
        Assert.Equal(["true"], IcalExtensionProperties.ReadValues(merged, "X-NEXTCLOUD-PINNED"));
    }

    [Fact]
    public void ExtensionProperties_AreExportedAndReimportedWithTheTask()
    {
        var task = new TodoTask
        {
            Uid = "uid-1",
            Summary = "Imported",
            Created = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            LastModified = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero),
            IcalUnknown = IcalExtensionProperties.Merge(
                null,
                [new("X-MSTODO-STATUS", "waitingOnOthers")],
                []),
        };
        var calendar = new IcalCalendar { Name = "List" };
        calendar.Tasks.Add(task);

        byte[] exported = new IcalWriter(TimeZoneInfo.Utc)
            .WriteCalendar(calendar, new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero));
        Assert.Contains("X-MSTODO-STATUS:waitingOnOthers\r\n", Encoding.UTF8.GetString(exported), StringComparison.Ordinal);

        TodoTask reimported = new IcalReader(TimeZoneInfo.Utc).Read(exported).Tasks.Single();
        Assert.Equal(["waitingOnOthers"], IcalExtensionProperties.ReadValues(reimported.IcalUnknown, "X-MSTODO-STATUS"));
    }
}
