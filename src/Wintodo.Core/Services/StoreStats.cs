namespace Wintodo.Core.Services;

public sealed class StoreStats
{
    public string DatabasePath { get; set; } = string.Empty;

    public int SchemaVersion { get; set; }

    public long ListCount { get; set; }

    public long TaskCount { get; set; }

    public long PendingChangeCount { get; set; }

    public long DatabaseSizeBytes { get; set; }

    public int ActiveReadConnections { get; set; }
}
