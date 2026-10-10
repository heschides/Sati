using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Sati.Api.Data;
using Sati.Api.Infrastructure;
using Xunit;

namespace Sati.Api.Tests;

public sealed partial class SessionAdmissionResultTests
{
    [Theory]
    [InlineData("null")]
    [InlineData("DBNull")]
    [InlineData("string")]
    [InlineData("long")]
    [InlineData("positive")]
    [InlineData("negative")]
    public Task OnboardingMalformedScalarCannotAdmit(string kind) =>
        WithOnboardingAsync(Scalar(kind), async (db, _, token) =>
            await Assert.ThrowsAsync<InvalidOperationException>(() => ClaimMdOnboardingCoordination.TryAcquireAsync(db, token)));

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(-1)]
    public Task OnboardingExactOutcomePreservesTransactionOwnership(int result) =>
        WithOnboardingAsync(result, async (db, _, token) =>
            Assert.Equal(result != -1, await ClaimMdOnboardingCoordination.TryAcquireAsync(db, token)));

    [Theory]
    [InlineData("success")]
    [InlineData("busy")]
    [InlineData("null")]
    public Task OnboardingCancellationAfterScalarPreventsContinuation(string kind) =>
        WithOnboardingAsync(kind == "success" ? 0 : kind == "busy" ? -1 : null,
            async (db, connection, token) =>
            {
                var failure = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                    ClaimMdOnboardingCoordination.TryAcquireAsync(db, token));
                Assert.Equal(token, failure.CancellationToken);
            }, cancelAfterScalar: true);

    [Fact]
    public Task OnboardingCommandFailureLeavesCallerTransactionOwner() =>
        WithOnboardingAsync(0, async (db, connection, token) =>
        {
            connection.ScalarFailure = true;
            await Assert.ThrowsAsync<SyntheticCommandException>(() => ClaimMdOnboardingCoordination.TryAcquireAsync(db, token));
        });

    [Fact]
    public async Task OnboardingRefusesMissingTransactionWithoutOpeningConnection()
    {
        var fixture = new Fixture("onboarding", 0);
        await using var db = fixture.CreateDbContext();
        await Assert.ThrowsAsync<InvalidOperationException>(() => ClaimMdOnboardingCoordination.TryAcquireAsync(db, default));
        Assert.Equal(0, fixture.Connection.Opens);
        Assert.Empty(fixture.Connection.AcquiredResources);
    }

    private static async Task WithOnboardingAsync(object? scalar,
        Func<ApiDbContext, SyntheticConnection, CancellationToken, Task> assertion, bool cancelAfterScalar = false)
    {
        using var cancellation = new CancellationTokenSource();
        var fixture = new Fixture("onboarding", scalar);
        var connection = fixture.Connection;
        await using (var db = fixture.CreateDbContext())
        {
            await db.Database.OpenConnectionAsync();
            await using var transaction = new OnboardingTransaction(connection);
            await db.Database.UseTransactionAsync(transaction);
            if (cancelAfterScalar) connection.AfterTargetScalar = cancellation.Cancel;
            await assertion(db, connection, cancellation.Token);
            Assert.Equal(ConnectionState.Open, connection.State);
            Assert.False(connection.WasDisposed);
            Assert.Same(transaction, connection.LastCommandTransaction);
            Assert.Equal(10, connection.LastCommandTimeout);
            Assert.Contains("@LockOwner = 'Transaction'", connection.LastCommandText, StringComparison.Ordinal);
            Assert.Contains("@LockTimeout = 0", connection.LastCommandText, StringComparison.Ordinal);
            Assert.Equal(new[] { ClaimMdOnboardingCoordination.Resource }, connection.AcquiredResources);
            Assert.Equal(0, connection.Releases);
            Assert.False(transaction.RolledBack);
            await transaction.RollbackAsync(); // caller, rather than coordinator, ends ownership
            Assert.True(transaction.RolledBack);
        }
        fixture.AssertClosed();
        Assert.Equal(0, connection.Releases); // no session-owned release was invented
    }

    private sealed class OnboardingTransaction(DbConnection connection) : DbTransaction
    {
        protected override DbConnection DbConnection => connection;
        public override IsolationLevel IsolationLevel => IsolationLevel.Serializable;
        public bool RolledBack { get; private set; }
        public override void Commit() => throw new NotSupportedException();
        public override void Rollback() => RolledBack = true;
    }
}
