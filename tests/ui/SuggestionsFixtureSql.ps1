# A second writer for the Suggestions gates' disposable guest databases.
function Invoke-SuggestionsFixtureSql([string]$Database, [string]$Sql) {
    $fixtureRoot = 'C:\wintodo\tests\ui\artifacts\'
    $path = [IO.Path]::GetFullPath($Database)
    if ($env:WINTODO_ALLOW_LOCAL_UI -ne '1' -or $PSScriptRoot -ine 'C:\wintodo\tests\ui' -or
        !$path.StartsWith($fixtureRoot, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetExtension($path) -ine '.db' -or !(Test-Path -LiteralPath $path -PathType Leaf)) {
        throw 'Expected an existing, disposable UI gate database in WintodoTest.'
    }
    if (!('SuggestionsFixtureSql' -as [type])) {
        Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class SuggestionsFixtureSql {
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Row(IntPtr context, int count, IntPtr values, IntPtr names);
    [DllImport("winsqlite3", CallingConvention=CallingConvention.Cdecl)]
    private static extern int sqlite3_open_v2([MarshalAs(UnmanagedType.LPUTF8Str)] string path, out IntPtr db, int flags, IntPtr vfs);
    [DllImport("winsqlite3", CallingConvention=CallingConvention.Cdecl)]
    private static extern int sqlite3_exec(IntPtr db, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, Row row, IntPtr context, IntPtr err);
    [DllImport("winsqlite3", CallingConvention=CallingConvention.Cdecl)] private static extern IntPtr sqlite3_errmsg(IntPtr db);
    [DllImport("winsqlite3", CallingConvention=CallingConvention.Cdecl)] private static extern int sqlite3_busy_timeout(IntPtr db, int ms);
    [DllImport("winsqlite3", CallingConvention=CallingConvention.Cdecl)] private static extern int sqlite3_close(IntPtr db);
    public static string Execute(string path, string sql) {
        int rc = sqlite3_open_v2(path, out var db, 2, IntPtr.Zero); // read/write; never create
        try {
            if (rc != 0) throw new InvalidOperationException("Cannot open fixture: " + rc);
            sqlite3_busy_timeout(db, 5000);
            string scalar = null; int rows = 0;
            Row collect = (_, count, values, names) => { rows++; scalar = Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(values)); return 0; };
            rc = sqlite3_exec(db, sql, collect, IntPtr.Zero, IntPtr.Zero);
            if (rc != 0) throw new InvalidOperationException(Marshal.PtrToStringUTF8(sqlite3_errmsg(db)));
            if (rows > 1) throw new InvalidOperationException("Expected at most one fixture result row.");
            return scalar;
        } finally { if (db != IntPtr.Zero) sqlite3_close(db); }
    }
}
'@
    }
    [SuggestionsFixtureSql]::Execute($path, $Sql)
}
