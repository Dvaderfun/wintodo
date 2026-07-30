namespace Wintodo.Core.Models;

public sealed class SavedView
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Query { get; set; } = string.Empty;

    public string? Icon { get; set; }

    public double SortOrder { get; set; }
}
