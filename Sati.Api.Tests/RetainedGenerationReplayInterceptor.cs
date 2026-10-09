using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Sati.Testing;

// Hide one retained-key lookup to exercise the duplicate-write recovery branch.
// This is deterministic branch coverage, not a concurrent database race proof.
internal sealed class RetainedGenerationReplayInterceptor : DbCommandInterceptor
{
    public string? HideKeyOnce { get; set; }
    public string? HideControlOnce { get; set; }
    public bool LookupHidden { get; private set; }
    public bool RecoveryLookupObserved { get; private set; }
    public bool InjectKeyConflict { get; set; }
    private string? _hiddenKey;

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        if (InjectKeyConflict && _hiddenKey is not null && command.CommandText.Contains("INSERT INTO \"EdiGenerations\"", StringComparison.Ordinal) &&
            command.Parameters.Cast<DbParameter>().Any(x => Equals(x.Value, _hiddenKey)))
        {
            InjectKeyConflict = false;
            // SQLite may report the control index before the retry-key index. Inject
            // that exact storage conflict with the retained winner already present.
            throw new Microsoft.Data.Sqlite.SqliteException(
                "UNIQUE constraint failed: EdiGenerations.AgencyId, EdiGenerations.ActorUserId, EdiGenerations.IdempotencyKey", 19);
        }
        return ValueTask.FromResult(result);
    }

    public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command,
        CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
    {
        if (!command.CommandText.Contains("EdiGenerations", StringComparison.Ordinal)) return result;
        if (HideControlOnce is not null && command.CommandText.Contains("EXISTS", StringComparison.Ordinal) &&
            command.Parameters.Cast<DbParameter>().Any(x => Equals(x.Value, HideControlOnce)))
        {
            HideControlOnce = null;
            var absent = new DataTable();
            absent.Columns.Add(result.GetName(0), typeof(bool));
            absent.Rows.Add(false);
            await result.DisposeAsync();
            return absent.CreateDataReader();
        }
        if (!command.CommandText.Contains("IdempotencyKey", StringComparison.Ordinal)) return result;
        if (HideKeyOnce is null)
        {
            if (_hiddenKey is not null && command.Parameters.Cast<DbParameter>().Any(x => Equals(x.Value, _hiddenKey)))
                RecoveryLookupObserved = true;
            return result;
        }
        if (!command.Parameters.Cast<DbParameter>().Any(x => Equals(x.Value, HideKeyOnce))) return result;
        _hiddenKey = HideKeyOnce;
        HideKeyOnce = null;
        LookupHidden = true;
        var empty = new DataTable();
        for (var i = 0; i < result.FieldCount; i++) empty.Columns.Add(result.GetName(i), result.GetFieldType(i));
        await result.DisposeAsync();
        return empty.CreateDataReader();
    }
}
