namespace Wintodo.Core.Ical;

public sealed class IcalImportResult
{
    public int CreatedListCount { get; init; }

    public int CreatedTaskCount { get; init; }

    public int UpdatedTaskCount { get; init; }

    public IReadOnlyList<long> ListIds { get; init; } = [];
}
