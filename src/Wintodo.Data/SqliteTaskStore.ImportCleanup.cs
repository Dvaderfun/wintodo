using Microsoft.Data.Sqlite;

namespace Wintodo.Data;

public sealed partial class SqliteTaskStore
{
    public const string DefaultDatabaseFileName = "wintodo.db";

    /// <summary>Recovers files abandoned by a terminated import in the default database's own root.</summary>
    public Task CleanupInterruptedImportsAsync(string attachmentRoot, CancellationToken cancellationToken = default)
    {
        string root = Path.GetFullPath(attachmentRoot);
        // Import recovery deletes by absence from the DB, so an override must
        // never use the package's index of attachments under a different DB.
        if (!string.Equals(_databasePath, Path.Combine(root, DefaultDatabaseFileName), StringComparison.Ordinal))
            return Task.CompletedTask;
        return ExecuteRollbackAsync((connection, transaction) =>
        {
            string staging = Path.Combine(root, "import-staging");
            if (Directory.Exists(staging) && (File.GetAttributes(staging) & FileAttributes.ReparsePoint) == 0)
                Directory.Delete(staging, recursive: true);
            var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT DISTINCT t.uid FROM tasks t JOIN attachments a ON a.task_id = t.id;";
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read()) referenced.Add(reader.GetString(0));
            string files = Path.Combine(root, "attachments");
            if (Directory.Exists(files) && (File.GetAttributes(files) & FileAttributes.ReparsePoint) == 0)
            {
                foreach (string folder in Directory.EnumerateDirectories(files))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string resolved = Path.GetFullPath(folder);
                    if (resolved.StartsWith(files + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                        && Guid.TryParse(Path.GetFileName(folder), out _) && !referenced.Contains(Path.GetFileName(folder))
                        && (File.GetAttributes(folder) & FileAttributes.ReparsePoint) == 0)
                        Directory.Delete(resolved, recursive: true);
                }
            }
            return 0;
        }, cancellationToken);
    }
}
