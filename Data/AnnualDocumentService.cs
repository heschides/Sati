using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Forms;
using Sati.Models;

namespace Sati.Data;

public sealed class AnnualDocumentService(IDbContextFactory<SatiContext> factory, ISessionService session,
    AnnualPacketComposer composer) : IAnnualDocumentService
{
    private User Actor => session.CurrentUser ?? throw new UnauthorizedAccessException();
    private static async Task<Person> RequirePerson(SatiContext db, User actor, int personId)
    {
        if (!await LocalTenantAccess.CanAccessPersonAsync(db, actor, personId)) throw new UnauthorizedAccessException();
        return await db.People.AsNoTracking().SingleAsync(x => x.Id == personId);
    }
    public async Task<AnnualDocumentsStatusDto> GetStatusAsync(int personId, DateTime cycleStart)
    {
        var actor = Actor; await using var db = await factory.CreateDbContextAsync();
        await LocalTenantAccess.EnsureSessionAsync(db, session);
        var person = await RequirePerson(db, actor, personId);
        return await GetStatusCoreAsync(db, actor, person, cycleStart);
    }
    private static async Task<AnnualDocumentsStatusDto> GetStatusCoreAsync(SatiContext db, User actor, Person person, DateTime cycleStart)
    {
        var personId = person.Id;
        var days = await db.Settings.Where(x => x.AgencyId == actor.AgencyId).Select(x => (int?)x.AnnualPacketOpenDaysBefore).FirstOrDefaultAsync() ?? 30;
        var window = AnnualPacketWindow.ForCycle(person.EffectiveDate ?? throw new InvalidOperationException("Set an effective date first."), cycleStart.Date, DateTime.Today, days);
        var activeArtifacts = await db.DocumentArtifacts.AsNoTracking().Where(x =>
            x.PersonId == personId && x.SupersededByArtifactId == null &&
            (x.CycleStart == cycleStart.Date || x.Kind == AnnualDocumentKind.DhhsAuthorizedRepresentative))
            .ToListAsync();
        var authorizedRepresentative = activeArtifacts
            .Where(x => x.Kind == AnnualDocumentKind.DhhsAuthorizedRepresentative)
            .OrderByDescending(x => x.GeneratedAtUtc).ThenByDescending(x => x.Id)
            .FirstOrDefault();
        var artifacts = activeArtifacts
            .Where(x => x.CycleStart == cycleStart.Date && x.Kind != AnnualDocumentKind.DhhsAuthorizedRepresentative)
            .Select(DocumentArtifactStore.ToDto).ToList();
        if (authorizedRepresentative is not null)
            artifacts.Add(DocumentArtifactStore.ToDto(authorizedRepresentative));
        var authorizedRepresentativeOnFile = activeArtifacts.Any(x =>
            x.Kind == AnnualDocumentKind.DhhsAuthorizedRepresentative &&
            x.Origin == DocumentArtifactOrigin.RecordedAsExternal);
        var ids = artifacts.Select(x => x.Id).ToArray();
        var acknowledged = await db.DocumentAcknowledgments.Where(x => ids.Contains(x.DocumentArtifactId)).Select(x => x.DocumentArtifactId).Distinct().ToListAsync();
        var target = cycleStart.Date;
        var completedTypes = await db.Forms
            .Where(x => x.PersonId == personId && x.TargetEffectiveDate == target &&
                        x.CompletedDate != null &&
                        (x.Type == FormType.PCP || x.Type == FormType.SafetyPlan ||
                         x.Type == FormType.PrivacyPractices))
            .Select(x => x.Type)
            .ToListAsync();
        var releases = await db.ReleaseObligations.AsNoTracking()
            .Include(x => x.Attestations)
            .Where(x => x.PersonId == personId && x.TargetEffectiveDate == target)
            .ToListAsync();
        return new(window, artifacts, acknowledged, AnnualDocumentReminder.Describe(
            window.IsOpen,
            completedTypes.Contains(FormType.PCP),
            artifacts,
            releases.Select(x => x.ToComplianceFact()),
            completedTypes.Contains(FormType.SafetyPlan),
            completedTypes.Contains(FormType.PrivacyPractices)),
            authorizedRepresentativeOnFile);
    }
    public async Task<DocumentAcknowledgmentDto> AcknowledgeAsync(int personId, AcknowledgeDocumentRequest request)
    {
        var actor = Actor; await using var db = await factory.CreateDbContextAsync();
        await LocalTenantAccess.EnsureSessionAsync(db, session);
        await RequirePerson(db, actor, personId);
        var artifact = await db.DocumentArtifacts.SingleOrDefaultAsync(x => x.Id == request.DocumentArtifactId && x.PersonId == personId &&
            x.AgencyId == actor.AgencyId && x.Kind == AnnualDocumentKind.PrivacyPractices && x.Origin == DocumentArtifactOrigin.GeneratedInSati && x.SupersededByArtifactId == null)
            ?? throw new InvalidOperationException("Reload the current privacy notice first.");
        var error = DocumentAcknowledgmentRules.Validate(request, artifact.GeneratedAtUtc.ToLocalTime(), DateTime.Today);
        if (error is not null) throw new ArgumentException(error);
        var receipt = new DocumentAcknowledgment { DocumentArtifactId = artifact.Id, ReceivedOn = request.ReceivedOn?.Date,
            GoodFaithEffortReason = request.GoodFaithEffortReason?.Trim(), RecordedByUserId = actor.Id, RecordedAtUtc = DateTime.UtcNow };
        db.DocumentAcknowledgments.Add(receipt);
        LocalAuditTrail.Record(db, actor, "document.acknowledged", "DocumentArtifact", artifact.Id);
        await db.SaveChangesAsync();
        return new(receipt.Id, receipt.DocumentArtifactId, receipt.ReceivedOn, receipt.GoodFaithEffortReason, receipt.RecordedByUserId, receipt.RecordedAtUtc);
    }
    public async Task<DocumentArtifactDto> RecordAuthorizedRepresentativeOnFileAsync(
        int personId, DateTime cycleStart, string note)
    {
        var actor = Actor; await using var db = await factory.CreateDbContextAsync();
        await LocalTenantAccess.EnsureSessionAsync(db, session);
        var person = await RequirePerson(db, actor, personId);
        if (!SafetyPlanRules.CanAuthor(actor.Id, actor.Permissions, person.UserId))
            throw new UnauthorizedAccessException();
        if (person.EffectiveDate is not DateTime effectiveDate ||
            AnnualDocumentCycle.CurrentStart(effectiveDate, cycleStart) != cycleStart.Date)
            throw new ArgumentException("The annual period must begin on the consumer's effective-date anniversary.", nameof(cycleStart));
        var noteError = AnnualDocumentRules.ValidateExternalNote(note);
        if (noteError is not null) throw new ArgumentException(noteError, nameof(note));

        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        if (await db.DocumentArtifacts.AnyAsync(x => x.PersonId == personId &&
            x.Kind == AnnualDocumentKind.DhhsAuthorizedRepresentative &&
            x.Origin == DocumentArtifactOrigin.RecordedAsExternal && x.SupersededByArtifactId == null))
            throw new InvalidOperationException("A signed DHHS Authorized Representative form is already recorded on file.");
        var artifact = await DocumentArtifactStore.StageExternalAsync(db, personId, actor.AgencyId,
            AnnualDocumentKind.DhhsAuthorizedRepresentative, cycleStart.Date, DateTime.UtcNow, actor.Id, note,
            CancellationToken.None);
        LocalAuditTrail.Record(db, actor, LocalAuditActions.DocumentRecordedExternal, "Person", personId,
            System.Text.Json.JsonSerializer.Serialize(new
            {
                kind = AnnualDocumentKind.DhhsAuthorizedRepresentative.ToString(),
                cycleStart = cycleStart.Date.ToString("yyyy-MM-dd")
            }));
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return DocumentArtifactStore.ToDto(artifact);
    }
    public async Task<VerifyDocumentResult> VerifyAsync(int personId, VerifyDocumentRequest request)
    {
        var actor = Actor; await using var db = await factory.CreateDbContextAsync();
        await LocalTenantAccess.EnsureSessionAsync(db, session);
        await RequirePerson(db, actor, personId);
        var artifact = await db.DocumentArtifacts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.DocumentArtifactId &&
            x.PersonId == personId && x.AgencyId == actor.AgencyId) ?? throw new UnauthorizedAccessException();
        var matches = DocumentVerification.Matches(artifact.ContentSha256, artifact.ByteCount, request);
        LocalAuditTrail.Record(db, actor, "document.verified", "DocumentArtifact", artifact.Id, System.Text.Json.JsonSerializer.Serialize(new { matches }));
        await db.SaveChangesAsync();
        return new(matches, matches ? "This file matches the recorded original." : "This file does not match a recorded generated original.");
    }
    public async Task<AgencyReleaseResult> SavePacketAsync(int personId, DateTime cycleStart)
    {
        var actor = Actor; await using var db = await factory.CreateDbContextAsync();
        await LocalTenantAccess.EnsureSessionAsync(db, session);
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var person = await RequirePerson(db, actor, personId);
        if (!SafetyPlanRules.CanAuthor(actor.Id, actor.Permissions, person.UserId)) throw new UnauthorizedAccessException();
        var status = await GetStatusCoreAsync(db, actor, person, cycleStart);
        if (!status.Window.IsOpen) throw new InvalidOperationException($"The packet opens on {status.Window.OpensOn:d}.");
        var target = cycleStart.Date;
        var today = DateTime.Today;
        var releaseResolution = await ReleaseObligationService.ResolveAssignmentsAsync(
            db, person, target, today, CancellationToken.None);
        var releaseRows = await db.ReleaseObligations
            .Include(x => x.Attestations)
            .Include(x => x.AuthorizationEvents)
            .Where(x => x.PersonId == personId && x.TargetEffectiveDate == target)
            .ToListAsync();
        var releaseChanges = ReleaseObligationService.ReconcileRows(
            db, person, target, releaseResolution, releaseRows, today, DateTime.UtcNow);
        if (releaseChanges.CreatedKeys.Count != 0 || releaseChanges.RetiredKeys.Count != 0)
        {
            LocalAuditTrail.Record(
                db,
                actor,
                LocalAuditActions.ReleaseObligationsReconciled,
                "Person",
                personId,
                System.Text.Json.JsonSerializer.Serialize(new
                {
                    targetEffectiveDate = target.ToString("yyyy-MM-dd"),
                    created = releaseChanges.CreatedKeys,
                    retired = releaseChanges.RetiredKeys,
                    source = "annual-packet"
                }));
            // Assign the durable row ids before artifact rows take their FKs. This
            // flush remains inside the packet transaction.
            await db.SaveChangesAsync();
        }
        var agency = await db.Agencies.AsNoTracking().SingleAsync(x => x.Id == actor.AgencyId);
        var candidates = await db.DocumentTemplates.AsNoTracking().Where(x => x.AgencyId == actor.AgencyId || x.AgencyId == null).ToListAsync();
        var selected = new List<DocumentTemplateDto>();
        foreach (var kind in new[] { AnnualDocumentKind.PrivacyPractices, AnnualDocumentKind.MedicalRecordsRequest })
        {
            var fact = DocumentTemplateResolution.Resolve(actor.AgencyId, kind, candidates.Select(x => new DocumentTemplateFact(x.Id, x.AgencyId, x.Kind.ToString(), x.Version, x.PublishedAtUtc, x.RetiredAtUtc)));
            if (fact is not null)
            {
                var item = candidates.Single(x => x.Id == fact.Id);
                selected.Add(new(item.Id, item.AgencyId, item.Kind.ToString(), item.Version, item.Body, item.PublishedAtUtc, item.PublishedByUserId, item.RetiredAtUtc, DocumentTemplateRules.OwnerName(item.AgencyId)));
            }
        }
        var linked = await db.PersonProviders.Where(x => x.PersonId == personId && x.IsPrimaryCare && x.EndDate == null).Select(x => (int?)x.ProviderId).SingleOrDefaultAsync();
        var directory = await db.Providers.AsNoTracking().Where(x => x.AgencyId == actor.AgencyId).ToListAsync();
        var recipientDirectory = directory.Select(x => new RecordsProviderFact(
            x.Id,
            x.ParentProviderId,
            x.Name,
            AgencyReleaseService.ComposeAddress(x.Street, x.City, x.State, x.Zip),
            x.Phone)).ToList();
        var recipient = RecordsRecipient.Resolve(linked, recipientDirectory);
        var safety = await db.SafetyPlans.AsNoTracking().Where(x => x.PersonId == personId && x.CycleStart == cycleStart.Date).OrderByDescending(x => x.Version).FirstOrDefaultAsync();
        SafetyPlanDto? plan = safety is null ? null : new(safety.Id, safety.PersonId, safety.AuthorUserId, safety.CycleStart, safety.Status, safety.Version, safety.Revision,
            safety.CreatedAtUtc, safety.UpdatedAtUtc, safety.SubmittedAtUtc, safety.ApprovedAtUtc, safety.ApprovedByUserId, safety.ReturnReason, safety.DocumentJson);
        var hasRecipientObligations = await db.ReleaseObligations.AnyAsync(x =>
            x.PersonId == personId && x.TargetEffectiveDate == target);
        var medical = hasRecipientObligations
            ? linked is int providerId && await db.ReleaseObligations.AnyAsync(x =>
                x.PersonId == personId &&
                x.TargetEffectiveDate == target &&
                x.Category == ReleaseObligationCategory.Medical &&
                x.RecipientProviderId == providerId &&
                x.Attestations.Any() &&
                !x.AuthorizationEvents.Any(change =>
                    change.Kind == ReleaseAuthorizationEventKind.Withdrawn &&
                    change.OccurredOn <= DateTime.Today))
            : await db.Forms.AnyAsync(x => x.PersonId == personId &&
                x.Type == FormType.Release_Medical &&
                x.TargetEffectiveDate == target && x.CompletedDate != null);
        var packetReleases = releaseRows
            .Where(x => x.RetiredOn is null || today < x.RetiredOn.Value.Date)
            .Select(x =>
            {
                var details = RecordsRecipient.Resolve(x.RecipientProviderId, recipientDirectory);
                return new PacketReleaseInput(
                    x.Id,
                    x.ObligationId,
                    x.Category,
                    x.CompletedOn,
                    x.RecipientDisplayName ?? details?.Name,
                    details?.Address,
                    details?.Phone);
            })
            .ToList();
        var input = new PacketRenderInput(new(personId, person.FullName, person.BirthDate, person.GuardianName, agency.Name,
            AgencyReleaseService.ComposeAddress(agency.Street, agency.City, agency.State, agency.Zip), agency.EdiContactPhone, actor.DisplayName, actor.Role.ToString()),
            cycleStart.Date, status.Window.EndsOn, DateTime.UtcNow, actor.Id, status.Artifacts, plan, selected, medical,
            recipient?.Name, recipient?.Address, recipient?.Phone, packetReleases);
        var rendered = composer.Render(input);
        var recorded = new List<DocumentArtifactDto>();
        foreach (var file in rendered.Documents)
            recorded.Add(DocumentArtifactStore.ToDto(await DocumentArtifactStore.StageGeneratedAsync(db, personId, actor.AgencyId, file.Kind, cycleStart,
                file.Origin, input.GeneratedAtUtc, actor.Id, file.Pdf, file.FileName, file.BlankFields, default, file.TemplateOwner, file.TemplateKey,
                file.TemplateVersion, file.SourceContentId, file.SourceContentVersion,
                file.ReleaseObligationRecordId)));
        var zip = AnnualPacketComposer.Zip(input, rendered, recorded);
        LocalAuditTrail.Record(db, actor, "annual-packet.saved", "Person", personId);
        await db.SaveChangesAsync(); await transaction.CommitAsync();
        return new(zip, $"Annual-Documents-{personId}-{cycleStart:yyyy-MM-dd}.zip");
    }
}
