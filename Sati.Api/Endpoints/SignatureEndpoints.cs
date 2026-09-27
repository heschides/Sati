using System.Data;
using System.Net.Mail;
using System.Security.Claims;
using System.Text.Json;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Sati.Api.Data;
using Sati.Api.Infrastructure;
using Sati.Api.Security;
using Sati.Contracts.V1;
using Sati.Signatures;
using Sati.Models;

namespace Sati.Api.Endpoints;

internal static partial class ApiEndpoints
{
    private static void MapSignatures(RouteGroupBuilder api)
    {
        api.MapGet("/signatures/availability", async (SignatureFeature feature, SignatureOptions options,
            ISignatureBlobStore blobs,
            ClaimsPrincipal principal, ApiDbContext db, HttpContext context, CancellationToken ct) =>
        {
            PreventSensitiveResponseCaching(context);
            var actor = Actor.From(principal);
            var agencyEnabled = await db.Settings.AsNoTracking().AnyAsync(
                settings => settings.AgencyId == actor.AgencyId &&
                            settings.IsInternalElectronicSignatureEnabled, ct);
            var enabled = feature.Enabled && agencyEnabled;
            var explanation = !feature.Enabled
                ? "Sati-hosted signing is unavailable in this environment. Continue with the external signing process."
                : !agencyEnabled
                    ? "Your agency has not enabled Sati-hosted signing. Continue with the external signing process."
                    : "Sati-hosted signing is enabled only for consumers explicitly marked as Test. Use fictional records only; delivery remains controlled by this environment.";
            return Results.Ok(new SignatureAvailabilityDto(enabled, explanation,
                enabled && options.EmailEnabled ? "RestrictedTestRecipients" : "Suppressed",
                feature.Enabled, agencyEnabled,
                blobs is not UnconfiguredSignatureBlobStore));
        });
        api.MapGet("/signatures/catalog", (HttpContext context) =>
        { PreventSensitiveResponseCaching(context); return Results.Ok(SignatureMeaningCatalog.All); });
        api.MapGet("/people/{personId:int}/external-signatures", ListExternalSignatures);
        api.MapPost("/people/{personId:int}/external-signatures", RecordExternalSignature);
        api.MapGet("/external-signatures/{evidenceId:int}/signed.pdf", DownloadExternalSignature);
        var signatures = api.MapGroup("").AddEndpointFilter<SignatureEnabledFilter>();
        signatures.MapGet("/people/{personId:int}/signature-signers", GetSignatureSigners);
        signatures.MapGet("/people/{personId:int}/signature-requests", ListSignatureRequests);
        signatures.MapPost("/people/{personId:int}/documents/{artifactId:int}/freeze", FreezeSignatureDocument);
        signatures.MapPost("/signature-requests", CreateStaffSignatureRequest);
        signatures.MapPost("/signature-requests/{requestId:int}/replace", ReplaceStaffSignatureRequest);
        signatures.MapPost("/signature-requests/{requestId:int}/revoke", RevokeStaffSignatureRequest);
        signatures.MapPost("/signature-requests/{requestId:int}/withdraw-authorization", WithdrawStaffSignatureAuthorization);
        signatures.MapGet("/signature-requests/{requestId:int}/original.pdf", DownloadOriginalSignatureDocument);
        signatures.MapGet("/signature-requests/{requestId:int}/signed.pdf", DownloadSignedSignatureDocument);
    }

    private static async Task<IResult> ListExternalSignatures(
        int personId, ClaimsPrincipal principal, ApiDbContext db, HttpContext context,
        CancellationToken ct)
    {
        PreventSensitiveResponseCaching(context);
        var actor = Actor.From(principal);
        if (await AccessibleSafetyPerson(db, actor, personId, ct) is null)
            return Results.NotFound();
        var rows = await db.ExternalSignatureEvidence.AsNoTracking()
            .Where(x => x.AgencyId == actor.AgencyId && x.PersonId == personId)
            .OrderByDescending(x => x.AttestedAtUtc)
            .Select(x => ToExternalSignatureDto(x))
            .ToListAsync(ct);
        return Results.Ok(rows);
    }

