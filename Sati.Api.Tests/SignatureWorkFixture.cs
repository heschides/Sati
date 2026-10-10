using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sati.Api.Data;
using Sati.Api.Infrastructure;
using Sati.Contracts.V1;
using Sati.Models;
using Sati.Signatures;

namespace Sati.Api.Tests;

/// <summary>Owned synthetic evidence and providers; never opens deployed data or sends mail.</summary>
internal sealed class SignatureWorkFixture(bool sql, IInterceptor[] hooks) : IAsyncDisposable
{
    internal SyntheticPipelineDatabase Database { get; } = new(sql);
    internal ManualTimeProvider Clock { get; } = new();
    internal SignatureOptions Signature { get; } = new()
    {
        Enabled = true, WorkersEnabled = true, ExpectedEnvironment = "Testing", ExpectedDatabaseName = "SatiApiTests",
        PortalBaseUri = "https://sign.example.test/"
    };
    internal SatiApiOptions Api { get; set; } = new()
    { ExpectedEnvironment = "Testing", ExpectedDatabaseName = "SatiApiTests" };
    internal Blobs BlobStore { get; } = new();
    internal Keys KeyStore { get; } = new();
    internal Sender Email { get; } = new();
    internal int A, B, UserA, UserB;
    internal ApiDbContext Open() => new(Database.Options(hooks));
    internal Contexts Factory() => new(Database.Options(hooks));
    internal static async Task<SignatureWorkFixture> CreateAsync(bool sql = false, params IInterceptor[] hooks)
    {
        var f = new SignatureWorkFixture(sql, hooks);
        try
        {
            await f.Database.InitializeAsync();
            await using var db = f.Open();
            var a = new ServerAgency { Name = "Synthetic signature A" };
            var b = new ServerAgency { Name = "Synthetic signature B" };
            db.Agencies.AddRange(a, b); await db.SaveChangesAsync(); f.A = a.Id; f.B = b.Id;
            var ua = new ServerUser { AgencyId = a.Id, Username = "synthetic-signature-a", DisplayName = "Synthetic A", Role = "CaseManager", PasswordHash = "synthetic-only", Salt = "synthetic-only" };
            var ub = new ServerUser { AgencyId = b.Id, Username = "synthetic-signature-b", DisplayName = "Synthetic B", Role = "CaseManager", PasswordHash = "synthetic-only", Salt = "synthetic-only" };
            db.Users.AddRange(ua, ub); await db.SaveChangesAsync(); f.UserA = ua.Id; f.UserB = ub.Id;
            return f;
        }
        catch { await f.DisposeAsync(); throw; }
    }

