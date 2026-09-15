using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sati.Api.Data;
using Sati.Api.Infrastructure;
using Sati.Contracts.V1;
using Sati.Models;
using Xunit;

namespace Sati.Api.Tests;

[Collection(SatiApiCollection.Name)]
public sealed class SignatureComplianceProjectionTests(SatiApiFactory factory)
{
    [Fact]
    public async Task SignedCompletionBecomesOneExactSystemAttestationAndSurvivesWithdrawal()
    {
        using var client = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var signedAtUtc = new DateTime(2026, 3, 8, 4, 30, 0, DateTimeKind.Utc);
        var seeded = await SeedSignedPrivacyCompletionAsync(db, signedAtUtc);
        var projector = scope.ServiceProvider
            .GetRequiredService<SignatureComplianceProjectionService>();

        var applied = await new SignatureStaffSingleAttempt(db).ExecuteAsync(
            () => projector.ProjectCompletionAsync(db, seeded.CompletionId));
        Assert.NotNull(applied);
        Assert.Equal(SignatureComplianceProjectionOutcome.Applied.ToString(), applied!.Outcome);
        Assert.Equal(SignatureComplianceTargets.Form, applied.TargetKind);
        Assert.Equal(seeded.FormId, applied.TargetId);
        Assert.Equal(seeded.ArtifactId, applied.DocumentArtifactId);
        Assert.Equal(seeded.CompletionId, applied.CompletionId);
        Assert.Equal(signedAtUtc, applied.SignedAtUtc);
        Assert.Equal(new DateTime(2026, 3, 7), applied.CompletedOn);
        Assert.True(applied.RecordedAtUtc > applied.SignedAtUtc);
        Assert.NotNull(applied.FormAttestationId);
        Assert.Null(applied.ExistingCompletedOn);

        db.ChangeTracker.Clear();
        var form = await db.Forms.SingleAsync(x => x.Id == seeded.FormId);
        Assert.Equal(new DateTime(2026, 3, 7), form.CompletedDate);
        var attestation = await db.FormAttestations.SingleAsync(x => x.FormId == form.Id);
        Assert.Equal("System", attestation.ActorKind);
        Assert.Null(attestation.ActorUserId);
        Assert.Equal(new DateTime(2026, 3, 7), attestation.CompletedOn);
        Assert.Equal(applied.RecordedAtUtc, attestation.RecordedAtUtc);
        Assert.Contains(seeded.CompletionId.ToString(), attestation.Reason);

        var replay = await new SignatureStaffSingleAttempt(db).ExecuteAsync(
            () => projector.ProjectCompletionAsync(db, seeded.CompletionId));
        Assert.Equal(applied.Id, replay!.Id);
        Assert.Equal(1, await db.Set<SignatureComplianceProjection>()
            .CountAsync(x => x.CompletionId == seeded.CompletionId));
        Assert.Equal(1, await db.FormAttestations.CountAsync(x => x.FormId == form.Id));

        var immutable = await db.Set<SignatureComplianceProjection>()
            .SingleAsync(x => x.CompletionId == seeded.CompletionId);
        immutable.Outcome = SignatureComplianceProjectionOutcome.AlreadySatisfied.ToString();
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();

        var request = await db.SignatureRequests.SingleAsync(x => x.Id == seeded.RequestId);
        request.AuthorizationRevokedAtUtc = DateTime.UtcNow;
        request.AuthorizationRevocationReason = "Synthetic withdrawal after signing.";
        request.Revision++;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        Assert.Equal(new DateTime(2026, 3, 7),
            (await db.Forms.SingleAsync(x => x.Id == seeded.FormId)).CompletedDate);
        Assert.Equal(1, await db.Set<SignatureComplianceProjection>()
            .CountAsync(x => x.CompletionId == seeded.CompletionId));
    }

    [Fact]
    public async Task ExistingManualCompletionIsPreservedAndProjectionRecordsAlreadySatisfied()
    {
        using var client = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var manualDate = new DateTime(2026, 3, 5);
        var seeded = await SeedSignedPrivacyCompletionAsync(
            db,
            new DateTime(2026, 3, 8, 4, 30, 0, DateTimeKind.Utc),
            manualDate);
        var projector = scope.ServiceProvider
            .GetRequiredService<SignatureComplianceProjectionService>();

        var result = await new SignatureStaffSingleAttempt(db).ExecuteAsync(
            () => projector.ProjectCompletionAsync(db, seeded.CompletionId));

        Assert.NotNull(result);
        Assert.Equal(
            SignatureComplianceProjectionOutcome.AlreadySatisfied.ToString(),
            result!.Outcome);
        Assert.Equal(manualDate, result.ExistingCompletedOn);
        Assert.Null(result.FormAttestationId);
        db.ChangeTracker.Clear();
        Assert.Equal(manualDate,
            (await db.Forms.SingleAsync(x => x.Id == seeded.FormId)).CompletedDate);
        Assert.Empty(await db.FormAttestations
            .Where(x => x.FormId == seeded.FormId).ToListAsync());
    }