    private static async Task<IResult> RecordExternalSignature(
        int personId, RecordExternalSignatureRequest input, ClaimsPrincipal principal,
        ApiDbContext db, ISignatureBlobStore blobs, ApiClock clock, AuditTrail audit,
        CancellationToken ct)
    {
        var actor = Actor.From(principal);
        if (personId != input.PersonId ||
            await AccessibleSafetyPerson(db, actor, personId, ct) is not { } person)
            return Results.NotFound();

        var existing = await db.ExternalSignatureEvidence.AsNoTracking()
            .SingleOrDefaultAsync(x => x.AgencyId == actor.AgencyId &&
                                       x.ClientRequestId == input.ClientRequestId, ct);
        if (existing is not null)
            return Results.Ok(ToExternalSignatureDto(existing));

        var pdfError = ExternalSignatureRules.ValidatePdf(input.Pdf);
        if (pdfError is not null)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["pdf"] = [pdfError] });
        if (input.SignedOn.Date > clock.Today)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["signedOn"] = ["The signing date cannot be in the future."] });
        if (string.IsNullOrWhiteSpace(input.SignerName) || input.SignerName.Trim().Length > 120)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["signerName"] = ["Enter the signer's name (120 characters or fewer)."] });
        if (!ReleaseSigningRules.CanSign(person.HasGuardian, input.SignerCapacity))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["signerCapacity"] = [person.HasGuardian
                ? "The active guardian must sign this release."
                : "The consumer must sign this release."] });
        var signerMatchesRecord = input.SignerCapacity == SignerCapacity.Consumer
            ? SignatureRules.NamesMatch(
                SigningName(person.FirstName, person.LastName), input.SignerName)
            : (await db.PersonContacts.AsNoTracking()
                .Where(x => x.PersonId == personId && x.IsActive && x.Kind == "Guardian")
                .Select(x => new { x.FirstName, x.LastName })
                .ToListAsync(ct))
                .Any(x => SignatureRules.NamesMatch(
                    SigningName(x.FirstName, x.LastName), input.SignerName));
        if (!signerMatchesRecord)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["signerName"] = ["The signer name and capacity must match the current consumer or guardian record."] });
        if (!input.DocumentReviewed || !input.IdentityAndAuthorityVerified ||
            !input.SignaturesAndDatesComplete)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["attestation"] = ["All external-signature verification statements must be affirmed."] });
        var note = input.VerificationNote?.Trim();
        if (note?.Length > ExternalSignatureRules.MaximumNoteLength)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["verificationNote"] = ["The verification note cannot exceed 1,000 characters."] });

        try
        {
            using var stream = new MemoryStream(input.Pdf, writable: false);
            using var document = PdfSharp.Pdf.IO.PdfReader.Open(
                stream, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
            if (document.PageCount == 0)
                throw new InvalidDataException();
        }
        catch
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["pdf"] = ["The uploaded PDF is damaged, encrypted, or cannot be safely read."] });
        }

        var artifact = await db.DocumentArtifacts.AsNoTracking().SingleOrDefaultAsync(x =>
            x.Id == input.DocumentArtifactId && x.AgencyId == actor.AgencyId &&
            x.PersonId == personId && x.SupersededByArtifactId == null &&
            x.Origin == nameof(DocumentArtifactOrigin.GeneratedInSati), ct);
        if (artifact is null || artifact.ReleaseObligationId is not long obligationId ||
            artifact.BlankFieldsJson != "[]" ||
            artifact.Kind is not (nameof(AnnualDocumentKind.ReleaseAgency) or
                                  nameof(AnnualDocumentKind.ReleaseMedical) or
                                  nameof(AnnualDocumentKind.ReleaseDhhs)))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["documentArtifactId"] = ["Choose a complete, current Sati-generated release linked to one recipient obligation."] });

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var obligation = await db.ReleaseObligations.Include(x => x.Attestations)
            .Include(x => x.AuthorizationEvents).SingleOrDefaultAsync(x =>
                x.Id == obligationId && x.AgencyId == actor.AgencyId &&
                x.PersonId == personId && x.TargetEffectiveDate == artifact.CycleStart, ct);
        if (obligation is null)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["documentArtifactId"] = ["The document's recipient obligation is no longer available."] });

        var hash = Convert.ToHexString(SHA256.HashData(input.Pdf));
        var blobPath = $"external/{actor.AgencyId}/{personId}/{artifact.Id}/{input.ClientRequestId:N}.pdf";
        await blobs.WriteOnceAsync(blobPath, input.Pdf, ct);
        var recordedAt = clock.UtcNow.UtcDateTime;
        var evidence = new ExternalSignatureEvidence
        {
            ClientRequestId = input.ClientRequestId,
            AgencyId = actor.AgencyId,
            PersonId = personId,
            DocumentArtifactId = artifact.Id,
            ReleaseObligationId = obligation.Id,
            Method = input.Method.ToString(),
            SignedOn = input.SignedOn.Date,
            SignerName = input.SignerName.Trim(),
            SignerCapacity = input.SignerCapacity.ToString(),
            AttestedByUserId = actor.UserId,
            AttestedAtUtc = recordedAt,
            AttestationText = ExternalSignatureRules.AttestationText,
            BlobPath = blobPath,
            ContentSha256 = hash,
            ByteCount = input.Pdf.LongLength,
            VerificationNote = string.IsNullOrWhiteSpace(note) ? null : note
        };
        db.ExternalSignatureEvidence.Add(evidence);
        await db.SaveChangesAsync(ct);

        if (obligation.CompletedOn is null)
        {
            obligation.AttestManually(input.SignedOn, clock.Today,
                actor.UserId == person.UserId ? AttestationActorKind.CaseManager : AttestationActorKind.Supervisor,
                actor.UserId, recordedAt,
                $"Externally signed PDF evidence {evidence.Id}; staff identity, authority, and completeness verification recorded.");
        }
        audit.Record(actor, "signature.external-verified", "ExternalSignatureEvidence", evidence.Id,
            JsonSerializer.Serialize(new { artifactId = artifact.Id, obligationId, method = input.Method.ToString(), hash, byteCount = input.Pdf.LongLength }));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.Ok(ToExternalSignatureDto(evidence));
    }

    private static async Task<IResult> DownloadExternalSignature(
        int evidenceId, ClaimsPrincipal principal, ApiDbContext db,
        ISignatureBlobStore blobs, AuditTrail audit, HttpContext context, CancellationToken ct)
    {
        PreventSensitiveResponseCaching(context);
        var actor = Actor.From(principal);
        var evidence = await db.ExternalSignatureEvidence.AsNoTracking().SingleOrDefaultAsync(
            x => x.Id == evidenceId && x.AgencyId == actor.AgencyId, ct);
        if (evidence is null ||
            await AccessibleSafetyPerson(db, actor, evidence.PersonId, ct) is null)
            return Results.NotFound();
        var bytes = await blobs.ReadAsync(evidence.BlobPath, ct);
        if (!CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(bytes), Convert.FromHexString(evidence.ContentSha256)))
            throw new SignatureWorkflowException("external_signature_integrity", "The retained signed copy failed its integrity check.", 503);
        audit.Record(actor, "signature.external-document-released", "ExternalSignatureEvidence", evidence.Id);
        await db.SaveChangesAsync(ct);
        return Results.File(bytes, "application/pdf", $"Externally-signed-release-{evidence.Id}.pdf");
    }

    private static ExternalSignatureEvidenceDto ToExternalSignatureDto(ExternalSignatureEvidence x) => new(
        x.Id, x.PersonId, x.DocumentArtifactId, x.ReleaseObligationId, x.Method,
        x.SignedOn, x.SignerName, x.SignerCapacity, x.AttestedByUserId,
        x.AttestedAtUtc, x.ContentSha256, x.ByteCount, x.VerificationNote);

    private static Task<IResult> SignatureTransaction(ApiDbContext db, Func<Task<IResult>> operation, CancellationToken ct) =>
        new SignatureStaffSingleAttempt(db).ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var result = await operation();
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return result;
        });
    private static SignatureActor SigningActor(Actor actor) => new(actor.AgencyId, actor.UserId);

    private static Task<IResult> GetSignatureSigners(int personId, ClaimsPrincipal principal, ApiDbContext db,
        AuditTrail audit, CancellationToken ct) => SignatureTransaction(db, async () =>
    {
        var actor = Actor.From(principal);
        if (await AccessibleSigningPerson(db, actor, personId, ct) is not { } person) return Results.NotFound();
        List<SignatureSignerDto> signers;
        if (person.HasGuardian)
        {
            var guardians = await db.PersonContacts.AsNoTracking()
                .Where(x => x.PersonId == personId && x.IsActive && x.Kind == "Guardian")
                .OrderBy(x => x.LastName).ThenBy(x => x.FirstName).ToListAsync(ct);
            signers = guardians.Select(x => new SignatureSignerDto(
                SignerCapacity.Guardian, x.Id,
                SigningName(x.FirstName, x.LastName), x.Email?.Trim())).ToList();
        }
        else
        {
            signers =
            [
                new SignatureSignerDto(
                    SignerCapacity.Consumer, null,
                    SigningName(person.FirstName, person.LastName), person.Email?.Trim())
            ];
        }
        audit.Record(actor, "signature.staff-signers-released", "Person", personId, JsonSerializer.Serialize(new { count = signers.Count }));
        return Results.Ok(signers);
    }, ct);

    private static Task<IResult> ListSignatureRequests(int personId, ClaimsPrincipal principal, ApiDbContext db,
        SignatureStaffRuntime runtime, AuditTrail audit, CancellationToken ct) => SignatureTransaction(db, async () =>
    {
        var actor = Actor.From(principal);
        if (await AccessibleSafetyPerson(db, actor, personId, ct) is null) return Results.NotFound();
        var result = await runtime.Workflow.ListAsync(SigningActor(actor), personId, ct);
        audit.Record(actor, "signature.staff-history-released", "Person", personId, JsonSerializer.Serialize(new { count = result.Count }));
        return Results.Ok(result);
    }, ct);

    private static Task<IResult> FreezeSignatureDocument(int personId, int artifactId, FreezeSignatureDocumentRequest input,
        ClaimsPrincipal principal, ApiDbContext db, SignatureStaffRuntime runtime, AuditTrail audit, CancellationToken ct) => SignatureTransaction(db, async () =>
    {
        var actor = Actor.From(principal);
        if (await AccessibleSigningPerson(db, actor, personId, ct) is null) return Results.NotFound();
        var frozen = await runtime.Workflow.FreezeAsync(SigningActor(actor), personId, artifactId, input, ct);
        audit.Record(actor, "signature.document-frozen", "DocumentArtifact", artifactId,
            JsonSerializer.Serialize(new { frozen.Id, frozen.ContentSha256, frozen.ByteCount }));
        return Results.Ok(frozen);
    }, ct);

    private static Task<IResult> CreateStaffSignatureRequest(CreateSignatureRequest input, ClaimsPrincipal principal,
        ApiDbContext db, SignatureStaffRuntime runtime, CancellationToken ct) => SignatureTransaction(db, async () =>
    {
        var actor = Actor.From(principal);
        if (await AccessibleSigningPerson(db, actor, input.PersonId, ct) is not { } person) return Results.NotFound();
        var signer = await ResolveSignatureSigner(db, person, input.SignerCapacity, input.SignerContactId, ct);
        ConfirmSignatureSnapshot(signer, input.ExpectedSignerName, input.ExpectedDeliveryEmail);
        return Results.Ok(await runtime.Workflow.CreateAsync(SigningActor(actor), input, signer, ct));
    }, ct);

    private static Task<IResult> ReplaceStaffSignatureRequest(int requestId, ReplaceSignatureRequest input, ClaimsPrincipal principal,
        ApiDbContext db, SignatureStaffRuntime runtime, CancellationToken ct) => SignatureTransaction(db, async () =>
    {
        var actor = Actor.From(principal);
        var request = await AccessibleSignatureRequest(db, actor, requestId, ct);
        if (request is null) return Results.NotFound();
        var person = await AccessibleSigningPerson(db, actor, request.PersonId, ct);
        if (person is null || !Enum.TryParse<SignerCapacity>(request.SignerCapacity, out var capacity)) return Results.NotFound();
        var signer = await ResolveSignatureSigner(db, person, capacity, request.SignerContactId, ct);
        ConfirmSignatureSnapshot(signer, input.ExpectedSignerName, input.ExpectedDeliveryEmail);
        // A fresh request snapshots the currently verified record; the old identity is never rewritten.
        return Results.Ok(await runtime.Workflow.ReplaceAsync(SigningActor(actor), requestId, input, signer, ct));
    }, ct);

    private static Task<IResult> RevokeStaffSignatureRequest(int requestId, SignatureReasonRequest input, ClaimsPrincipal principal,
        ApiDbContext db, SignatureStaffRuntime runtime, CancellationToken ct) => SignatureTransaction(db, async () =>
    {
        var actor = Actor.From(principal);
        if (await AccessibleSignatureRequest(db, actor, requestId, ct) is not { } request ||
            await AccessibleSigningPerson(db, actor, request.PersonId, ct) is null) return Results.NotFound();
        return Results.Ok(await runtime.Workflow.RevokeAsync(SigningActor(actor), requestId, input, ct));
    }, ct);

    private static Task<IResult> WithdrawStaffSignatureAuthorization(int requestId, SignatureReasonRequest input, ClaimsPrincipal principal,
        ApiDbContext db, SignatureStaffRuntime runtime, CancellationToken ct) => SignatureTransaction(db, async () =>
    {
        var actor = Actor.From(principal);
        if (await AccessibleSignatureRequest(db, actor, requestId, ct) is not { } request ||
            await AccessibleSigningPerson(db, actor, request.PersonId, ct) is null) return Results.NotFound();
        return Results.Ok(await runtime.Workflow.WithdrawAuthorizationAsync(SigningActor(actor), requestId, input, ct));
    }, ct);

    private static Task<IResult> DownloadOriginalSignatureDocument(int requestId, ClaimsPrincipal principal, ApiDbContext db,
        SignatureStaffRuntime runtime, AuditTrail audit, CancellationToken ct) => DownloadStaffSignatureDocument(requestId, false, principal, db, runtime, audit, ct);
    private static Task<IResult> DownloadSignedSignatureDocument(int requestId, ClaimsPrincipal principal, ApiDbContext db,
        SignatureStaffRuntime runtime, AuditTrail audit, CancellationToken ct) => DownloadStaffSignatureDocument(requestId, true, principal, db, runtime, audit, ct);
    private static Task<IResult> DownloadStaffSignatureDocument(int requestId, bool signed, ClaimsPrincipal principal, ApiDbContext db,
        SignatureStaffRuntime runtime, AuditTrail audit, CancellationToken ct) => SignatureTransaction(db, async () =>
    {
        var actor = Actor.From(principal);
        if (await AccessibleSignatureRequest(db, actor, requestId, ct) is null) return Results.NotFound();
        var bytes = await runtime.Workflow.DownloadAsync(SigningActor(actor), requestId, signed, ct);
        audit.Record(actor, "signature.staff-document-released", "SignatureRequest", requestId,
            JsonSerializer.Serialize(new { signed, hash = SignatureSecrets.Hash(bytes), byteCount = bytes.LongLength }));
        return Results.File(bytes, "application/pdf", $"Signature-{requestId}-{(signed ? "signed" : "original")}.pdf");
    }, ct);

    private static async Task<Sati.Models.SignatureRequest?> AccessibleSignatureRequest(ApiDbContext db, Actor actor, int requestId, CancellationToken ct)
    {
        var request = await db.SignatureRequests.AsNoTracking().SingleOrDefaultAsync(x => x.Id == requestId && x.AgencyId == actor.AgencyId, ct);
        return request is not null && await AccessibleSafetyPerson(db, actor, request.PersonId, ct) is not null ? request : null;
    }
    private static async Task<ServerPerson?> AccessibleSigningPerson(ApiDbContext db, Actor actor, int personId, CancellationToken ct)
    {
        var person = await AccessibleSafetyPerson(db, actor, personId, ct);
        if (person is not null && !person.IsTestData)
            throw new SignatureWorkflowException("signature_test_consumer_required",
                "Use a fictional consumer explicitly marked as Test when created. Electronic signing is unavailable for ordinary consumer records.");
        return person;
    }
    private static async Task<VerifiedSignatureSigner> ResolveSignatureSigner(ApiDbContext db, ServerPerson person,
        SignerCapacity capacity, int? contactId, CancellationToken ct)
    {
        if (!ReleaseSigningRules.CanSign(person.HasGuardian, capacity))
            throw InvalidSignatureSigner(person.HasGuardian);
        string name; string? email; DateTime? birthDate = null;
        if (capacity == SignerCapacity.Consumer && contactId is null)
        { name = SigningName(person.FirstName, person.LastName); email = person.Email; birthDate = person.BirthDate; }
        else if (capacity is SignerCapacity.Guardian or SignerCapacity.AuthorizedRepresentative && contactId is > 0)
        {
            var contact = await db.PersonContacts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == contactId && x.PersonId == person.Id &&
                x.IsActive && x.Kind == capacity.ToString(), ct);
            if (contact is null) throw InvalidSignatureSigner(person.HasGuardian);
            name = SigningName(contact.FirstName, contact.LastName); email = contact.Email;
        }
        else throw InvalidSignatureSigner(person.HasGuardian);
        email = email?.Trim();
        if (name.Length is < 1 or > 120 || email is null || email.Length > 254 || !MailAddress.TryCreate(email, out var address) ||
            !string.Equals(address.Address, email, StringComparison.OrdinalIgnoreCase)) throw InvalidSignatureSigner(person.HasGuardian);
        return new(name, email, birthDate, person.HasGuardian);
    }
    private static string SigningName(string? first, string? last) => $"{first?.Trim()} {last?.Trim()}".Trim();
    private static void ConfirmSignatureSnapshot(VerifiedSignatureSigner current, string? expectedName, string? expectedEmail)
    {
        if (string.IsNullOrWhiteSpace(expectedName) || string.IsNullOrWhiteSpace(expectedEmail) ||
            !string.Equals(current.Name, expectedName, StringComparison.Ordinal) || !string.Equals(current.Email, expectedEmail, StringComparison.Ordinal))
            throw new SignatureWorkflowException("signature_signer_changed",
                "The signer's name or preferred email changed. Reload, choose the intended signer, and confirm their current details before continuing.", 409);
    }
    private static SignatureWorkflowException InvalidSignatureSigner(bool hasGuardian) => new("signature_signer_invalid",
        hasGuardian
            ? "Guardian signature only. Choose an active guardian from this consumer's contacts and confirm the guardian's current name and preferred email."
            : "This consumer must sign. Correct the consumer's name and preferred email before continuing.");
}