    internal sealed record Item(int Agency, int Completion, int Request, int Form, long Mail, string OriginalPath);
    internal async Task<Item> SeedAsync(int agency, bool damagedProjection = false, bool packaged = true)
    {
        await using var db = Open();
        var staff = agency == A ? UserA : UserB;
        var signed = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
        var form = new ServerForm { Type = "PrivacyPractices", TargetEffectiveDate = signed.Date, DueDate = signed.Date };
        var person = new ServerPerson
        {
            AgencyId = agency, UserId = staff, FirstName = "Synthetic", LastName = "Signer", Email = "signer@example.test",
            BirthDate = new(1990, 1, 1), EffectiveDate = signed.Date, CreatedAtUtc = signed.AddDays(-1), IsTestData = true, Forms = [form]
        };
        db.People.Add(person); await db.SaveChangesAsync();
        byte[] pdf;
        using (var document = new PdfSharp.Pdf.PdfDocument())
        { document.AddPage(); using var stream = new MemoryStream(); document.Save(stream, false); pdf = stream.ToArray(); }
        var artifact = new ServerDocumentArtifact
        {
            AgencyId = agency, PersonId = person.Id, Kind = nameof(AnnualDocumentKind.PrivacyPractices), CycleStart = signed.Date,
            Origin = nameof(DocumentArtifactOrigin.GeneratedInSati), GeneratedAtUtc = signed.AddHours(-2), GeneratedByUserId = staff,
            ContentSha256 = Hash(pdf), ByteCount = pdf.LongLength, SuggestedFileName = "synthetic-privacy.pdf", BlankFieldsJson = "[]"
        };
        db.DocumentArtifacts.Add(artifact); await db.SaveChangesAsync();
        var frozen = new FrozenSignatureDocument
        {
            AgencyId = agency, PersonId = person.Id, DocumentArtifactId = artifact.Id, ContentSha256 = Hash(pdf), ByteCount = pdf.LongLength,
            BlobPath = $"synthetic/{Guid.NewGuid():N}.pdf", StoredAtUtc = signed.AddHours(-1), StoredByUserId = staff
        };
        BlobStore.Content.Add(frozen.BlobPath, pdf);
        db.FrozenSignatureDocuments.Add(frozen); await db.SaveChangesAsync();
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var request = new SignatureRequest
        {
            AgencyId = agency, PersonId = person.Id, FrozenDocumentId = frozen.Id, ClientRequestId = Guid.NewGuid(),
            SignerCapacity = damagedProjection ? SignerCapacity.AuthorizedRepresentative.ToString() : SignerCapacity.Consumer.ToString(),
            SignerName = "Synthetic Signer", DeliveryEmail = person.Email!, TokenSha256 = Hash(Encoding.UTF8.GetBytes(token)),
            PinHash = "synthetic-hash", PinSalt = "synthetic-salt", PinIterations = 100_000, PinPepperWrapped = [1], PinKeyId = "synthetic-key",
            State = "Signed", Revision = 5, DisclosureVersion = SignatureRules.DisclosureVersion, DisclosureText = SignatureRules.DisclosureText,
            IntentText = SignatureMeaningCatalog.Find(AnnualDocumentKind.PrivacyPractices)!.IntentText,
            IssuedAtUtc = signed.AddHours(-1), IssuedByUserId = staff, ExpiresAtUtc = Clock.GetUtcNow().UtcDateTime.AddDays(1), CompletedAtUtc = signed
        };
        db.SignatureRequests.Add(request); await db.SaveChangesAsync();
        var session = new SignatureSession
        {
            AgencyId = agency, RequestId = request.Id, Purpose = "Signing", TokenSha256 = Hash(RandomNumberGenerator.GetBytes(32)),
            AuthenticationVersion = 1, IssuedAtUtc = signed.AddMinutes(-20), ExpiresAtUtc = signed.AddMinutes(10),
            DocumentReleasedAtUtc = signed.AddMinutes(-15), AccessAcknowledgedAtUtc = signed.AddMinutes(-10)
        };
        db.SignatureSessions.Add(session); await db.SaveChangesAsync();
        var consent = new SignatureConsent
        {
            AgencyId = agency, RequestId = request.Id, SessionId = session.Id, DisclosureVersion = request.DisclosureVersion,
            DisclosureText = request.DisclosureText, AcceptedAtUtc = signed.AddMinutes(-10)
        };
        db.SignatureConsents.Add(consent); await db.SaveChangesAsync();
        var completion = new SignatureCompletion
        {
            AgencyId = agency, RequestId = request.Id, FrozenDocumentId = frozen.Id, SessionId = session.Id, ConsentId = consent.Id,
            TypedSignerName = request.SignerName, IntentText = request.IntentText, SignedAtUtc = signed
        };
        db.SignatureCompletions.Add(completion); await db.SaveChangesAsync();
        var kinds = new[] { "Issued", "Authenticated", "DocumentReleased", "ElectronicConsent", "Signed" };
        for (var index = 0; index < kinds.Length; index++)
            db.SignatureEvents.Add(new SignatureEvent
            {
                AgencyId = agency, RequestId = request.Id, SessionId = index == 0 ? null : session.Id, Sequence = index + 1,
                Kind = kinds[index], ActorKind = index == 0 ? "Staff" : "Signer", ActorUserId = index == 0 ? staff : null,
                OccurredAtUtc = index == 4 ? signed : signed.AddMinutes(-20 + index * 5), DetailJson = "{}"
            });
        if (packaged) db.SignaturePackages.Add(new SignaturePackage
        {
            AgencyId = agency, RequestId = request.Id, CompletionId = completion.Id, ContentSha256 = Hash(pdf), ByteCount = pdf.LongLength,
            BlobPath = $"synthetic/retained/{Guid.NewGuid():N}.pdf", CreatedAtUtc = signed.AddHours(1)
        });
        var mail = new SignatureOutbox { AgencyId = agency, RequestId = request.Id, Purpose = "Invitation", NextAttemptAtUtc = Clock.GetUtcNow().UtcDateTime };
        db.SignatureOutbox.Add(mail); await db.SaveChangesAsync();
        await new SignatureOutboxProtector(KeyStore).ProtectAsync(mail, new(person.Email!, "https://sign.example.test/s/" + token, "Invitation"));
        mail.Revision++; await db.SaveChangesAsync();
        return new(agency, completion.Id, request.Id, form.Id, mail.Id, frozen.BlobPath);
    }

