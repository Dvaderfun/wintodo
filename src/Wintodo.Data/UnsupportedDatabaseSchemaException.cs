namespace Wintodo.Data;

public sealed class UnsupportedDatabaseSchemaException : Exception
{
    public UnsupportedDatabaseSchemaException(long databaseVersion, int supportedVersion)
        : base("The database schema is newer than this version of the app supports.")
    {
        DatabaseVersion = databaseVersion;
        SupportedVersion = supportedVersion;
    }

    public long DatabaseVersion { get; }

    public int SupportedVersion { get; }
}
