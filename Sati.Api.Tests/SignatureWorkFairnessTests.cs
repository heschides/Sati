using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Sati.Api.Infrastructure;
using Sati.Models;
using Xunit;

namespace Sati.Api.Tests;

public sealed partial class SignatureWorkFairnessTests
{
    [Fact]
    public async Task CancellationAfterCommittedOfferLeavesPositionAndPreventsClinicalEffect()
    {
        using var cancellation = new CancellationTokenSource(); var stop = new AfterOffer(cancellation);
        await using var f = await SignatureWorkFixture.CreateAsync(false, stop); var a = await f.SeedAsync(f.A); await f.SeedAsync(f.B);
        stop.Armed = true;
        await using (var services = f.Services())
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => services.GetRequiredService<SignatureProcessingService>().RunOnceAsync(cancellation.Token));
        Assert.True(stop.Fired); await using var db = f.Open();
        Assert.Equal(f.A, (await db.SignatureWorkRotation.SingleAsync(x => x.WorkKind == SignatureWorkKind.Projection)).LastAgencyId);
        Assert.Empty(await db.Set<SignatureComplianceProjection>().ToListAsync());
        Assert.Null((await db.Forms.SingleAsync(x => x.Id == a.Form)).CompletedDate);
        await using var restarted = f.Services();
        Assert.Equal(f.B, (await restarted.GetRequiredService<SignatureWorkSelector>().SelectAsync(SignatureWorkKind.Projection, [], CancellationToken.None)).AgencyId);
        Assert.Empty(f.Email.Sends); Assert.Empty(f.BlobStore.Reads);
    }

    [Fact]
    public async Task RetryingOuterScopeCannotEnterSelectorOrMoveMetadata()
    {
        await using var f = await SignatureWorkFixture.CreateAsync(); await f.SeedAsync(f.A);
        await using var services = f.Services(); await using var db = f.Open();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new RetryingSelector(db).ExecuteAsync(() =>
            services.GetRequiredService<SignatureWorkSelector>().SelectAsync(SignatureWorkKind.Projection, [], CancellationToken.None)));
        Assert.All(await db.SignatureWorkRotation.ToListAsync(), x => Assert.Equal(1, x.Revision));
        Assert.Empty(await db.SignatureAgencyWorkRotation.ToListAsync());
    }

    private sealed class RetryingSelector(DbContext db) : ExecutionStrategy(db, 1, TimeSpan.Zero)
    { protected override bool ShouldRetryOn(Exception exception) => false; }
    private sealed class AfterOffer(CancellationTokenSource cancellation) : DbTransactionInterceptor
    {
        internal bool Armed, Fired;
        public override Task TransactionCommittedAsync(System.Data.Common.DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            if (Armed && eventData.Context?.ChangeTracker.Entries<SignatureWorkRotation>().Any() == true)
            { Armed = false; Fired = true; cancellation.Cancel(); }
            return Task.CompletedTask;
        }
    }

    [Theory]
    [InlineData(SignatureWorkKind.Projection)]
    [InlineData(SignatureWorkKind.Package)]
    [InlineData(SignatureWorkKind.Mail)]
    public async Task AgencyThenItemRotationSurvivesEverySelectorRestartWithoutBusinessWrites(SignatureWorkKind kind)
    {
        await using var f = await SignatureWorkFixture.CreateAsync();
        var a1 = await f.SeedAsync(f.A, packaged: false);
        var a2 = await f.SeedAsync(f.A, packaged: false);
        var b = await f.SeedAsync(f.B, packaged: false);
        long Id(SignatureWorkFixture.Item item) => kind == SignatureWorkKind.Mail ? item.Mail : item.Completion;
        var expected = new[] { (f.A, Id(a1)), (f.B, Id(b)), (f.A, Id(a2)), (f.B, Id(b)), (f.A, Id(a1)) };
        foreach (var (agency, item) in expected)
        {
            await using var services = f.Services();
            var offered = await services.GetRequiredService<SignatureWorkSelector>().SelectAsync(kind, [], CancellationToken.None);
            Assert.Equal(SignatureSelectionKind.Selected, offered.Kind);
            Assert.Equal(agency, offered.AgencyId); Assert.Equal(item, offered.ItemId);
        }
        await using var db = f.Open();
        Assert.Equal(6, (await db.SignatureWorkRotation.SingleAsync(x => x.WorkKind == kind)).Revision);
        Assert.All(await db.SignatureWorkRotation.Where(x => x.WorkKind != kind).ToListAsync(), x => Assert.Equal(1, x.Revision));
        Assert.All(await db.SignatureRequests.ToListAsync(), x => Assert.Equal(5, x.Revision));
        Assert.All(await db.SignatureOutbox.ToListAsync(), x => { Assert.Equal(0, x.Attempts); Assert.Null(x.LeaseId); Assert.Null(x.ProviderOperationId); });
        Assert.Empty(await db.SignaturePackages.ToListAsync()); Assert.Empty(await db.Set<SignatureComplianceProjection>().ToListAsync());
        Assert.Empty(f.BlobStore.Reads); Assert.Empty(f.Email.Sends); Assert.Empty(f.Email.Polls);
    }

    [Theory]
    [InlineData(SignatureWorkKind.Projection)] [InlineData(SignatureWorkKind.Package)] [InlineData(SignatureWorkKind.Mail)]
    public async Task VisitedIdsAreExcludedAndEmptySelectionDoesNotAdvance(SignatureWorkKind kind)
    {
        await using var f = await SignatureWorkFixture.CreateAsync();
        var a = await f.SeedAsync(f.A, packaged: false); var b = await f.SeedAsync(f.B, packaged: false);
        await using var services = f.Services(); var selector = services.GetRequiredService<SignatureWorkSelector>();
        var one = await selector.SelectAsync(kind, [], CancellationToken.None);
        var two = await selector.SelectAsync(kind, [one.ItemId], CancellationToken.None);
        Assert.Equal(f.A, one.AgencyId); Assert.Equal(f.B, two.AgencyId);
        Assert.Equal(SignatureSelectionKind.Empty, (await selector.SelectAsync(kind, [one.ItemId, two.ItemId], CancellationToken.None)).Kind);
        await using var db = f.Open(); Assert.Equal(3, (await db.SignatureWorkRotation.SingleAsync(x => x.WorkKind == kind)).Revision);
    }

    [Fact]
    public async Task HostedPassOffersTenDistinctDamagedProjectionsThenRestartContinues()
    {
        await using var f = await SignatureWorkFixture.CreateAsync();
        for (var i = 0; i < 12; i++) await f.SeedAsync(f.A, damagedProjection: true);
        await using (var services = f.Services()) await services.GetRequiredService<SignatureProcessingService>().RunOnceAsync(CancellationToken.None);
        await using (var db = f.Open())
        {
            Assert.Equal(11, (await db.SignatureWorkRotation.SingleAsync(x => x.WorkKind == SignatureWorkKind.Projection)).Revision);
            Assert.Equal((await db.SignatureCompletions.OrderBy(x => x.Id).Skip(9).FirstAsync()).Id,
                (await db.SignatureAgencyWorkRotation.SingleAsync(x => x.WorkKind == SignatureWorkKind.Projection)).LastItemId);
            Assert.Equal(10, await db.SignatureOutbox.CountAsync(x => x.CompletedAtUtc != null));
            Assert.Empty(await db.Set<SignatureComplianceProjection>().ToListAsync());
        }
        await using (var services = f.Services()) await services.GetRequiredService<SignatureProcessingService>().RunOnceAsync(CancellationToken.None);
        await using var after = f.Open();
        Assert.Equal(21, (await after.SignatureWorkRotation.SingleAsync(x => x.WorkKind == SignatureWorkKind.Projection)).Revision);
        Assert.Equal(12, await after.SignatureOutbox.CountAsync(x => x.CompletedAtUtc != null));
        Assert.Equal(12, await after.SignaturePackages.CountAsync());
        Assert.Empty(f.Email.Sends); Assert.Empty(f.Email.Polls);
    }

    [Fact]
    public async Task DamagedPackageCannotHideHealthyPackageAndReceipt()
    {
        await using var f = await SignatureWorkFixture.CreateAsync();
        var damaged = await f.SeedAsync(f.A, packaged: false); var healthy = await f.SeedAsync(f.B, packaged: false);
        f.BlobStore.Content.Remove(damaged.OriginalPath);
        await using var services = f.Services(); await services.GetRequiredService<SignatureProcessingService>().RunOnceAsync(CancellationToken.None);
        await using var db = f.Open();
        var package = Assert.Single(await db.SignaturePackages.ToListAsync()); Assert.Equal(healthy.Completion, package.CompletionId);
        Assert.Equal(f.B, package.AgencyId); Assert.True(f.BlobStore.Content.ContainsKey(package.BlobPath));
        Assert.Single(await db.SignatureOutbox.Where(x => x.Purpose == "Receipt" && x.RequestId == healthy.Request).ToListAsync());
        Assert.DoesNotContain(await db.SignaturePackages.ToListAsync(), x => x.CompletionId == damaged.Completion);
        Assert.Empty(f.Email.Sends); Assert.Empty(f.Email.Polls);
    }

    [Theory]
    [InlineData("scope")] [InlineData("due")] [InlineData("lease")] [InlineData("completed")]
    public async Task ScopedMailClaimRechecksEligibilityWithoutClaimingAnotherRow(string boundary)
    {
        await using var f = await SignatureWorkFixture.CreateAsync();
        var a = await f.SeedAsync(f.A); var b = await f.SeedAsync(f.B);
        await using (var change = f.Open())
        {
            var row = await change.SignatureOutbox.SingleAsync(x => x.Id == a.Mail);
            if (boundary == "due") row.NextAttemptAtUtc = f.Clock.GetUtcNow().UtcDateTime.AddHours(1);
            if (boundary == "lease") { row.LeaseId = Guid.NewGuid(); row.LeaseUntilUtc = f.Clock.GetUtcNow().UtcDateTime.AddMinutes(1); }
            if (boundary == "completed") { row.CompletedAtUtc = f.Clock.GetUtcNow().UtcDateTime; row.State = "Suppressed"; }
            row.Revision++; await change.SaveChangesAsync();
        }
        await using var services = f.Services(); await using var db = f.Open();
        Assert.False(await services.GetRequiredService<Sati.Signatures.SignatureMailWorker>().ProcessCandidateAsync(db, boundary == "scope" ? f.B : f.A, a.Mail));
        db.ChangeTracker.Clear(); Assert.All(await db.SignatureOutbox.ToListAsync(), x => Assert.Equal(0, x.Attempts));
        Assert.Empty(f.Email.Sends); Assert.Empty(f.Email.Polls);
    }

    [Fact]
    public async Task ScopedMailRecoveryPollsExistingGuidOnlyAndRetainsOtherAgency()
    {
        await using var f = await SignatureWorkFixture.CreateAsync(); var a = await f.SeedAsync(f.A); var b = await f.SeedAsync(f.B);
        var operation = Guid.NewGuid(); f.Signature.EmailEnabled = true;
        await using (var change = f.Open())
        {
            var row = await change.SignatureOutbox.SingleAsync(x => x.Id == a.Mail);
            row.ProviderOperationId = operation; row.Attempts = 1; row.LeaseId = Guid.NewGuid();
            row.LeaseUntilUtc = f.Clock.GetUtcNow().UtcDateTime.AddSeconds(-1); row.State = "Pending";
            row.Revision++; await change.SaveChangesAsync();
        }
        await using var services = f.Services(); await using var db = f.Open();
        Assert.True(await services.GetRequiredService<Sati.Signatures.SignatureMailWorker>().ProcessCandidateAsync(db, f.A, a.Mail));
        Assert.Equal(operation.ToString("D"), Assert.Single(f.Email.Polls)); Assert.Empty(f.Email.Sends);
        db.ChangeTracker.Clear(); var done = await db.SignatureOutbox.SingleAsync(x => x.Id == a.Mail);
        Assert.Equal(operation, done.ProviderOperationId); Assert.NotNull(done.CompletedAtUtc); Assert.Equal(2, done.Attempts);
        Assert.Equal(0, (await db.SignatureOutbox.SingleAsync(x => x.Id == b.Mail)).Attempts);
    }

    [Fact]
    public async Task SchedulingMetadataRequiresNewRevisionAndDetectsStaleOwner()
    {
        await using var f = await SignatureWorkFixture.CreateAsync(); await f.SeedAsync(f.A);
        await using var stale = f.Open(); var old = await stale.SignatureWorkRotation.SingleAsync(x => x.WorkKind == SignatureWorkKind.Projection);
        old.LastAgencyId = f.A; await Assert.ThrowsAsync<InvalidOperationException>(() => stale.SaveChangesAsync()); old.Revision++;
        await using (var services = f.Services()) await services.GetRequiredService<SignatureWorkSelector>().SelectAsync(SignatureWorkKind.Projection, [], CancellationToken.None);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => stale.SaveChangesAsync());
        await using var db = f.Open(); var lane = await db.SignatureAgencyWorkRotation.SingleAsync(); db.Remove(lane);
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Theory]
    [InlineData(-1, false)] [InlineData(0, true)] [InlineData(1, true)]
    public void SqlAdmissionRecognizesOnlyEstablishedOwnership(int value, bool expected) =>
        Assert.Equal(expected, SignatureWorkSelector.OwnsLease(value, CancellationToken.None));

    [Fact]
    public void SqlAdmissionRejectsMalformedErrorsAndCancellationWins()
    {
        foreach (var value in new object?[] { null, DBNull.Value, -2, -3, -999, 2, 0L, "0" })
            Assert.Throws<InvalidOperationException>(() => SignatureWorkSelector.OwnsLease(value, CancellationToken.None));
        Assert.Throws<OperationCanceledException>(() => SignatureWorkSelector.OwnsLease(null, new CancellationToken(true)));
    }

    [Fact]
    public async Task DamagedProjectionHeadCannotHideHealthyAgencyAcrossHostRestart()
    {
        await using var f = await SignatureWorkFixture.CreateAsync();
        var damaged = await f.SeedAsync(f.A, damagedProjection: true);
        var healthy = await f.SeedAsync(f.B);
        for (var pass = 0; pass < 2; pass++)
        {
            await using var services = f.Services();
            await services.GetRequiredService<SignatureProcessingService>().RunOnceAsync(CancellationToken.None);
        }
        await using var db = f.Open();
        var projected = await db.Set<SignatureComplianceProjection>().AsNoTracking().ToListAsync();
        var row = Assert.Single(projected);
        Assert.Equal(healthy.Completion, row.CompletionId); Assert.Equal(f.B, row.AgencyId);
        Assert.Equal("Applied", row.Outcome);
        Assert.Equal(new DateTime(2026, 10, 1), (await db.Forms.SingleAsync(x => x.Id == healthy.Form)).CompletedDate);
        Assert.Null((await db.Forms.SingleAsync(x => x.Id == damaged.Form)).CompletedDate);
        Assert.Equal(2, await db.SignaturePackages.CountAsync());
        Assert.Empty(f.Email.Sends); Assert.Empty(f.Email.Polls);
    }
}
