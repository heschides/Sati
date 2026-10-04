using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Sati.Api.Data;

namespace Sati.Api.Infrastructure;

/// <summary>
/// Serializes the cross-agency account-number and secret-alias check with its inserts
/// across every API host sharing the Demo database. SQL releases this lease with the
/// caller's transaction, including on rollback or a dropped connection.
/// </summary>
internal static class ClaimMdOnboardingCoordination
{
    internal const string Resource = "Sati.ClaimMdTestAccountOnboarding";

    internal static async Task<bool> TryAcquireAsync(ApiDbContext db, CancellationToken token)
    {
        if (!db.Database.IsSqlServer() || db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Claim.MD onboarding requires an active SQL Server transaction.");
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = db.Database.CurrentTransaction.GetDbTransaction();
        command.CommandText = "DECLARE @result int; EXEC @result = sys.sp_getapplock " +
            "@Resource = @resource, @LockMode = 'Exclusive', @LockOwner = 'Transaction', " +
            "@LockTimeout = 0; SELECT @result;";
        command.CommandTimeout = 10;
        var resource = command.CreateParameter();
        resource.ParameterName = "@resource";
        resource.Value = Resource;
        command.Parameters.Add(resource);
        var result = Convert.ToInt32(await command.ExecuteScalarAsync(token));
        if (result == -1) return false;
        if (result < 0)
            throw new InvalidOperationException("Claim.MD onboarding coordination is unavailable.");
        return true;
    }
}
