<#
.SYNOPSIS
    Dumps the installed Sati version and the database's tables, indexes and update
    history, so the startup refusal can be compared against a known-good database.
    Read-only.

.DESCRIPTION
    1.3.13 still refuses to start on this database, reporting that part of the annual
    compliance update is already present. Two of these have been found and fixed by
    reasoning from a partial list of objects. This stops doing that: it dumps the whole
    picture, so the comparison is exhaustive rather than a guess about which object to
    look at next.

    It also reports the version of Sati actually installed, because an identical dialog
    would appear if the new build had not replaced the old one.

    No transaction, no writes. Object names, column names, dates and counts only: no
    consumer names, notes, or other personal information.
#>
[CmdletBinding()]
param(
    [string]$SqlServer = '(localdb)\MSSQLLocalDB',
    [string]$DatabaseName = 'SatiProduction'
)

$ErrorActionPreference = 'Stop'

"Sati schema dump"
"Run at   : $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
"Server   : $SqlServer"
"Database : $DatabaseName"
"This dump makes no changes."
""

"INSTALLED SATI"
"--------------"
$candidates = @(
    (Join-Path $env:LOCALAPPDATA 'Programs\Satilogica\Sati\Sati.exe'),
    (Join-Path $env:LOCALAPPDATA 'Programs\Sati\Sati.exe'),
    (Join-Path $env:LOCALAPPDATA 'Sati\Sati.exe'),
    (Join-Path ${env:ProgramFiles} 'Sati\Sati.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Sati\Sati.exe')
) | Where-Object { $_ }
$found = $false
foreach ($candidate in $candidates) {
    if (Test-Path -LiteralPath $candidate) {
        $found = $true
        $info = (Get-Item -LiteralPath $candidate).VersionInfo
        "  {0}" -f $candidate
        "    FileVersion    : {0}" -f $info.FileVersion
        "    ProductVersion : {0}" -f $info.ProductVersion
        "    Written        : {0}" -f (Get-Item -LiteralPath $candidate).LastWriteTime
    }
}
if (-not $found) {
    foreach ($root in @($env:LOCALAPPDATA, ${env:ProgramFiles}, ${env:ProgramFiles(x86)})) {
        if (-not $root) { continue }
        Get-ChildItem -LiteralPath $root -Filter 'Sati.exe' -Recurse -ErrorAction SilentlyContinue -Depth 4 |
            Where-Object { $_.FullName -notlike '*\Temp\*' } |
            Select-Object -First 5 | ForEach-Object {
                $found = $true
                "  {0}" -f $_.FullName
                "    FileVersion    : {0}" -f $_.VersionInfo.FileVersion
                "    Written        : {0}" -f $_.LastWriteTime
            }
    }
}
if (-not $found) { "  Sati.exe was not found in the usual install locations." }
""

$connection = New-Object System.Data.SqlClient.SqlConnection `
    "Server=$SqlServer;Database=$DatabaseName;Integrated Security=true;Encrypt=false;Connect Timeout=30;"
$connection.Open()
try {
    $command = $connection.CreateCommand()
    $command.CommandTimeout = 300
    $command.CommandText = @'
SET NOCOUNT ON;

SELECT N'history' AS Section, MigrationId AS Name, N'' AS Detail
FROM dbo.__EFMigrationsHistory;

SELECT N'table' AS Section, t.name AS Name, N'' AS Detail
FROM sys.tables AS t WHERE t.schema_id = SCHEMA_ID(N'dbo');

SELECT N'index' AS Section, OBJECT_NAME(i.object_id) + N'.' + i.name AS Name,
       STUFF((SELECT N', ' + c.name
              FROM sys.index_columns AS ic
              INNER JOIN sys.columns AS c
                      ON c.object_id = ic.object_id AND c.column_id = ic.column_id
              WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id
                AND ic.is_included_column = 0
              ORDER BY ic.key_ordinal
              FOR XML PATH(N'')), 1, 2, N'')
       + CASE WHEN i.is_unique = 1 THEN N' [unique]' ELSE N'' END
       + ISNULL(N' filter=' + i.filter_definition, N'') AS Detail
FROM sys.indexes AS i
INNER JOIN sys.tables AS t ON t.object_id = i.object_id
WHERE t.schema_id = SCHEMA_ID(N'dbo') AND i.name IS NOT NULL;

SELECT N'check' AS Section, cc.name AS Name, N'' AS Detail
FROM sys.check_constraints AS cc;
'@
    $reader = $command.ExecuteReader()
    do {
        $section = ''
        while ($reader.Read()) {
            if ($reader.GetString(0) -ne $section) {
                $section = $reader.GetString(0)
                ""
                $section.ToUpperInvariant()
                ('-' * $section.Length)
            }
            $detail = $reader.GetValue(2)
            if ([string]::IsNullOrWhiteSpace([string]$detail)) { "  {0}" -f $reader.GetValue(1) }
            else { "  {0}  ({1})" -f $reader.GetValue(1), $detail }
        }
    } while ($reader.NextResult())
    $reader.Close()
    ""
    'Dump complete. Send this whole file to Josh. Nothing was changed.'
}
finally {
    $connection.Dispose()
}
