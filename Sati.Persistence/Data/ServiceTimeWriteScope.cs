using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Sati.Data;

/// <summary>
/// Holds the database-owned schedule lock until the validation and write commit together.
/// The scope is the agency and case manager across all dates, so moving a note between dates
/// cannot invert lock order. It is shared by API and transitional local writers and does not
/// depend on which application process handles the request.
/// </summary>
public static class ServiceTimeWriteScope
{
    public static async Task<IDbContextTransaction> BeginAsync(
        DbContext context, int agencyId, int caseManagerId, CancellationToken cancellationToken = default)
    {
        if (agencyId <= 0 || caseManagerId <= 0)
            throw new ArgumentOutOfRangeException(nameof(caseManagerId), "A persisted agency and case manager are required.");
        if (context.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("The schedule write must own its transaction.");
        if (!context.Database.IsSqlServer() && context.Database.ProviderName != "Microsoft.EntityFrameworkCore.Sqlite")
            throw new InvalidOperationException("Schedule writes require a supported relational database.");

        var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            if (context.Database.IsSqlServer())
            {
                await using var command = context.Database.GetDbConnection().CreateCommand();
                command.Transaction = transaction.GetDbTransaction();
                command.CommandText = """
                    DECLARE @result int;
                    EXEC @result = sys.sp_getapplock
                        @Resource = @resource,
                        @LockMode = 'Exclusive',
                        @LockOwner = 'Transaction',
                        @LockTimeout = 10000;
                    SELECT @result;
                    """;
                command.CommandTimeout = 15;
                var resource = command.CreateParameter();
                resource.ParameterName = "@resource";
                resource.DbType = DbType.String;
                resource.Size = 255;
                resource.Value = FormattableString.Invariant($"Sati:ServiceTime:{agencyId}:{caseManagerId}");
                command.Parameters.Add(resource);
                if (await command.ExecuteScalarAsync(cancellationToken) is not int result)
                    throw new InvalidOperationException("The database did not confirm the schedule lock.");
                if (result < 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    throw new ServiceTimeWriteConflictException();
                }
            }
            // SQLite's non-deferred serializable transaction obtains its write reservation before
            // the first conflict read. SQLite remains test evidence, not a SQL Server substitute.
            return transaction;
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }
}

public sealed class ServiceTimeWriteConflictException : InvalidOperationException
{
    public ServiceTimeWriteConflictException()
        : base("Another change to this case manager's schedule is in progress. Refresh the schedule and try again; this attempt made no change.") { }

    public ServiceTimeWriteConflictException(string message, Exception? innerException = null)
        : base(message, innerException) { }
}