    [Fact]
    public async Task SignedMedicalArtifactAttestsOnlyItsExactReleaseObligation()
    {
        using var client = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var signedAtUtc = new DateTime(2026, 3, 8, 4, 30, 0, DateTimeKind.Utc);
        var seeded = await SeedSignedMedicalCompletionAsync(db, signedAtUtc);
        var projector = scope.ServiceProvider
            .GetRequiredService<SignatureComplianceProjectionService>();

        var result = await new SignatureStaffSingleAttempt(db).ExecuteAsync(
            () => projector.ProjectCompletionAsync(db, seeded.CompletionId));

        Assert.NotNull(result);
        Assert.Equal(SignatureComplianceProjectionOutcome.Applied.ToString(), result!.Outcome);
        Assert.Equal(SignatureComplianceTargets.ReleaseObligation, result.TargetKind);
        Assert.Equal(seeded.ReleaseObligationId, result.TargetId);
        Assert.Null(result.FormAttestationId);
        Assert.NotNull(result.ReleaseObligationAttestationId);
        Assert.Equal(new DateTime(2026, 3, 7), result.CompletedOn);

        db.ChangeTracker.Clear();
        var obligation = await db.ReleaseObligations
            .Include(item => item.Attestations)
            .SingleAsync(item => item.Id == seeded.ReleaseObligationId);
        var attestation = Assert.Single(obligation.Attestations);
        Assert.Equal(result.ReleaseObligationAttestationId, attestation.Id);
        Assert.Equal(seeded.CompletionId, attestation.SignatureCompletionId);
        Assert.Equal(ReleaseAttestationSource.ElectronicSignature, attestation.Source);
        Assert.Equal(SignerCapacity.Consumer, attestation.SignerCapacity);
        Assert.Equal(new DateTime(2026, 3, 7), obligation.CompletedOn);

        var replay = await new SignatureStaffSingleAttempt(db).ExecuteAsync(
            () => projector.ProjectCompletionAsync(db, seeded.CompletionId));
        Assert.Equal(result.Id, replay!.Id);
        Assert.Equal(1, await db.Set<SignatureComplianceProjection>()
            .CountAsync(item => item.CompletionId == seeded.CompletionId));
        Assert.Equal(1, await db.Set<ReleaseObligationAttestation>()
            .CountAsync(item => item.SignatureCompletionId == seeded.CompletionId));
    }

