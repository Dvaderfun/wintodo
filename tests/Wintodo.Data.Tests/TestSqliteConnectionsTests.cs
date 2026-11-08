using Microsoft.Data.Sqlite;

namespace Wintodo.Data.Tests;

public sealed class TestSqliteConnectionsTests
{
    [Fact]
    public async Task FixtureDisposal_ClosesOwnedPoolsAndPreservesUnrelatedIdleHandle()
    {
        StoreFixture owned = await StoreFixture.CreateAsync();
        await using StoreFixture unrelated = await StoreFixture.CreateAsync();
        bool disposed = false;
        using SqliteConnection otherConnection = StorePoolConnection(unrelated.DatabasePath, SqliteOpenMode.ReadOnly);
        otherConnection.Open();
        SQLitePCL.sqlite3 otherHandle = Assert.IsType<SQLitePCL.sqlite3>(otherConnection.Handle);
        otherConnection.Close();
        using SqliteConnection ownedReader = StorePoolConnection(owned.DatabasePath, SqliteOpenMode.ReadOnly);
        using SqliteConnection ownedWriter = StorePoolConnection(owned.DatabasePath, SqliteOpenMode.ReadWriteCreate);
        ownedReader.Open();
        SQLitePCL.sqlite3 readerHandle = Assert.IsType<SQLitePCL.sqlite3>(ownedReader.Handle);
        ownedReader.Close();
        ownedWriter.Open();
        SQLitePCL.sqlite3 writerHandle = Assert.IsType<SQLitePCL.sqlite3>(ownedWriter.Handle);
        ownedWriter.Close();
        try
        {
            Assert.False(otherHandle.IsClosed);
            await owned.DisposeAsync();
            disposed = true;

            Assert.True(readerHandle.IsClosed);
            Assert.True(writerHandle.IsClosed);
            Assert.False(Directory.Exists(owned.DirectoryPath));
            Assert.False(otherHandle.IsClosed);
            otherConnection.Open();
            Assert.Same(otherHandle, otherConnection.Handle);
            using SqliteCommand command = otherConnection.CreateCommand();
            command.CommandText = "SELECT 1;";
            Assert.Equal(1L, command.ExecuteScalar());
            otherConnection.Close();
        }
        finally
        {
            if (!disposed) await owned.DisposeAsync();
        }
    }

    [Theory]
    [InlineData(SqliteOpenMode.ReadWriteCreate)]
    [InlineData(SqliteOpenMode.ReadOnly)]
    public async Task HelperConnection_DisposesItsHandleAndEscapesTheSelectedPath(SqliteOpenMode mode)
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        string path = Path.Combine(fixture.DirectoryPath, "tasks with spaces;Mode=Memory.db");
        using (SqliteConnection seed = TestSqliteConnections.Create(path))
        {
            seed.Open();
            using SqliteCommand command = seed.CreateCommand();
            command.CommandText = "CREATE TABLE marker(value INTEGER); INSERT INTO marker(value) VALUES(7);";
            command.ExecuteNonQuery();
        }

        SQLitePCL.sqlite3 handle;
        using (SqliteConnection connection = TestSqliteConnections.Create(path, mode))
        {
            connection.Open();
            Assert.Equal(path, connection.DataSource);
            handle = Assert.IsType<SQLitePCL.sqlite3>(connection.Handle);
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT value FROM marker;";
            Assert.Equal(7L, command.ExecuteScalar());
        }

        Assert.True(handle.IsClosed);
        Assert.True(File.Exists(path));
        File.Delete(path);
        Assert.False(File.Exists(path));
    }

    private static SqliteConnection StorePoolConnection(string path, SqliteOpenMode mode)
    {
        return new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(path),
            Mode = mode,
            Cache = SqliteCacheMode.Shared,
            Pooling = true,
        }.ToString());
    }
}
