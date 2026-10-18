using System.Globalization;

namespace Wintodo.Data;

public sealed partial class SqliteTaskStore
{
    /// <summary>Connection-local SQLite version: changes only when another connection commits.</summary>
    public Task<long> GetExternalChangeVersionAsync(CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        return Task.Run(() =>
        {
            _writeGate.Wait(cancellationToken);
            try
            {
                using var command = _writer!.CreateCommand();
                command.CommandText = "PRAGMA data_version;";
                return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
            }
            finally { _writeGate.Release(); }
        }, cancellationToken);
    }
}
