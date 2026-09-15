using System.Data;
using Microsoft.EntityFrameworkCore;
using Sati.Api.Data;
using Sati.Contracts.V1;
using Sati.Models;

namespace Sati.Api.Infrastructure;

/// <summary>
/// API-only bridge from immutable signing evidence to clinical compliance. The
/// public portal never maps the projection table or any clinical form.
/// </summary>
internal sealed class SignatureComplianceProjectionService(ApiClock clock)
{
    public async Task<bool> ProcessNextAsync(
        ApiDbContext db,
        CancellationToken cancellationToken = default)
    {
        var completionId = await (
                from completion in db.SignatureCompletions.AsNoTracking()
                join frozen in db.FrozenSignatureDocuments.AsNoTracking()
                    on new { completion.AgencyId, Id = completion.FrozenDocumentId }
                    equals new { frozen.AgencyId, frozen.Id }
                join artifact in db.DocumentArtifacts.AsNoTracking()
                    on new { frozen.AgencyId, frozen.PersonId, Id = frozen.DocumentArtifactId }
                    equals new { artifact.AgencyId, artifact.PersonId, artifact.Id }
                where !db.Set<SignatureComplianceProjection>()
                    .Any(projection => projection.CompletionId == completion.Id) &&
                    (((artifact.Kind == nameof(AnnualDocumentKind.PrivacyPractices) ||
                       artifact.Kind == nameof(AnnualDocumentKind.SafetyPlan)) &&
                      db.Forms.Any(form =>
                          form.PersonId == artifact.PersonId &&
                          form.TargetEffectiveDate == artifact.CycleStart &&
                          ((artifact.Kind == nameof(AnnualDocumentKind.PrivacyPractices) &&
                            form.Type == "PrivacyPractices") ||
                           (artifact.Kind == nameof(AnnualDocumentKind.SafetyPlan) &&
                            form.Type == "SafetyPlan")))) ||
                     ((artifact.Kind == nameof(AnnualDocumentKind.ReleaseAgency) ||
                       artifact.Kind == nameof(AnnualDocumentKind.ReleaseMedical)) &&
                      artifact.ReleaseObligationId != null &&
                      db.ReleaseObligations.Any(obligation =>
                          obligation.Id == artifact.ReleaseObligationId &&
                          obligation.AgencyId == artifact.AgencyId &&
                          obligation.PersonId == artifact.PersonId &&
                          obligation.TargetEffectiveDate == artifact.CycleStart)))
                orderby completion.Id
                select (int?)completion.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (completionId is null)
            return false;

        await ProjectCompletionAsync(db, completionId.Value, cancellationToken);
        return true;
    }

    public async Task<SignatureComplianceProjection?> ProjectCompletionAsync(
        ApiDbContext db,
        int completionId,
        CancellationToken cancellationToken = default)
    {
        if (completionId <= 0)
            throw new ArgumentOutOfRangeException(nameof(completionId));
        if (db.Database.CurrentTransaction is not null)
            throw new InvalidOperationException(
                "Signature compliance projection requires its own short-lived transaction.");

        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        try
        {
            var existing = await db.Set<SignatureComplianceProjection>()
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    projection => projection.CompletionId == completionId,
                    cancellationToken);
            if (existing is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return existing;
            }

            var completion = await db.SignatureCompletions.AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.Id == completionId,
                    cancellationToken);
            if (completion is null)
            {
                await transaction.CommitAsync(cancellationToken);
                return null;
            }

            var request = await db.SignatureRequests.AsNoTracking()
                .SingleAsync(candidate =>
                    candidate.Id == completion.RequestId &&
                    candidate.AgencyId == completion.AgencyId &&
                    candidate.FrozenDocumentId == completion.FrozenDocumentId,
                    cancellationToken);
            if (request.State != "Signed" ||
                !Enum.TryParse<SignerCapacity>(request.SignerCapacity, out var capacity) ||
                capacity == SignerCapacity.AuthorizedRepresentative)
                throw new InvalidOperationException(
                    "Only a retained consumer or guardian signing decision can satisfy compliance.");

            var frozen = await db.FrozenSignatureDocuments.AsNoTracking()
                .SingleAsync(candidate =>
                    candidate.Id == completion.FrozenDocumentId &&
                    candidate.AgencyId == completion.AgencyId &&
                    candidate.PersonId == request.PersonId,
                    cancellationToken);
            var artifact = await db.DocumentArtifacts.AsNoTracking()
                .SingleAsync(candidate =>
                    candidate.Id == frozen.DocumentArtifactId &&
                    candidate.AgencyId == completion.AgencyId &&
                    candidate.PersonId == request.PersonId,
                    cancellationToken);
            var person = await db.People.AsNoTracking()
                .SingleAsync(candidate =>
                    candidate.Id == request.PersonId &&
                    candidate.AgencyId == completion.AgencyId,
                    cancellationToken);
            if (!ReleaseSigningRules.CanSign(person.HasGuardian, capacity))
                throw new InvalidOperationException(
                    "The recorded signer capacity does not satisfy the consumer's signing policy.");

            if (!Enum.TryParse<AnnualDocumentKind>(artifact.Kind, out var kind) ||
                SignatureMeaningCatalog.Find(kind)?.PolicyStatus !=
                    SignaturePolicyStatus.SyntheticTestingOnly)
            {
                await transaction.CommitAsync(cancellationToken);
                return null;
            }

            var signedAtUtc = DateTime.SpecifyKind(
                completion.SignedAtUtc, DateTimeKind.Utc);
            var recordedAtUtc = clock.UtcNow.UtcDateTime;
            if (signedAtUtc > recordedAtUtc)
                throw new InvalidOperationException(
                    "A signature completion cannot be projected before it occurred.");
            var completedOn = clock.ToAgencyDate(signedAtUtc);

            var releaseCategory = kind switch
            {
                AnnualDocumentKind.ReleaseAgency => ReleaseObligationCategory.Agency,
                AnnualDocumentKind.ReleaseMedical => ReleaseObligationCategory.Medical,
                _ => (ReleaseObligationCategory?)null
            };
            if (releaseCategory is not null)
            {
                if (artifact.ReleaseObligationId is not long releaseObligationId)
                {
                    // A legacy, unscoped release artifact cannot safely identify one recipient
                    // when several obligations of the same kind may coexist.
                    await transaction.CommitAsync(cancellationToken);
                    return null;
                }

                var obligation = await db.ReleaseObligations
                    .Include(item => item.Attestations)
                    .Include(item => item.AuthorizationEvents)
                    .SingleOrDefaultAsync(item =>
                        item.Id == releaseObligationId &&
                        item.AgencyId == completion.AgencyId &&
                        item.PersonId == request.PersonId &&
                        item.TargetEffectiveDate == artifact.CycleStart.Date &&
                        item.Category == releaseCategory.Value,
                        cancellationToken);
                if (obligation is null)
                    throw new InvalidOperationException(
                        "The signed release artifact does not identify a matching release obligation.");

                var matchingAttestation = obligation.Attestations.SingleOrDefault(item =>
                    item.SignatureCompletionId == completion.Id);
                long? releaseAttestationId = null;
                DateTime? releaseExistingCompletedOn = null;
                var releaseOutcome = SignatureComplianceProjectionOutcome.Applied;
                if (matchingAttestation is not null)
                {
                    releaseAttestationId = matchingAttestation.Id;
                }
                else if (obligation.CompletedOn is DateTime priorCompletion)
                {
                    releaseExistingCompletedOn = priorCompletion.Date;
                    releaseOutcome = SignatureComplianceProjectionOutcome.AlreadySatisfied;
                }
                else
                {
                    var attestation = obligation.AttestFromElectronicSignature(
                        completedOn,
                        clock.Today,
                        person.HasGuardian,
                        capacity,
                        completion.Id,
                        recordedAtUtc);
                    await db.SaveChangesAsync(cancellationToken);
                    releaseAttestationId = attestation.Id;
                }

                var releaseProjection = new SignatureComplianceProjection
                {
                    AgencyId = completion.AgencyId,
                    CompletionId = completion.Id,
                    RequestId = completion.RequestId,
                    FrozenDocumentId = completion.FrozenDocumentId,
                    DocumentArtifactId = frozen.DocumentArtifactId,
                    PersonId = request.PersonId,
                    DocumentKind = kind.ToString(),
                    TargetKind = SignatureComplianceTargets.ReleaseObligation,
                    TargetId = obligation.Id,
                    ReleaseObligationAttestationId = releaseAttestationId,
                    Outcome = releaseOutcome.ToString(),
                    SignedAtUtc = signedAtUtc,
                    CompletedOn = completedOn,
                    RecordedAtUtc = recordedAtUtc,
                    SignerCapacity = capacity.ToString(),
                    SignerContactId = request.SignerContactId,
                    ExistingCompletedOn = releaseExistingCompletedOn
                };
                db.Set<SignatureComplianceProjection>().Add(releaseProjection);
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return releaseProjection;
            }

            var formType = FormTypeFor(kind);
            if (formType is null)
            {
                // Agency and medical releases require an artifact-to-recipient-
                // obligation link. Never guess a fixed release row when several
                // recipients may require separate attestations.
                await transaction.CommitAsync(cancellationToken);
                return null;
            }

            var form = await db.Forms.SingleOrDefaultAsync(candidate =>
                candidate.PersonId == request.PersonId &&
                candidate.Type == formType &&
                candidate.TargetEffectiveDate == artifact.CycleStart.Date,
                cancellationToken);
            if (form is null)
            {
                await transaction.CommitAsync(cancellationToken);
                return null;
            }

            long? formAttestationId = null;
            DateTime? existingCompletedOn = form.CompletedDate?.Date;
            var outcome = SignatureComplianceProjectionOutcome.AlreadySatisfied;
            if (existingCompletedOn is null)
            {
                form.ApplyAttestation(completedOn);
                var attestation = new ServerFormAttestation
                {
                    FormId = form.Id,
                    Kind = "Attested",
                    CompletedOn = completedOn,
                    ActorKind = AttestationActorKind.System.ToString(),
                    ActorUserId = null,
                    RecordedAtUtc = recordedAtUtc,
                    PrerequisiteStateJson = FormAttestationRules.NoPrerequisitesStateJson,
                    Reason = $"Electronic signature completion {completion.Id}."
                };
                db.FormAttestations.Add(attestation);
                await db.SaveChangesAsync(cancellationToken);
                formAttestationId = attestation.Id;
                outcome = SignatureComplianceProjectionOutcome.Applied;
            }

            var projection = new SignatureComplianceProjection
            {
                AgencyId = completion.AgencyId,
                CompletionId = completion.Id,
                RequestId = completion.RequestId,
                FrozenDocumentId = completion.FrozenDocumentId,
                DocumentArtifactId = frozen.DocumentArtifactId,
                PersonId = request.PersonId,
                DocumentKind = kind.ToString(),
                TargetKind = SignatureComplianceTargets.Form,
                TargetId = form.Id,
                FormAttestationId = formAttestationId,
                Outcome = outcome.ToString(),
                SignedAtUtc = signedAtUtc,
                CompletedOn = completedOn,
                RecordedAtUtc = recordedAtUtc,
                SignerCapacity = capacity.ToString(),
                SignerContactId = request.SignerContactId,
                ExistingCompletedOn = existingCompletedOn
            };
            db.Set<SignatureComplianceProjection>().Add(projection);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return projection;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    private static string? FormTypeFor(AnnualDocumentKind kind) => kind switch
    {
        AnnualDocumentKind.PrivacyPractices => "PrivacyPractices",
        AnnualDocumentKind.SafetyPlan => "SafetyPlan",
        _ => null
    };
}
