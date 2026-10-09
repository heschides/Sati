using Microsoft.EntityFrameworkCore;
using Sati.Api.Data;
using Sati.Api.Infrastructure;
using Sati.Api.Security;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models.Billing;

namespace Sati.Api.Endpoints;

internal static partial class ApiEndpoints
{
    // Call only inside the export/replay serializable transaction. In particular,
    // duplicate-write recovery must begin a new transaction before calling here.
    private static async Task<(ServerBillingPeriod? Period, IResult? Failure)> LoadExportablePeriodAsync(
        ApiDbContext db, Actor actor, int periodId, CancellationToken cancellationToken)
    {
        if (!actor.HasBillingPermissions || !await TenantAccess.IsCurrentActorAsync(db, actor, cancellationToken))
            return (null, Results.Unauthorized());

        return await LoadCompliantPeriodAsync(db, actor.AgencyId, periodId, null, cancellationToken);
    }

    // A worker uses its trusted retained agency and exact mapped subset. It never
    // constructs a human Actor or rebuilds the immutable file from current sources.
    private static async Task<(ServerBillingPeriod? Period, IResult? Failure)> LoadCompliantPeriodAsync(
        ApiDbContext db, int agencyId, int periodId, MappedReleaseFile? retained, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is null || agencyId <= 0)
            throw new InvalidOperationException("Current billing release checks require admitted trusted scope.");

        var period = await (from candidate in db.BillingPeriods.AsNoTracking().Include(value => value.Lines)
                            join owner in db.Users.AsNoTracking() on candidate.UserId equals owner.Id
                            where candidate.Id == periodId && owner.AgencyId == agencyId
                            select candidate).SingleOrDefaultAsync(cancellationToken);
        if (period is null) return (null, Results.NotFound());
        if (period.Lines.Count == 0)
            return (null, Results.ValidationProblem(new Dictionary<string, string[]>
            { ["period"] = ["The billing period has no claim lines."] }));
        if (period.Status != 1)
            return (null, Results.Conflict(new ApiErrorDto("billing_period_not_submitted",
                "Submit and lock the billing period before generating its 837P file.", string.Empty)));
        if (retained is not null)
        {
            var selected = new List<ServerClaimLine>();
            foreach (var claim in retained.Claims)
            {
                var line = period.Lines.SingleOrDefault(row => row.Id == claim.ClaimLineId && row.NoteId == claim.NoteId);
                if (line is null) return (null, OriginalClaimReleaseHeld());
                if (claim.CorrectionId is long correctionId)
                {
                    var correction = await db.ClaimCorrections.AsNoTracking().SingleOrDefaultAsync(row =>
                        row.Id == correctionId && row.AgencyId == agencyId && row.BillingPeriodId == periodId, cancellationToken);
                    if (correction is null) return (null, OriginalClaimReleaseHeld());
                    // Withdrawal preserves the exact standing bill. Current positive
                    // billability must not prevent reversing an improper payment.
                    if (correction.Action == ClaimCorrectionAction.Void) continue;
                    line = CorrectedLine(line, correction);
                }
                selected.Add(line);
            }
            period.Lines = selected;
            if (selected.Count == 0) return (period, null); // lineage/financial review checked separately
        }
        if (EdiReadinessConflict(period) is { } readinessConflict) return (null, readinessConflict);

        var noteIds = period.Lines.Select(line => line.NoteId).Distinct().ToList();
        var sources = await (from note in db.Notes.AsNoTracking()
                             join person in db.People.AsNoTracking() on note.PersonId equals person.Id
                             join owner in db.Users.AsNoTracking() on person.UserId equals owner.Id
                             where noteIds.Contains(note.Id) && owner.AgencyId == agencyId &&
                                   note.AgencyId == agencyId && person.AgencyId == agencyId
                             select new { Note = note, Person = person }).ToListAsync(cancellationToken);
        if (sources.Count != noteIds.Count)
            return (null, Results.Conflict(new ApiErrorDto("invalid_billing_source",
                "The billing period contains a note outside the agency boundary or a missing source record.", string.Empty)));