    internal ServiceProvider Services(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDbContextFactory<ApiDbContext>>(Factory()); services.AddSingleton<TimeProvider>(Clock);
        services.AddSingleton(Signature); services.AddSingleton(new SignatureFeature(Signature)); services.AddSingleton(Options.Create(Api));
        services.AddSingleton<ApiClock>(); services.AddSingleton<IDemoWorkerResetCoordination, Reset>();
        services.AddSingleton<ISignatureBlobStore>(BlobStore); services.AddSingleton<ISignatureOutboxKeyWrapper>(KeyStore);
        services.AddSingleton<ISignatureEmailSender>(Email); services.AddSingleton<SignatureOutboxProtector>();
        services.AddSingleton<SignaturePackageBuilder>(); services.AddSingleton<SignatureCompletionWorker>();
        services.AddSingleton<SignatureMailWorker>(); services.AddSingleton<SignatureComplianceProjectionService>();
        services.AddSingleton<SignatureWorkerGate>(); services.AddSingleton<SignatureWorkSelector>();
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<SignatureProcessingService>>(NullLogger<SignatureProcessingService>.Instance);
        services.AddSingleton<SignatureProcessingService>(); configure?.Invoke(services); return services.BuildServiceProvider();
    }
    public ValueTask DisposeAsync() => Database.DisposeAsync();
    private static string Hash(byte[] value) => Convert.ToHexString(SHA256.HashData(value));
    internal sealed class Contexts(DbContextOptions<ApiDbContext> options) : IDbContextFactory<ApiDbContext>
    {
        public ApiDbContext CreateDbContext() => new(options);
        public Task<ApiDbContext> CreateDbContextAsync(CancellationToken token = default)
        { token.ThrowIfCancellationRequested(); return Task.FromResult(CreateDbContext()); }
    }
    internal sealed class Blobs : ISignatureBlobStore
    {
        internal Dictionary<string, byte[]> Content { get; } = [];
        internal List<string> Reads { get; } = [];
        public Task<byte[]> ReadAsync(string path, CancellationToken token = default)
        { token.ThrowIfCancellationRequested(); Reads.Add(path); return Task.FromResult(Content[path].ToArray()); }
        public Task WriteOnceAsync(string path, byte[] content, CancellationToken token = default)
        { token.ThrowIfCancellationRequested(); Content.Add(path, content.ToArray()); return Task.CompletedTask; }
    }
    internal sealed class Keys : ISignatureOutboxKeyWrapper
    {
        public Task<WrappedDataKey> WrapAsync(byte[] key, CancellationToken token = default) => Task.FromResult(new WrappedDataKey(key.ToArray(), "synthetic-key"));
        public Task<byte[]> UnwrapAsync(byte[] key, string id, CancellationToken token = default) => Task.FromResult(key.ToArray());
    }
    internal sealed class Sender : ISignatureEmailSender
    {
        internal List<Guid> Sends { get; } = [];
        internal List<string> Polls { get; } = [];
        public Task<SignatureEmailResult> SendAsync(Guid id, SignatureEmail email, CancellationToken token = default)
        { Sends.Add(id); return Task.FromResult(new SignatureEmailResult("Sent", id.ToString("D"))); }
        public Task<SignatureEmailResult> GetStatusAsync(string id, CancellationToken token = default)
        { Polls.Add(id); return Task.FromResult(new SignatureEmailResult("Sent", id)); }
    }
    private sealed class Reset : IDemoWorkerResetCoordination
    {
        public Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> operation, T unavailable, CancellationToken token) => operation(token);
        public Task<T> RunDispatchAsync<T>(Guid id, Func<CancellationToken, Task<T>> operation, T unavailable, CancellationToken token) => throw new NotSupportedException();
        public Task<T> RunAccountPreflightAsync<T>(int agency, Guid account, Func<IAccountPreflightLease, CancellationToken, Task<T>> operation, T unavailable, CancellationToken token) => throw new NotSupportedException();
    }
}
