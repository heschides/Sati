using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Models;

namespace Sati.Data;

public sealed partial class ReleaseObligationService(
    IDbContextFactory<SatiContext> contextFactory,
    ISessionService sessionService) : IReleaseObligationService
{
    public async Task<ReleaseObligationStatusDto> GetStatusAsync(
        int personId,
        DateTime targetEffectiveDate,
        CancellationToken cancellationToken = default)
    {
        var actor = CurrentActor();
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await LocalTenantAccess.EnsureSessionAsync(context, sessionService, cancellationToken);
        var person = await LoadAccessiblePersonAsync(context, actor, personId, cancellationToken);
        ValidateTarget(person.EffectiveDate, targetEffectiveDate);
        var resolution = await ResolveAssignmentsAsync(
            context, person, targetEffectiveDate, DateTime.Today, cancellationToken);
        var rows = await LoadRowsAsync(context, personId, targetEffectiveDate, cancellationToken);
        var issues = await LoadStatusIssuesAsync(
            context, personId, targetEffectiveDate, DateTime.Today, rows,
            resolution.Issues, cancellationToken);
        return ToStatus(person, targetEffectiveDate, DateTime.Today, rows, issues);
    }

    public async Task<ReleaseObligationStatusDto> ReconcileAsync(
        int personId,
        DateTime targetEffectiveDate,
        CancellationToken cancellationToken = default)
    {
        var actor = CurrentActor();
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await LocalTenantAccess.EnsureSessionAsync(context, sessionService, cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        var person = await LoadAccessiblePersonAsync(context, actor, personId, cancellationToken);
        ValidateTarget(person.EffectiveDate, targetEffectiveDate);
        var today = DateTime.Today;
        var nowUtc = DateTime.UtcNow;
        var resolution = await ResolveAssignmentsAsync(
            context, person, targetEffectiveDate, today, cancellationToken);
        var rows = await LoadRowsAsync(context, personId, targetEffectiveDate, cancellationToken);
        var changes = ReconcileRows(
            context, person, targetEffectiveDate, resolution, rows, today, nowUtc);

        if (changes.CreatedKeys.Count != 0 || changes.RetiredKeys.Count != 0)
        {
            LocalAuditTrail.Record(
                context,
                actor,
                LocalAuditActions.ReleaseObligationsReconciled,
                "Person",
                person.Id,
                JsonSerializer.Serialize(new
                {
                    targetEffectiveDate = targetEffectiveDate.Date.ToString("yyyy-MM-dd"),
                    created = changes.CreatedKeys,
                    retired = changes.RetiredKeys
                }));
            await context.SaveChangesAsync(cancellationToken);
        }

        var issues = await LoadStatusIssuesAsync(
            context, personId, targetEffectiveDate, today, rows,
            resolution.Issues, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToStatus(person, targetEffectiveDate, today, rows, issues);
    }

    public async Task<ReleaseObligationDto> AttestAsync(
        int personId,
        Guid obligationId,
        DateTime completedOn,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        var actor = CurrentActor();
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await LocalTenantAccess.EnsureSessionAsync(context, sessionService, cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        var person = await LoadAccessiblePersonAsync(context, actor, personId, cancellationToken);
        var row = await LoadRowAsync(context, personId, obligationId, cancellationToken);
        if (row.CompletedOn is not null)
            throw new InvalidOperationException("This release obligation is already attested.");
        if (completedOn.Date < row.AvailableOn.Date)
            throw new ArgumentOutOfRangeException(nameof(completedOn),
                $"This release obligation becomes available on {row.AvailableOn:yyyy-MM-dd}.");

        var linkedNotes = await context.Notes.AsNoTracking()
            .Where(note => note.PersonId == personId && note.AgencyId == actor.AgencyId &&
                note.ReleaseObligationId == row.Id)
            .Select(note => new { note.Id, note.EventDate, note.Status })
            .ToListAsync(cancellationToken);
        if (linkedNotes.Count > 1)
            throw new InvalidOperationException(
                "Several notes are linked to this release. Have a supervisor review the exact evidence before attesting.");
        int evidenceNoteId;
        if (linkedNotes.Count == 1)
        {
            var linked = linkedNotes[0];
            var conflict = ManualAttestationNoteRules.Conflict(
                completedOn, linked.EventDate, (int?)linked.Status,
                await context.ClaimLines.AsNoTracking().AnyAsync(
                    line => line.NoteId == linked.Id, cancellationToken));
            if (conflict is not null)
                throw new InvalidOperationException(conflict);
            evidenceNoteId = linked.Id;
        }
        else
        {
            var releaseFormType = Enum.Parse<FormType>(ReleaseNoteLinkRules.FormTypeName(row.Category));
            var legacyCandidateExists = await context.Notes.AsNoTracking().AnyAsync(note =>
                note.PersonId == personId && note.AgencyId == actor.AgencyId &&
                note.ReleaseObligationId == null && note.FormType == releaseFormType &&
                note.EventDate != null && note.EventDate.Value.Date == completedOn.Date,
                cancellationToken);
            if (legacyCandidateExists)
                throw new InvalidOperationException(
                    "An unlinked release note may already document this work. Review it and select the exact recipient before attesting; Sati will not create a duplicate by guessing.");
            var draft = Note.Create(
                $"Draft: document the {row.Category} release for {row.RecipientDisplayName ?? "DHHS"}.",
                completedOn.Date, NoteStatus.Pending, null, personId,
                releaseFormType, NoteType.Form);
            draft.Activities = NoteActivity.Form;
            draft.ReleaseObligationId = row.Id;
            draft.AgencyId = actor.AgencyId;
            context.Notes.Add(draft);
            LocalAuditTrail.Record(context, actor, LocalAuditActions.NoteCreated, "Note");
            await context.SaveChangesAsync(cancellationToken);
            evidenceNoteId = draft.Id;
        }

        var actorKind = actor.Id == person.UserId
            ? AttestationActorKind.CaseManager
            : AttestationActorKind.Supervisor;
        row.AttestManually(
            completedOn,
            DateTime.Today,
            actorKind,
            actor.Id,
            DateTime.UtcNow,
            reason,
            evidenceNoteId);
        LocalAuditTrail.Record(
            context,
            actor,
            LocalAuditActions.ReleaseObligationAttested,
            "Person",
            personId,
            AuditMetadata(row, completedOn, reason));
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToDto(row, DateTime.Today);
    }

    public async Task<ReleaseObligationDto> WithdrawAsync(
        int personId,
        Guid obligationId,
        DateTime withdrawnOn,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var actor = CurrentActor();
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await LocalTenantAccess.EnsureSessionAsync(context, sessionService, cancellationToken);
        await LoadAccessiblePersonAsync(context, actor, personId, cancellationToken);
        var row = await LoadRowAsync(context, personId, obligationId, cancellationToken);
        row.Withdraw(withdrawnOn, DateTime.Today, actor.Id, DateTime.UtcNow, reason);
        LocalAuditTrail.Record(
            context,
            actor,
            LocalAuditActions.ReleaseAuthorizationWithdrawn,
            "Person",
            personId,
            AuditMetadata(row, withdrawnOn, reason));
        await context.SaveChangesAsync(cancellationToken);
        return ToDto(row, DateTime.Today);
    }

    public async Task<ReleaseObligationDto> RevokeAttestationAsync(
        int personId,
        Guid obligationId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var actor = CurrentActor();
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await LocalTenantAccess.EnsureSessionAsync(context, sessionService, cancellationToken);
        await LoadAccessiblePersonAsync(context, actor, personId, cancellationToken);
        var row = await LoadRowAsync(context, personId, obligationId, cancellationToken);
        var linkedNoteId = row.Attestations.SingleOrDefault(item => item.RevokedAtUtc is null)?.EvidenceNoteId;
        if (linkedNoteId is int noteId)
        {
            var hasClaimLine = await context.ClaimLines.AsNoTracking().AnyAsync(
                line => line.NoteId == noteId, cancellationToken);
            if (hasClaimLine && !actor.HasAdminPermissions)
                throw new UnauthorizedAccessException(
                    "A billing claim line exists for this note. Contact an Admin to correct the release attestation.");
            var status = await context.Notes.AsNoTracking()
                .Where(note => note.Id == noteId)
                .Select(note => (NoteStatus?)note.Status)
                .SingleOrDefaultAsync(cancellationToken);
            if (!hasClaimLine && status is NoteStatus.Logged or NoteStatus.Approved &&
                !actor.HasSupervisorPermissions)
                throw new UnauthorizedAccessException(
                    "This note has reached a supervisor. Ask a supervisor to correct the release attestation.");
        }
        row.RevokeManualAttestation(actor.Id, DateTime.UtcNow, reason);
        LocalAuditTrail.Record(
            context,
            actor,
            LocalAuditActions.ReleaseObligationAttestationRevoked,
            "Person",
            personId,
            AuditMetadata(row, DateTime.Today, reason));
        await context.SaveChangesAsync(cancellationToken);
        return ToDto(row, DateTime.Today);
    }

    private User CurrentActor()
    {
        var actor = sessionService.CurrentUser
            ?? throw new UnauthorizedAccessException("A signed-in case manager is required.");
        if (!actor.HasCaseManagerPermissions && !actor.HasSupervisorPermissions)
            throw new UnauthorizedAccessException(
                "A case manager or supervisor account is required.");
        return actor;
    }

    private static async Task<Person> LoadAccessiblePersonAsync(
        SatiContext context,
        User actor,
        int personId,
        CancellationToken cancellationToken)
    {
        var person = await context.People.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == personId && item.AgencyId == actor.AgencyId,
            cancellationToken)
            ?? throw new UnauthorizedAccessException("That consumer is outside the signed-in agency.");
        if (!await LocalTenantAccess.CanAccessUserAsync(
                context, actor, person.UserId, cancellationToken))
            throw new UnauthorizedAccessException("That consumer is outside the signed-in caseload.");
        return person;
    }

    internal static async Task<ReleaseAssignmentResolutionResult> ResolveAssignmentsAsync(
        SatiContext context,
        Person person,
        DateTime targetEffectiveDate,
        DateTime observedOn,
        CancellationToken cancellationToken)
    {
        var links = await (from link in context.PersonProviders.AsNoTracking()
                           join provider in context.Providers.AsNoTracking()
                               on link.ProviderId equals provider.Id
                           where link.PersonId == person.Id && provider.AgencyId == person.AgencyId
                           select new ReleaseProviderLinkFact(
                               link.Id,
                               link.ProviderId,
                               provider.Type.ToString(),
                               link.Role,
                               link.StartDate,
                               link.EndDate,
                               link.AssignmentKnownOn,
                               provider.Name))
            .ToListAsync(cancellationToken);
        return ReleaseAssignmentResolution.Resolve(
            targetEffectiveDate,
            observedOn,
            RequiresServiceProvider(person),
            links);
    }

    private static bool RequiresServiceProvider(Person person) =>
        person.Waiver != WaiverType.None ||
        person.HasHomeSupport ||
        person.HasSelfDirectedHomeSupport ||
        person.HasSharedLiving ||
        person.HasCommunitySupport1To1 ||
        person.HasCommunitySupportSelfDirected ||
        person.HasCommunitySupportDayProgram;

    private static async Task<List<ReleaseObligation>> LoadRowsAsync(
        SatiContext context,
        int personId,
        DateTime targetEffectiveDate,
        CancellationToken cancellationToken) =>
        await context.ReleaseObligations
            .Include(item => item.Attestations)
            .Include(item => item.AuthorizationEvents)
            .Where(item => item.PersonId == personId &&
                           item.TargetEffectiveDate == targetEffectiveDate.Date)
            .OrderBy(item => item.DueOn)
            .ThenBy(item => item.StableKey)
            .ToListAsync(cancellationToken);

    private static async Task<ReleaseObligation> LoadRowAsync(
        SatiContext context,
        int personId,
        Guid obligationId,
        CancellationToken cancellationToken) =>
        await context.ReleaseObligations
            .Include(item => item.Attestations)
            .Include(item => item.AuthorizationEvents)
            .SingleOrDefaultAsync(
                item => item.PersonId == personId && item.ObligationId == obligationId,
                cancellationToken)
            ?? throw new InvalidOperationException("That release obligation no longer exists.");

    private static async Task<IReadOnlyList<ReleaseLinkageIssue>> LoadStatusIssuesAsync(
        SatiContext context,
        int personId,
        DateTime targetEffectiveDate,
        DateTime today,
        IEnumerable<ReleaseObligation> rows,
        IReadOnlyList<ReleaseLinkageIssue> assignmentIssues,
        CancellationToken cancellationToken)
    {
        var target = targetEffectiveDate.Date;
        var legacyRows = await context.Forms.AsNoTracking()
            .Where(form => form.PersonId == personId &&
                           form.TargetEffectiveDate == target &&
                           form.CompletedDate != null &&
                           (form.Type == FormType.Release_Agency ||
                            form.Type == FormType.Release_DHHS ||
                            form.Type == FormType.Release_Medical))
            .ToListAsync(cancellationToken);
        var legacyIssues = LegacyReleaseCompletionReview.FindIssues(
            target,
            today,
            legacyRows.Select(form => new LegacyReleaseCompletionFact(
                form.Type.ToString(),
                form.TargetEffectiveDate,
                form.CompletedDate!.Value)),
            rows.Select(item => item.ToComplianceFact()));
        return assignmentIssues.Concat(legacyIssues).ToArray();
    }

    internal static ReconcileChanges ReconcileRows(
        SatiContext context,
        Person person,
        DateTime targetEffectiveDate,
        ReleaseAssignmentResolutionResult resolution,
        List<ReleaseObligation> rows,
        DateTime today,
        DateTime nowUtc)
    {
        var reconciliation = ReleaseObligationRules.ReconcileCycle(
            targetEffectiveDate,
            resolution.Assignments,
            rows.Select(item => new ExistingReleaseObligation(
                item.StableKey, item.AssignmentKey, item.RetiredOn)));
        var linksById = resolution.Assignments
            .Where(item => ReleaseAssignmentResolution.TryGetProviderLinkId(
                item.AssignmentKey, out _))
            .ToDictionary(item => item.AssignmentKey, StringComparer.Ordinal);
        var providerSnapshots = context.PersonProviders.AsNoTracking()
            .Where(link => link.PersonId == person.Id)
            .Join(context.Providers.AsNoTracking(), link => link.ProviderId, provider => provider.Id,
                (link, provider) => new { link.Id, link.ProviderId, provider.Name, provider.AgencyId })
            .Where(item => item.AgencyId == person.AgencyId)
            .ToList()
            .ToDictionary(item => item.Id);
        var createdKeys = new List<string>();
        foreach (var plan in reconciliation.ToCreate)
        {
            int? providerId = null;
            string? recipient = null;
            if (plan.AssignmentKey is not null)
            {
                if (!linksById.ContainsKey(plan.AssignmentKey) ||
                    !ReleaseAssignmentResolution.TryGetProviderLinkId(
                        plan.AssignmentKey, out var linkId) ||
                    !providerSnapshots.TryGetValue(linkId, out var snapshot))
                    continue;
                providerId = snapshot.ProviderId;
                recipient = snapshot.Name;
            }

            var row = ReleaseObligation.Create(
                person.AgencyId ?? throw new InvalidOperationException(
                    "The consumer has no agency owner."),
                person.Id,
                plan,
                DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc),
                providerId,
                recipient);
            context.ReleaseObligations.Add(row);
            rows.Add(row);
            if (!person.ReleaseObligations.Any(item =>
                    string.Equals(item.StableKey, row.StableKey, StringComparison.Ordinal)))
                person.ReleaseObligations.Add(row);
            createdKeys.Add(plan.StableKey);
        }

        var retiredKeys = new List<string>();
        foreach (var retirement in reconciliation.ToRetire)
        {
            var row = rows.Single(item => item.StableKey == retirement.StableKey);
            row.Retire(retirement.RetiredOn, DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc));
            retiredKeys.Add(row.StableKey);
        }

        return new ReconcileChanges(createdKeys, retiredKeys);
    }

    private static ReleaseObligationStatusDto ToStatus(
        Person person,
        DateTime targetEffectiveDate,
        DateTime today,
        IEnumerable<ReleaseObligation> rows,
        IReadOnlyList<ReleaseLinkageIssue> issues)
    {
        var signer = ReleaseSigningRules.For(person.HasGuardian);
        return new ReleaseObligationStatusDto(
            person.Id,
            targetEffectiveDate.Date,
            signer.RequiredCapacity.ToString(),
            signer.Label,
            rows.OrderBy(item => item.DueOn).ThenBy(item => item.StableKey)
                .Select(item => ToDto(item, today)).ToArray(),
            issues);
    }

    private static ReleaseObligationDto ToDto(ReleaseObligation row, DateTime today) =>
        new(
            row.Id,
            row.ObligationId,
            row.PersonId,
            row.StableKey,
            row.Category.ToString(),
            row.Trigger.ToString(),
            row.TargetEffectiveDate,
            row.RecipientProviderId,
            row.RecipientDisplayName,
            row.AvailableOn,
            row.DueOn,
            row.AppliesFromOn,
            row.RetiredOn,
            row.CompletedOn,
            row.WithdrawnOn,
            row.IsAuthorizationActive(today),
            row.Attestations.OrderBy(item => item.RecordedAtUtc)
                .Select(item => new ReleaseObligationAttestationDto(
                    item.Id,
                    item.CompletedOn,
                    item.Source.ToString(),
                    item.ActorKind.ToString(),
                    item.ActorUserId,
                    item.SignerCapacity?.ToString(),
                    item.SignatureCompletionId,
                    item.RecordedAtUtc,
                    item.Reason,
                    item.EvidenceNoteId,
                    item.RevokedAtUtc,
                    item.RevokedByUserId,
                    item.RevocationReason))
                .ToArray());

    private static string AuditMetadata(
        ReleaseObligation row,
        DateTime occurredOn,
        string? reason) =>
        JsonSerializer.Serialize(new
        {
            obligationId = row.ObligationId,
            stableKey = row.StableKey,
            category = row.Category.ToString(),
            targetEffectiveDate = row.TargetEffectiveDate.ToString("yyyy-MM-dd"),
            occurredOn = occurredOn.Date.ToString("yyyy-MM-dd"),
            explanationProvided = !string.IsNullOrWhiteSpace(reason)
        });

    private static void ValidateTarget(DateTime? initialEffectiveDate, DateTime targetEffectiveDate)
    {
        if (initialEffectiveDate is not DateTime initial)
            throw new InvalidOperationException(
                "Set the consumer's annual effective date before creating release obligations.");
        var target = targetEffectiveDate.Date;
        var years = target.Year - initial.Date.Year;
        if (years < 0 || initial.Date.AddYears(years) != target)
            throw new ArgumentException(
                "The release target must be one of this consumer's annual effective dates.",
                nameof(targetEffectiveDate));
    }

    internal sealed record ReconcileChanges(
        IReadOnlyList<string> CreatedKeys,
        IReadOnlyList<string> RetiredKeys);
}
