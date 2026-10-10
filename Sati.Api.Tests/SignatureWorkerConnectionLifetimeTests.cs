using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sati.Api.Data;
using Sati.Api.Infrastructure;
using Sati.Contracts.V1;
using Sati.Models;
using Sati.Signatures;
using Xunit;
using Xunit.Sdk;

namespace Sati.Api.Tests;

public sealed class SignatureWorkerConnectionLifetimeTests
{
    [SqlServerFact]
    public async Task ProjectionPackageAndDependencySpansRetainTwoConnectionsWithSelectorDisposed()
    {
        await using var h = await Harness.CreateAsync(packaged: false);
        h.Blobs.BeforeRead = _ => { h.Observe("blob-read", 2); return Task.CompletedTask; };
        h.Blobs.BeforeWrite = _ => { h.Observe("blob-write", 2); return Task.CompletedTask; };
        h.Keys.BeforeUnwrap = _ => { h.Observe("package-unwrap", 2); return Task.CompletedTask; };
        h.Keys.BeforeWrap = _ => { h.Observe("receipt-wrap", 2); return Task.CompletedTask; };
        await using (var services = h.Services()) await services.GetRequiredService<SignatureProcessingService>().RunOnceAsync(CancellationToken.None);
        Assert.Equal(new[] { "blob-read", "blob-write", "package-unwrap", "receipt-wrap" }, h.Intervals);
        Assert.Equal(1, h.Projection.Saves);
        Assert.Equal(2, h.Probe.Peak); Assert.Equal(0, h.Probe.Held);
        await using var evidence = h.Evidence();
        Assert.Single(await evidence.Set<SignatureComplianceProjection>().ToListAsync());
        Assert.Equal(new DateTime(2026, 10, 1), (await evidence.Forms.SingleAsync(x => x.Id == h.Item.Form)).CompletedDate);
        Assert.Single(await evidence.SignaturePackages.ToListAsync());
        Assert.Equal(2, await evidence.SignatureOutbox.CountAsync());
        Assert.All(await evidence.SignatureOutbox.ToListAsync(), row => Assert.Equal("Suppressed", row.State));
        Assert.Empty(h.Email.SendAttempts);
    }

    [SqlServerFact]
    public async Task PackageDependencyFailureClosesTransactionAndRetainsProjectionForLaterPackage()
    {
        await using var h = await Harness.CreateAsync(packaged: false);
        var called = false;
        h.Blobs.BeforeRead = _ => { called = true; h.Observe("failed-read", 2); throw new IOException("Synthetic blob failure."); };
        await using (var services = h.Services()) await services.GetRequiredService<SignatureProcessingService>().RunOnceAsync(CancellationToken.None);
        Assert.True(called); Assert.Equal(0, h.Probe.Held); Assert.Equal(2, h.Probe.Peak);
        await using (var evidence = h.Evidence())
        {
            Assert.Single(await evidence.Set<SignatureComplianceProjection>().ToListAsync());
            Assert.Empty(await evidence.SignaturePackages.ToListAsync());
            Assert.False(await evidence.SignatureOutbox.AnyAsync(x => x.Purpose == "Receipt"));
        }
        h.Blobs.BeforeRead = null;
        await using (var restarted = h.Services()) await restarted.GetRequiredService<SignatureProcessingService>().RunOnceAsync(CancellationToken.None);
        Assert.Equal(0, h.Probe.Held); Assert.Equal(2, h.Probe.Peak);
        await using var done = h.Evidence(); Assert.Single(await done.SignaturePackages.ToListAsync());
        Assert.Single(await done.SignatureEvents.Where(x => x.Kind == "PackagePrepared").ToListAsync());
    }

