using Microsoft.Data.Sqlite;
using Wintodo.Core.Services;

namespace Wintodo.Data;

public sealed partial class SqliteTaskStore : ITaskStore
{
    private const string MigrationResourceName = "Wintodo.Data.Migrations.001_init.sql";
    private const string ConnectionPragmas = """
        PRAGMA journal_mode = WAL;
        PRAGMA synchronous = NORMAL;
        PRAGMA foreign_keys = ON;
        PRAGMA busy_timeout = 5000;
        PRAGMA temp_store = MEMORY;
        """;

    private readonly string _databasePath;
    private readonly string _readConnectionString;
    private readonly string _writeConnectionString;
    private readonly IClock _clock;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly SemaphoreSlim _readGate = new(4, 4);
    private SqliteConnection? _writer;
    private int _activeReadConnections;
    private int _initialized;
    private int _disposed;

    static SqliteTaskStore()
    {
        SQLitePCL.Batteries_V2.Init();
    }

    public SqliteTaskStore(string databasePath, IClock clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentNullException.ThrowIfNull(clock);

        _databasePath = Path.GetFullPath(databasePath);
        _clock = clock;
        _writeConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true,
        }.ToString();
        _readConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Shared,
            Pooling = true,
        }.ToString();
    }

    public event EventHandler<StoreChangedEventArgs>? StoreChanged;

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return Task.Run(
            () =>
            {
                _writeGate.Wait(cancellationToken);
                try
                {
                    if (Volatile.Read(ref _initialized) != 0)
                    {
                        return;
                    }

                    string? directory = Path.GetDirectoryName(_databasePath);
                    if (!string.IsNullOrEmpty(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    _writer = new SqliteConnection(_writeConnectionString);
                    _writer.Open();
                    ApplyPragmas(_writer);
                    ApplyMigrations(_writer);
                    Volatile.Write(ref _initialized, 1);
                }
                finally
                {
                    _writeGate.Release();
                }
            },
            cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _writeGate.WaitAsync().ConfigureAwait(false);
        try
        {
            for (int connection = 0; connection < 4; connection++)
            {
                await _readGate.WaitAsync().ConfigureAwait(false);
            }

            if (_writer is not null)
            {
                await _writer.DisposeAsync().ConfigureAwait(false);
                _writer = null;
            }
        }
        finally
        {
            _writeGate.Release();
            _writeGate.Dispose();
            _readGate.Dispose();
        }
    }

    private Task<T> ExecuteReadAsync<T>(
        Func<SqliteConnection, T> action,
        CancellationToken cancellationToken)
    {
        EnsureInitialized();
        return Task.Run(
            () =>
            {
                _readGate.Wait(cancellationToken);
                Interlocked.Increment(ref _activeReadConnections);
                try
                {
                    using var connection = new SqliteConnection(_readConnectionString);
                    connection.Open();
                    ApplyReadPragmas(connection);
                    return action(connection);
                }
                finally
                {
                    Interlocked.Decrement(ref _activeReadConnections);
                    _readGate.Release();
                }
            },
            cancellationToken);
    }

    private Task<T> ExecuteWriteAsync<T>(
        Func<SqliteConnection, SqliteTransaction, WriteContext, T> action,
        CancellationToken cancellationToken)
    {
        EnsureInitialized();
        return Task.Run(
            () =>
            {
                _writeGate.Wait(cancellationToken);
                WriteContext? context = null;
                bool committed = false;
                try
                {
                    SqliteConnection writer = _writer
                        ?? throw new InvalidOperationException("The store is not initialized.");
                    using SqliteTransaction transaction = writer.BeginTransaction();
                    context = new WriteContext();
                    T result = action(writer, transaction, context);
                    transaction.Commit();
                    committed = true;
                    return result;
                }
                finally
                {
                    _writeGate.Release();
                    if (committed && context is { HasChanges: true })
                    {
                        StoreChanged?.Invoke(
                            this,
                            new StoreChangedEventArgs(context.ListIds, context.TaskUids));
                    }
                }
            },
            cancellationToken);
    }

    private Task ExecuteWriteAsync(
        Action<SqliteConnection, SqliteTransaction, WriteContext> action,
        CancellationToken cancellationToken)
    {
        return ExecuteWriteAsync(
            (connection, transaction, context) =>
            {
                action(connection, transaction, context);
                return true;
            },
            cancellationToken);
    }

    private static void ApplyPragmas(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = ConnectionPragmas;
        command.ExecuteNonQuery();
    }

    private static void ApplyReadPragmas(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA foreign_keys = ON;
            PRAGMA busy_timeout = 5000;
            PRAGMA temp_store = MEMORY;
            """;
        command.ExecuteNonQuery();
    }

    private static void ApplyMigrations(SqliteConnection connection)
    {
        if (SchemaVersionTableExists(connection))
        {
            using SqliteCommand versionCommand = connection.CreateCommand();
            versionCommand.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_version;";
            long version = (long)(versionCommand.ExecuteScalar() ?? 0L);
            if (version >= 1)
            {
                return;
            }
        }

        using Stream stream = typeof(SqliteTaskStore).Assembly.GetManifestResourceStream(MigrationResourceName)
            ?? throw new InvalidOperationException($"Embedded migration '{MigrationResourceName}' was not found.");
        using var reader = new StreamReader(stream);
        string migrationSql = reader.ReadToEnd();
        using SqliteTransaction transaction = connection.BeginTransaction();
        using SqliteCommand migration = connection.CreateCommand();
        migration.Transaction = transaction;
        migration.CommandText = migrationSql;
        migration.ExecuteNonQuery();
        transaction.Commit();
    }

    private static bool SchemaVersionTableExists(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT EXISTS(
                SELECT 1
                FROM sqlite_master
                WHERE type = 'table' AND name = 'schema_version'
            );
            """;
        return Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) == 1;
    }

    private void EnsureInitialized()
    {
        ThrowIfDisposed();
        if (Volatile.Read(ref _initialized) == 0)
        {
            throw new InvalidOperationException("Call InitializeAsync before using the store.");
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
    }

    private sealed class WriteContext
    {
        internal HashSet<long> ListIds { get; } = [];

        internal HashSet<string> TaskUids { get; } = new(StringComparer.Ordinal);

        internal bool HasChanges => ListIds.Count > 0 || TaskUids.Count > 0;

        internal void AddList(long listId)
        {
            ListIds.Add(listId);
        }

        internal void AddTask(long listId, string taskUid)
        {
            ListIds.Add(listId);
            TaskUids.Add(taskUid);
        }
    }
}
