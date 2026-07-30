namespace Wintodo.Core.Models;

public sealed class ListGroup
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public double SortOrder { get; set; }

    public bool IsExpanded { get; set; } = true;

    public DateTimeOffset Created { get; set; }
}