    [SqlServerFact]
    public async Task PackageCancellationClosesOwnedConnectionsAndDoesNotEnterMailPhase()
    {
        await using var h = await Harness.CreateAsync(packaged: false);
        using var cancellation = new CancellationTokenSource();
        h.Blobs.BeforeRead = _ => { h.Observe("cancelled-read", 2); cancellation.Cancel(); cancellation.Token.ThrowIfCancellationRequested(); return Task.CompletedTask; };
        await using (var services = h.Services())
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => services.GetRequiredService<SignatureProcessingService>().RunOnceAsync(cancellation.Token));
        Assert.Single(h.Intervals); Assert.Equal(0, h.Probe.Held); Assert.Equal(2, h.Probe.Peak);
        await using (var evidence = h.Evidence())
        {
            Assert.Single(await evidence.Set<SignatureComplianceProjection>().ToListAsync());
            Assert.Empty(await evidence.SignaturePackages.ToListAsync());
            Assert.Equal(0, (await evidence.SignatureOutbox.SingleAsync()).Attempts);
        }
        h.Blobs.BeforeRead = null;
        await using (var restarted = h.Services()) await restarted.GetRequiredService<SignatureProcessingService>().RunOnceAsync(CancellationToken.None);
        Assert.Equal(0, h.Probe.Held);
        await using var done = h.Evidence(); Assert.Single(await done.SignaturePackages.ToListAsync());
    }

    [SqlServerFact]
    public async Task MailUnwrapUsesResetOnlyAndPostRetainsRevocationTransaction()
    {
        await using var h = await Harness.CreateAsync(packaged: true);
        var receipt = await h.SeedReceiptAsync();
        h.Keys.BeforeUnwrap = _ => { h.Observe("mail-unwrap", 1); return Task.CompletedTask; };
        h.Email.BeforeSend = _ => { h.Observe("mail-post", 2); return Task.CompletedTask; };
        await using (var services = h.Services()) await services.GetRequiredService<SignatureProcessingService>().RunOnceAsync(CancellationToken.None);
        Assert.Equal(new[] { "mail-unwrap", "mail-post" }, h.Intervals);
        Assert.Equal(2, h.Probe.Peak); Assert.Equal(0, h.Probe.Held);
        var operation = Assert.Single(h.Email.SendAttempts); Assert.Empty(h.Email.GetAttempts);
        await using var evidence = h.Evidence(); var row = await evidence.SignatureOutbox.SingleAsync(x => x.Id == receipt);
        Assert.Equal(operation, row.ProviderOperationId); Assert.Equal(1, row.Attempts);
        Assert.Equal("Sent", row.State); Assert.NotNull(row.CompletedAtUtc); Assert.Null(row.LeaseId);
    }

    [SqlServerFact]
    public async Task CancelledPostPreservesGuidAndLeaseThenRestartUsesGetOnlyWithResetConnection()
    {
        await using var h = await Harness.CreateAsync(packaged: true);
        var receipt = await h.SeedReceiptAsync();
        using var cancellation = new CancellationTokenSource();
        h.Email.BeforeSend = _ => { h.Observe("cancelled-post", 2); cancellation.Cancel(); cancellation.Token.ThrowIfCancellationRequested(); return Task.CompletedTask; };
        await using (var services = h.Services())
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => services.GetRequiredService<SignatureProcessingService>().RunOnceAsync(cancellation.Token));
        Assert.Equal(0, h.Probe.Held); Assert.Equal(2, h.Probe.Peak);
        var operation = Assert.Single(h.Email.SendAttempts);
        await using (var evidence = h.Evidence())
        {
            var row = await evidence.SignatureOutbox.SingleAsync(x => x.Id == receipt);
            Assert.Equal(operation, row.ProviderOperationId); Assert.Equal(1, row.Attempts);
            Assert.Null(row.CompletedAtUtc); Assert.NotNull(row.LeaseId); Assert.NotNull(row.LeaseUntilUtc);
        }
        h.Source.Clock.Advance(TimeSpan.FromMinutes(6)); h.Email.BeforeSend = null;
        h.Email.BeforeGet = _ => { h.Observe("mail-get", 1); return Task.CompletedTask; };
        await using (var restarted = h.Services()) await restarted.GetRequiredService<SignatureProcessingService>().RunOnceAsync(CancellationToken.None);
        Assert.Single(h.Email.SendAttempts); Assert.Equal(operation.ToString("D"), Assert.Single(h.Email.GetAttempts));
        Assert.Equal(0, h.Probe.Held); Assert.Equal(2, h.Probe.Peak);
        await using var done = h.Evidence(); var recovered = await done.SignatureOutbox.SingleAsync(x => x.Id == receipt);
        Assert.Equal(operation, recovered.ProviderOperationId); Assert.Equal(2, recovered.Attempts);
        Assert.Equal("Sent", recovered.State); Assert.NotNull(recovered.CompletedAtUtc); Assert.Null(recovered.LeaseId);
    }

    [SqlServerFact]
    public async Task ExtraHeldConnectionIsDetectedAtActualPackageDependencyBoundary()
    {
        await using var h = await Harness.CreateAsync(packaged: false);
        var detected = false;
        h.Blobs.BeforeRead = async _ =>
        {
            Assert.Equal(2, h.Probe.Held);
            await using (var extra = new ApiDbContext(h.Source.Database.Options(h.Probe)))
            {
                await extra.Database.OpenConnectionAsync();
                Assert.Throws<EqualException>(() => Assert.Equal(2, h.Probe.Held));
                Assert.Equal(3, h.Probe.Held); detected = true;
            }
            Assert.Equal(2, h.Probe.Held);
        };
        await using (var services = h.Services()) await services.GetRequiredService<SignatureProcessingService>().RunOnceAsync(CancellationToken.None);
        Assert.True(detected); Assert.Equal(3, h.Probe.Peak); Assert.Equal(0, h.Probe.Held);
        await using var done = h.Evidence(); Assert.Single(await done.SignaturePackages.ToListAsync());
    }

    private sealed class Harness : IAsyncDisposable
    {
        internal SignatureWorkFixture Source { get; private set; } = null!;
        internal SignatureWorkFixture.Item Item { get; private set; } = null!;
        internal WorkerConnectionProbe Probe { get; } = new();
        internal ProjectionBarrier Projection { get; private set; } = null!;
        internal BlobBarrier Blobs { get; private set; } = null!;
        internal KeyBarrier Keys { get; private set; } = null!;
        internal EmailBarrier Email { get; private set; } = null!;
        internal List<string> Intervals { get; } = [];
        internal void Observe(string interval, int count) { Assert.Equal(count, Probe.Held); Intervals.Add(interval); }

        internal static async Task<Harness> CreateAsync(bool packaged)
        {
            var h = new Harness(); h.Projection = new ProjectionBarrier(h.Probe);
            h.Source = await SignatureWorkFixture.CreateAsync(true, h.Probe, h.Projection);
            try
            {
                h.Item = await h.Source.SeedAsync(h.Source.A, packaged: packaged);
                h.Source.Signature.ExpectedEnvironment = "Demo"; h.Source.Signature.ExpectedDatabaseName = "SatiDemo";
                h.Source.Api = new SatiApiOptions { ExpectedEnvironment = "Demo", ExpectedDatabaseName = "SatiDemo" };
                h.Blobs = new BlobBarrier(h.Source.BlobStore); h.Keys = new KeyBarrier(h.Source.KeyStore); h.Email = new EmailBarrier(h.Source.Email);
                h.Probe.ResetEvidence(); return h;
            }
            catch { await h.DisposeAsync(); throw; }
        }
        internal ApiDbContext Evidence() => new(Source.Database.Options());
        internal ServiceProvider Services() => Source.Services(services =>
        {
            services.RemoveAll<IDemoWorkerResetCoordination>(); services.AddSingleton<IDemoWorkerResetCoordination, SqlDemoWorkerResetCoordination>();
            services.RemoveAll<ISignatureBlobStore>(); services.AddSingleton<ISignatureBlobStore>(Blobs);
            services.RemoveAll<ISignatureOutboxKeyWrapper>(); services.AddSingleton<ISignatureOutboxKeyWrapper>(Keys);
            services.RemoveAll<ISignatureEmailSender>(); services.AddSingleton<ISignatureEmailSender>(Email);
        });
        internal async Task<long> SeedReceiptAsync()
        {
            long id;
            await using (var db = Evidence())
            {
                var invitation = await db.SignatureOutbox.SingleAsync(x => x.Id == Item.Mail);
                var email = await new SignatureOutboxProtector(Source.KeyStore).UnprotectAsync(invitation);
                invitation.CompletedAtUtc = Source.Clock.GetUtcNow().UtcDateTime; invitation.State = "Suppressed"; invitation.Revision++;
                var receipt = new SignatureOutbox { AgencyId = Item.Agency, RequestId = Item.Request, Purpose = "Receipt", NextAttemptAtUtc = Source.Clock.GetUtcNow().UtcDateTime };
                db.SignatureOutbox.Add(receipt); await db.SaveChangesAsync();
                await new SignatureOutboxProtector(Source.KeyStore).ProtectAsync(receipt, email with { Purpose = "Receipt", Link = email.Link.Replace("/s/", "/r/", StringComparison.Ordinal) });
                receipt.Revision++; await db.SaveChangesAsync(); id = receipt.Id;
            }
            Source.Signature.EmailEnabled = true; Source.Signature.AllowedTestRecipients = ["signer@example.test"];
            return id;
        }
        public ValueTask DisposeAsync() => Source.DisposeAsync();
    }

    private sealed class ProjectionBarrier(WorkerConnectionProbe probe) : SaveChangesInterceptor
    {
        internal int Saves;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (data.Context!.ChangeTracker.Entries<SignatureComplianceProjection>().Any(x => x.State == EntityState.Added))
            { Saves++; Assert.Equal(2, probe.Held); Assert.NotNull(data.Context.Database.CurrentTransaction); }
            return ValueTask.FromResult(result);
        }
    }
    private sealed class BlobBarrier(ISignatureBlobStore inner) : ISignatureBlobStore
    {
        internal Func<CancellationToken, Task>? BeforeRead, BeforeWrite;
        public async Task<byte[]> ReadAsync(string path, CancellationToken ct = default)
        { if (BeforeRead is { } callback) await callback(ct); return await inner.ReadAsync(path, ct); }
        public async Task WriteOnceAsync(string path, byte[] bytes, CancellationToken ct = default)
        { if (BeforeWrite is { } callback) await callback(ct); await inner.WriteOnceAsync(path, bytes, ct); }
    }
    private sealed class KeyBarrier(ISignatureOutboxKeyWrapper inner) : ISignatureOutboxKeyWrapper
    {
        internal Func<CancellationToken, Task>? BeforeWrap, BeforeUnwrap;
        public async Task<WrappedDataKey> WrapAsync(byte[] key, CancellationToken ct = default)
        { if (BeforeWrap is { } callback) await callback(ct); return await inner.WrapAsync(key, ct); }
        public async Task<byte[]> UnwrapAsync(byte[] key, string id, CancellationToken ct = default)
        { if (BeforeUnwrap is { } callback) await callback(ct); return await inner.UnwrapAsync(key, id, ct); }
    }
    private sealed class EmailBarrier(ISignatureEmailSender inner) : ISignatureEmailSender
    {
        internal Func<CancellationToken, Task>? BeforeSend, BeforeGet;
        internal List<Guid> SendAttempts { get; } = [];
        internal List<string> GetAttempts { get; } = [];
        public async Task<SignatureEmailResult> SendAsync(Guid id, SignatureEmail email, CancellationToken ct = default)
        { SendAttempts.Add(id); if (BeforeSend is { } callback) await callback(ct); return await inner.SendAsync(id, email, ct); }
        public async Task<SignatureEmailResult> GetStatusAsync(string id, CancellationToken ct = default)
        { GetAttempts.Add(id); if (BeforeGet is { } callback) await callback(ct); return await inner.GetStatusAsync(id, ct); }
    }
}
