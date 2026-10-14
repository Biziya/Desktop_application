<#
  check-db.ps1 - look inside the CT007 measurement database. Read-only; nothing to download.
  Uses winsqlite3.dll, the SQLite library built into Windows 10/11.

  Summary (schema version, sessions, latest session's records, duplicate check):
      powershell -ExecutionPolicy Bypass -File .\check-db.ps1
  One session's records:
      powershell -ExecutionPolicy Bypass -File .\check-db.ps1 -Session 3f2a...
  Any read-only query (use ? for -Value):
      powershell -ExecutionPolicy Bypass -File .\check-db.ps1 -Sql "SELECT * FROM MeasurementSessions"
  Add -Grid to open results in a sortable window instead of the console.
#>
param(
    [string]$Session,
    [string]$Sql,
    [string]$Value,
    [switch]$Grid,
    [string]$Database = (Join-Path $env:LOCALAPPDATA 'CT007 Measurement Studio\measurements.db')
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $Database)) {
    throw "Database not found: $Database  (start the app once so it creates it)"
}

if (-not ('CtSqlite' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

public sealed class CtSqliteResult
{
    public string[] Columns;
    public List<object[]> Rows = new List<object[]>();
}

// Read-only access through winsqlite3.dll, the SQLite library built into Windows 10 and 11.
public static class CtSqlite
{
    const string Lib = "winsqlite3.dll";
    const CallingConvention Cc = CallingConvention.StdCall;
    [DllImport(Lib, CallingConvention = Cc)] static extern int sqlite3_open_v2(byte[] file, out IntPtr db, int flags, IntPtr vfs);
    [DllImport(Lib, CallingConvention = Cc)] static extern int sqlite3_close_v2(IntPtr db);
    [DllImport(Lib, CallingConvention = Cc)] static extern IntPtr sqlite3_errmsg(IntPtr db);
    [DllImport(Lib, CallingConvention = Cc)] static extern int sqlite3_prepare_v2(IntPtr db, byte[] sql, int n, out IntPtr stmt, IntPtr tail);
    [DllImport(Lib, CallingConvention = Cc)] static extern int sqlite3_bind_parameter_count(IntPtr stmt);
    [DllImport(Lib, CallingConvention = Cc)] static extern int sqlite3_bind_text(IntPtr stmt, int i, byte[] text, int n, IntPtr destructor);
    [DllImport(Lib, CallingConvention = Cc)] static extern int sqlite3_step(IntPtr stmt);
    [DllImport(Lib, CallingConvention = Cc)] static extern int sqlite3_finalize(IntPtr stmt);
    [DllImport(Lib, CallingConvention = Cc)] static extern int sqlite3_column_count(IntPtr stmt);
    [DllImport(Lib, CallingConvention = Cc)] static extern IntPtr sqlite3_column_name(IntPtr stmt, int i);
    [DllImport(Lib, CallingConvention = Cc)] static extern int sqlite3_column_type(IntPtr stmt, int i);
    [DllImport(Lib, CallingConvention = Cc)] static extern long sqlite3_column_int64(IntPtr stmt, int i);
    [DllImport(Lib, CallingConvention = Cc)] static extern double sqlite3_column_double(IntPtr stmt, int i);
    [DllImport(Lib, CallingConvention = Cc)] static extern IntPtr sqlite3_column_text(IntPtr stmt, int i);
    [DllImport(Lib, CallingConvention = Cc)] static extern int sqlite3_column_bytes(IntPtr stmt, int i);

    static byte[] Utf8(string s) { return Encoding.UTF8.GetBytes(s + "\0"); }

    static string Text(IntPtr p, int length = -1)
    {
        if (p == IntPtr.Zero) return null;
        if (length < 0) { length = 0; while (Marshal.ReadByte(p, length) != 0) length++; }
        var bytes = new byte[length];
        Marshal.Copy(p, bytes, 0, length);
        return Encoding.UTF8.GetString(bytes);
    }

    public static CtSqliteResult Query(string path, string sql, string parameter)
    {
        IntPtr db;
        int rc = sqlite3_open_v2(Utf8(path), out db, 1 /* SQLITE_OPEN_READONLY */, IntPtr.Zero);
        try
        {
            if (rc != 0) throw new Exception("Cannot open database: " + Text(sqlite3_errmsg(db)));
            IntPtr stmt;
            if (sqlite3_prepare_v2(db, Utf8(sql), -1, out stmt, IntPtr.Zero) != 0) throw new Exception(Text(sqlite3_errmsg(db)));
            try
            {
                if (sqlite3_bind_parameter_count(stmt) > 0)
                {
                    var value = Encoding.UTF8.GetBytes(parameter ?? "");
                    sqlite3_bind_text(stmt, 1, value, value.Length, new IntPtr(-1));
                }
                var result = new CtSqliteResult();
                int count = sqlite3_column_count(stmt);
                result.Columns = new string[count];
                for (int i = 0; i < count; i++) result.Columns[i] = Text(sqlite3_column_name(stmt, i));
                while ((rc = sqlite3_step(stmt)) == 100)
                {
                    var row = new object[count];
                    for (int i = 0; i < count; i++)
                    {
                        switch (sqlite3_column_type(stmt, i))
                        {
                            case 1: row[i] = sqlite3_column_int64(stmt, i); break;
                            case 2: row[i] = sqlite3_column_double(stmt, i).ToString("R", CultureInfo.InvariantCulture); break;
                            case 5: row[i] = null; break;
                            default: row[i] = Text(sqlite3_column_text(stmt, i), sqlite3_column_bytes(stmt, i)); break;
                        }
                    }
                    result.Rows.Add(row);
                }
                if (rc != 101) throw new Exception(Text(sqlite3_errmsg(db)));
                return result;
            }
            finally { sqlite3_finalize(stmt); }
        }
        finally { sqlite3_close_v2(db); }
    }
}

'@
}

function Invoke-Sqlite([string]$Query, [string]$Parameter, [string]$Title) {
    $result = [CtSqlite]::Query($Database, $Query, $Parameter)
    $rows = foreach ($row in $result.Rows) {
        $item = [ordered]@{}
        for ($i = 0; $i -lt $result.Columns.Length; $i++) { $item[$result.Columns[$i]] = $row[$i] }
        [pscustomobject]$item
    }
    if ($Grid) { $rows | Out-GridView -Title $Title }
    else {
        Write-Host "`n== $Title ($(@($rows).Count) rows) ==" -ForegroundColor Cyan
        $rows | Format-Table -AutoSize | Out-String -Width 4096 | Write-Host
    }
}

$records = 'SELECT SortOrder, CaptureNo, StartTime, EndTime, Sample, TsMs, Count0, Count0Raw, Hdm, HdmRaw, DoseRate
            FROM MeasurementRecords WHERE MeasurementSessionId = ? ORDER BY SortOrder'

Write-Host "Database: $Database"

if ($Sql) { Invoke-Sqlite $Sql $Value 'Query'; return }
if ($Session) { Invoke-Sqlite $records $Session "Session $Session"; return }

Invoke-Sqlite 'PRAGMA user_version' '' 'Schema version (expected 2)'

Invoke-Sqlite 'SELECT s.MeasurementSessionId, s.CreatedAt, COUNT(r.MeasurementRecordId) AS Records
               FROM MeasurementSessions s
               LEFT JOIN MeasurementRecords r ON r.MeasurementSessionId = s.MeasurementSessionId
               GROUP BY s.MeasurementSessionId ORDER BY s.rowid' '' 'Sessions (oldest first)'

$latest = [CtSqlite]::Query($Database,
    'SELECT MeasurementSessionId FROM MeasurementRecords ORDER BY MeasurementRecordId DESC LIMIT 1', '').Rows
if ($latest.Count -gt 0) {
    Invoke-Sqlite $records $latest[0][0] "Latest session $($latest[0][0])"
}

Invoke-Sqlite 'SELECT MeasurementSessionId, CaptureNo, COUNT(*) AS Copies FROM MeasurementRecords
               GROUP BY MeasurementSessionId, CaptureNo HAVING COUNT(*) > 1' '' 'Duplicate check (expected 0 rows)'