        var personIds = sources.Select(row => row.Person.Id).Distinct().ToList();
        var forms = await db.Forms.AsNoTracking()
            .Include(form => form.Attestations)
            .Where(form => personIds.Contains(form.PersonId))
            .ToListAsync(cancellationToken);
        var releasesByPerson = await LoadReleaseBillingRowsByPersonAsync(
            db, personIds, cancellationToken);
        var providerLinksByPerson = await LoadReleaseProviderLinksByPersonAsync(
            db, agencyId, personIds, cancellationToken);
        await PopulateContactHistoryAsync(
            db, agencyId, sources.Select(row => row.Person), cancellationToken);
        var compliancePolicy = await LoadBillingCompliancePolicyContextAsync(
            db, agencyId, cancellationToken);
        var recoveryByNote = await LoadRecoveryDecisionsByNoteAsync(
            db, agencyId, noteIds, cancellationToken);
        var approverIds = sources.Where(row => row.Note.OverrideApprovedById.HasValue)
            .Select(row => row.Note.OverrideApprovedById!.Value).Distinct().ToList();
        var agencyApprovers = await db.Users.AsNoTracking()
            .Where(user => user.AgencyId == agencyId && approverIds.Contains(user.Id))
            .Select(user => user.Id).ToListAsync(cancellationToken);
        var errors = new List<string>();
        var days = new Dictionary<(int OwnerId, DateTime Date), List<DayNoteRow>>();
        foreach (var line in period.Lines)
        {
            var source = sources.Single(row => row.Note.Id == line.NoteId);
            var note = source.Note;
            if (await NoteAmendmentBilling.LineContentAsync(db, line.AmendedNoteVersionId, cancellationToken) is { } service)
            { note.EventDate = service.EventDate; note.Minutes = service.Minutes; note.StartTime = service.StartTime; note.IsUnbilled = service.IsUnbilled; }
            var block = ServiceTimeline.TryCreateBlock(note.Id, note.StartTime, note.Minutes,
                ContractMapper.NoteStatusName(note.Status));
            if (block is not null && note.EventDate is DateTime eventDate)
            {
                if (ServiceTimeline.DescribeWindowViolation(block.StartMinutes, block.Minutes) is string windowError)
                    errors.Add($"Note {note.Id}: {windowError}");
                var dayKey = (OwnerId: source.Person.UserId, Date: eventDate.Date);
                if (!days.TryGetValue(dayKey, out var day))
                {
                    day = await LoadDayNotesAsync(db, dayKey.OwnerId, agencyId, dayKey.Date, cancellationToken);
                    days.Add(dayKey, day);
                }
                var blocks = day.Select(row => ServiceTimeline.TryCreateBlock(row.Note.Id,
                    row.Note.StartTime, row.Note.Minutes, ContractMapper.NoteStatusName(row.Note.Status))).OfType<ServiceBlock>();
                if (ServiceTimeline.FindConflicts(block, blocks).Count > 0)
                    errors.Add($"Note {note.Id}: The source service time overlaps another note for this case manager.");
            }
            var facts = new BillingExportSource(source.Person.Id, note.Status, note.EventDate,
                note.ComplianceOverride, note.OverrideReason, note.ApprovedById, note.ApprovedAt,
                note.OverrideApprovedById, note.OverrideApprovedAt,
                note.OverrideApprovedById is int approverId && agencyApprovers.Contains(approverId));
            var personForms = forms.Where(form => form.PersonId == source.Person.Id).ToList();
            var complianceErrors = EvaluateBillingComplianceRelease(
                note,
                source.Person,
                personForms,
                releasesByPerson.GetValueOrDefault(source.Person.Id) ?? [],
                compliancePolicy,
                recoveryByNote.GetValueOrDefault(note.Id) ?? [],
                providerLinksByPerson.GetValueOrDefault(source.Person.Id) ?? []);
            // The release evaluation accounts for exact-obligation exceptions/recovery.
            // This note's form-work deadline is a separate, non-waivable check.
            errors.AddRange(EvaluateFormWorkBilling(note, personForms)
                .Select(error => $"Note {line.NoteId}: {error}"));
            errors.AddRange(BillingExportGate.Evaluate(
                ProfessionalClaimSnapshotCodec.Deserialize(line.ClaimSnapshotJson), agencyId,
                line.DateOfService, line.IsComplianceException, line.ComplianceExceptionReason, facts,
                complianceErrors, compliancePolicy.Resolve(line.DateOfService))
                .Select(error => $"Note {line.NoteId}: {error}"));
        }
        return errors.Count == 0 ? (period, null) :
            (null, Results.Conflict(new ApiErrorDto("billing_export_blocked",
                "The 837P cannot be released. " + string.Join(" ", errors), string.Empty)));
    }

    internal static async Task<bool> IsRetainedReleaseAllowedAsync(ApiDbContext db, ServerEdiGeneration generation,
        ClaimReleaseHistoryProjection projection, MappedReleaseFile retained, CancellationToken token)
    {
        if (!projection.Complete || projection.Defects.Any(defect => defect.Code == "candidate_invalid" ||
            defect.HasDeliveryEvidence && (defect.BillingPeriodId is null || defect.BillingPeriodId == generation.BillingPeriodId)))
            return false;
        await NoteAmendmentDispatchGuard.ValidateAsync(db, generation, token);
        if (generation.IsCorrection)
        {
            var period = await db.BillingPeriods.AsNoTracking().Include(row => row.Lines)
                .SingleAsync(row => row.Id == generation.BillingPeriodId, token);
            var history = await LoadClaimHistoryAsync(db, generation.AgencyId, period, token);
            foreach (var claim in retained.Claims)
            {
                var priorFacts = projection.Facts.Where(row => row.NoteId == claim.NoteId && row.GenerationId != generation.Id).ToList();
                if (priorFacts.Any(row => row.Evidence is OriginalClaimDeliveryEvidence.Queued or OriginalClaimDeliveryEvidence.Uncertain))
                    return false;
                var physicalIds = priorFacts.Where(row => row.Evidence == OriginalClaimDeliveryEvidence.Received)
                    .Select(row => row.GenerationId).ToHashSet();
                var physical = history.SubmissionsFor(claim.NoteId).Where(row => physicalIds.Contains(row.GenerationId)).ToList();
                var options = ClaimCorrectionRules.Evaluate(physical.Select(row => row.Facts).ToList(), false);
                var correction = history.Corrections.SingleOrDefault(row => row.Id == claim.CorrectionId);
                if (correction is null || !options.AllowedActions.Contains(correction.Action) ||
                    options.PayerClaimControlNumber != correction.PayerClaimControlNumber ||
                    physical.LastOrDefault()?.GenerationId != correction.CorrectsEdiGenerationId)
                    return false;
            }
        }
        var current = await LoadCompliantPeriodAsync(db, generation.AgencyId, generation.BillingPeriodId, retained, token);
        return current.Failure is null;
    }
}