    private static async Task<SeededCompletion> SeedSignedPrivacyCompletionAsync(
        ApiDbContext db,
        DateTime signedAtUtc,
        DateTime? existingCompletedOn = null)
    {
        var targetEffectiveDate = new DateTime(2026, 3, 7);
        var form = new ServerForm
        {
            Type = "PrivacyPractices",
            TargetEffectiveDate = targetEffectiveDate,
            DueDate = targetEffectiveDate,
            CompletedDate = existingCompletedOn?.Date
        };
        var person = new ServerPerson
        {
            AgencyId = 1,
            UserId = 12,
            FirstName = "Synthetic",
            LastName = $"Projection {Guid.NewGuid():N}",
            Email = "projection@example.test",
            BirthDate = new DateTime(1990, 1, 1),
            EffectiveDate = targetEffectiveDate,
            CreatedAtUtc = DateTime.UtcNow,
            IsTestData = true,
            DayProgramCount = 1,
            Forms = [form]
        };
        db.People.Add(person);
        await db.SaveChangesAsync();

        var artifact = new ServerDocumentArtifact
        {
            AgencyId = 1,
            PersonId = person.Id,
            Kind = nameof(AnnualDocumentKind.PrivacyPractices),
            CycleStart = targetEffectiveDate,
            Origin = nameof(DocumentArtifactOrigin.GeneratedInSati),
            GeneratedAtUtc = signedAtUtc.AddHours(-2),
            GeneratedByUserId = 12,
            ContentSha256 = Hash(Guid.NewGuid().ToString("N")),
            ByteCount = 100,
            SuggestedFileName = "synthetic-privacy.pdf",
            BlankFieldsJson = "[]"
        };
        db.DocumentArtifacts.Add(artifact);
        await db.SaveChangesAsync();

        var frozen = new FrozenSignatureDocument
        {
            AgencyId = 1,
            PersonId = person.Id,
            DocumentArtifactId = artifact.Id,
            ContentSha256 = artifact.ContentSha256!,
            ByteCount = artifact.ByteCount!.Value,
            BlobPath = $"synthetic/{Guid.NewGuid():N}.pdf",
            StoredAtUtc = signedAtUtc.AddHours(-1),
            StoredByUserId = 12
        };
        db.FrozenSignatureDocuments.Add(frozen);
        await db.SaveChangesAsync();

        var request = new SignatureRequest
        {
            AgencyId = 1,
            PersonId = person.Id,
            FrozenDocumentId = frozen.Id,
            ClientRequestId = Guid.NewGuid(),
            SignerCapacity = SignerCapacity.Consumer.ToString(),
            SignerName = $"{person.FirstName} {person.LastName}",
            DeliveryEmail = person.Email!,
            TokenSha256 = Hash(Guid.NewGuid().ToString("N")),
            PinHash = "synthetic-hash",
            PinSalt = "synthetic-salt",
            PinIterations = 100_000,
            PinPepperWrapped = [1],
            PinKeyId = "synthetic-key",
            State = "Signed",
            Revision = 1,
            DisclosureVersion = SignatureRules.DisclosureVersion,
            DisclosureText = SignatureRules.DisclosureText,
            IntentText = SignatureMeaningCatalog
                .Find(AnnualDocumentKind.PrivacyPractices)!.IntentText,
            IssuedAtUtc = signedAtUtc.AddHours(-1),
            IssuedByUserId = 12,
            ExpiresAtUtc = signedAtUtc.AddHours(23),
            CompletedAtUtc = signedAtUtc
        };
        db.SignatureRequests.Add(request);
        await db.SaveChangesAsync();

        var session = new SignatureSession
        {
            AgencyId = 1,
            RequestId = request.Id,
            Purpose = "Signing",
            TokenSha256 = Hash(Guid.NewGuid().ToString("N")),
            AuthenticationVersion = 1,
            IssuedAtUtc = signedAtUtc.AddMinutes(-20),
            ExpiresAtUtc = signedAtUtc.AddMinutes(10),
            DocumentReleasedAtUtc = signedAtUtc.AddMinutes(-15),
            AccessAcknowledgedAtUtc = signedAtUtc.AddMinutes(-10)
        };
        db.SignatureSessions.Add(session);
        await db.SaveChangesAsync();

        var consent = new SignatureConsent
        {
            AgencyId = 1,
            RequestId = request.Id,
            SessionId = session.Id,
            DisclosureVersion = request.DisclosureVersion,
            DisclosureText = request.DisclosureText,
            AcceptedAtUtc = signedAtUtc.AddMinutes(-10)
        };
        db.SignatureConsents.Add(consent);
        await db.SaveChangesAsync();

        var completion = new SignatureCompletion
        {
            AgencyId = 1,
            RequestId = request.Id,
            FrozenDocumentId = frozen.Id,
            SessionId = session.Id,
            ConsentId = consent.Id,
            TypedSignerName = request.SignerName,
            IntentText = request.IntentText,
            SignedAtUtc = signedAtUtc
        };
        db.SignatureCompletions.Add(completion);
        await db.SaveChangesAsync();

        db.SignaturePackages.Add(new SignaturePackage
        {
            AgencyId = 1,
            RequestId = request.Id,
            CompletionId = completion.Id,
            ContentSha256 = Hash(Guid.NewGuid().ToString("N")),
            ByteCount = 200,
            BlobPath = $"synthetic/package/{Guid.NewGuid():N}.pdf",
            CreatedAtUtc = signedAtUtc.AddDays(10)
        });
        await db.SaveChangesAsync();

        return new(person.Id, form.Id, artifact.Id, request.Id, completion.Id);
    }

