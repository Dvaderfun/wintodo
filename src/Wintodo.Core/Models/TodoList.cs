namespace Wintodo.Core.Models;

public sealed class TodoList
{
    public long Id { get; set; }

    public string Uid { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public ListColor Color { get; set; }

    public string? BackgroundTheme { get; set; }

    public long? GroupId { get; set; }

    public double SortOrder { get; set; }

    public SortMode SortMode { get; set; }

    public bool SortDescending { get; set; }

    public bool ShowCompleted { get; set; } = true;

    public bool CompletedExpanded { get; set; }

    public bool IsDeleted { get; set; }

    public DateTimeOffset Created { get; set; }

    public DateTimeOffset Modified { get; set; }

    public long? AccountId { get; set; }

    public string? RemoteUrl { get; set; }

    public string? RemoteCTag { get; set; }

    public string? SyncToken { get; set; }
}
