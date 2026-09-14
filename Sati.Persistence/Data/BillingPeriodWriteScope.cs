using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Sati.Data;

/// <summary>
/// Holds one database-owned billing-period lock until the period and all of its
/// claim lines have been validated and committed together. The lock is shared
/// across API processes, so submission or EDI generation cannot overtake a
/// claim insertion that has already passed its checks.
/// </summary>
public static class BillingPeriodWriteScope
{
    public static async Task<IDbContextTransaction> BeginAsync(
        DbContext context,
        int agencyId,
        int ownerUserId,
        int year,
        int month,
        CancellationToken cancellationToken = default)
    {
        if (agencyId <= 0 || ownerUserId <= 0 || year < 2000 || month is < 1 or > 12)
            throw new ArgumentOutOfRangeException(nameof(ownerUserId),
                "A persisted agency, owner, year, and month are required.");
        if (context.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("The billing-period write must own its transaction.");
        if (!context.Database.IsSqlServer() &&
            context.Database.ProviderName != "Microsoft.EntityFrameworkCore.Sqlite")
            throw new InvalidOperationException("Billing-period writes require a supported relational database.");

        var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
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
                resource.Value = FormattableString.Invariant(
                    $"Sati:BillingPeriod:{agencyId}:{ownerUserId}:{year:D4}:{month:D2}");
                command.Parameters.Add(resource);
                if (await command.ExecuteScalarAsync(cancellationToken) is not int result)
                    throw new InvalidOperationException("The database did not confirm the billing-period lock.");
                if (result < 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    throw new BillingPeriodWriteConflictException();
                }
            }

            return transaction;
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }
}

public sealed class BillingPeriodWriteConflictException : InvalidOperationException
{
    public BillingPeriodWriteConflictException()
        : base("Another change to this billing period is in progress. Refresh it and try again; this attempt made no change.") { }
}