    private static async Task<SeededReleaseCompletion> SeedSignedMedicalCompletionAsync(
        ApiDbContext db,
        DateTime signedAtUtc)
    {
        var targetEffectiveDate = new DateTime(2026, 3, 7);
        var person = new ServerPerson
        {
            AgencyId = 1,
            UserId = 12,
            FirstName = "Synthetic",
            LastName = $"Release Projection {Guid.NewGuid():N}",
            Email = "release-projection@example.test",
            BirthDate = new DateTime(1990, 1, 1),
            EffectiveDate = targetEffectiveDate,
            CreatedAtUtc = DateTime.UtcNow,
            IsTestData = true,
            DayProgramCount = 1
        };
        var provider = new ServerProvider
        {
            AgencyId = 1,
            Type = "Healthcare",
            MedicalKind = "Individual",
            Name = $"Synthetic Medical {Guid.NewGuid():N}"
        };
        db.People.Add(person);
        db.Providers.Add(provider);
        await db.SaveChangesAsync();

        var plan = ReleaseObligationRules.GenerateCycle(targetEffectiveDate,
        [
            new ReleaseAssignmentFact(
                $"synthetic-provider:{provider.Id}",
                ReleaseAssignmentKind.MedicalProvider,
                targetEffectiveDate.AddYears(-1),
                null,
                targetEffectiveDate)
        ]).Single(item => item.Category == ReleaseObligationCategory.Medical);
        var obligation = ReleaseObligation.Create(
            1,
            person.Id,
            plan,
            signedAtUtc.AddHours(-3),
            provider.Id,
            provider.Name);
        db.ReleaseObligations.Add(obligation);
        await db.SaveChangesAsync();

        var artifact = new ServerDocumentArtifact
        {
            AgencyId = 1,
            PersonId = person.Id,
            Kind = nameof(AnnualDocumentKind.ReleaseMedical),
            CycleStart = targetEffectiveDate,
            Origin = nameof(DocumentArtifactOrigin.GeneratedInSati),
            GeneratedAtUtc = signedAtUtc.AddHours(-2),
            GeneratedByUserId = 12,
            ContentSha256 = Hash(Guid.NewGuid().ToString("N")),
            ByteCount = 100,
            SuggestedFileName = "synthetic-medical-release.pdf",
            BlankFieldsJson = "[]",
            ReleaseObligationId = obligation.Id
        };
        db.DocumentArtifacts.Add(artifact);
        await db.SaveChangesAsync();

        var frozen = new FrozenSignatureDocument
        {
            AgencyId = 1,
            PersonId = person.Id,
            DocumentArtifactId = artifact.Id,
            ContentSha256 = artifact.ContentSha256!,
            ByteCount = artifact.ByteCount!.Value,
            BlobPath = $"synthetic/{Guid.NewGuid():N}.pdf",
            StoredAtUtc = signedAtUtc.AddHours(-1),
            StoredByUserId = 12
        };
        db.FrozenSignatureDocuments.Add(frozen);
        await db.SaveChangesAsync();

        var request = new SignatureRequest
        {
            AgencyId = 1,
            PersonId = person.Id,
            FrozenDocumentId = frozen.Id,
            ClientRequestId = Guid.NewGuid(),
            SignerCapacity = SignerCapacity.Consumer.ToString(),
            SignerName = $"{person.FirstName} {person.LastName}",
            DeliveryEmail = person.Email!,
            TokenSha256 = Hash(Guid.NewGuid().ToString("N")),
            PinHash = "synthetic-hash",
            PinSalt = "synthetic-salt",
            PinIterations = 100_000,
            PinPepperWrapped = [1],
            PinKeyId = "synthetic-key",
            State = "Signed",
            Revision = 1,
            DisclosureVersion = SignatureRules.DisclosureVersion,
            DisclosureText = SignatureRules.DisclosureText,
            IntentText = SignatureMeaningCatalog
                .Find(AnnualDocumentKind.ReleaseMedical)!.IntentText,
            IssuedAtUtc = signedAtUtc.AddHours(-1),
            IssuedByUserId = 12,
            ExpiresAtUtc = signedAtUtc.AddHours(23),
            CompletedAtUtc = signedAtUtc
        };
        db.SignatureRequests.Add(request);
        await db.SaveChangesAsync();

        var session = new SignatureSession
        {
            AgencyId = 1,
            RequestId = request.Id,
            Purpose = "Signing",
            TokenSha256 = Hash(Guid.NewGuid().ToString("N")),
            AuthenticationVersion = 1,
            IssuedAtUtc = signedAtUtc.AddMinutes(-20),
            ExpiresAtUtc = signedAtUtc.AddMinutes(10),
            DocumentReleasedAtUtc = signedAtUtc.AddMinutes(-15),
            AccessAcknowledgedAtUtc = signedAtUtc.AddMinutes(-10)
        };
        db.SignatureSessions.Add(session);
        await db.SaveChangesAsync();

        var consent = new SignatureConsent
        {
            AgencyId = 1,
            RequestId = request.Id,
            SessionId = session.Id,
            DisclosureVersion = request.DisclosureVersion,
            DisclosureText = request.DisclosureText,
            AcceptedAtUtc = signedAtUtc.AddMinutes(-10)
        };
        db.SignatureConsents.Add(consent);
        await db.SaveChangesAsync();

        var completion = new SignatureCompletion
        {
            AgencyId = 1,
            RequestId = request.Id,
            FrozenDocumentId = frozen.Id,
            SessionId = session.Id,
            ConsentId = consent.Id,
            TypedSignerName = request.SignerName,
            IntentText = request.IntentText,
            SignedAtUtc = signedAtUtc
        };
        db.SignatureCompletions.Add(completion);
        await db.SaveChangesAsync();

        return new(person.Id, artifact.Id, obligation.Id, request.Id, completion.Id);
    }

    private static string Hash(string value) => Convert.ToHexString(
        SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));

    private sealed record SeededCompletion(
        int PersonId,
        int FormId,
        int ArtifactId,
        int RequestId,
        int CompletionId);

    private sealed record SeededReleaseCompletion(
        int PersonId,
        int ArtifactId,
        long ReleaseObligationId,
        int RequestId,
        int CompletionId);
}
