using Microsoft.EntityFrameworkCore;
using Sati.Api.Data;
using Sati.Api.Infrastructure;
using Sati.Api.Security;
using Sati.Contracts.V1;

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

        var period = await (from candidate in db.BillingPeriods.AsNoTracking().Include(value => value.Lines)
                            join owner in db.Users.AsNoTracking() on candidate.UserId equals owner.Id
                            where candidate.Id == periodId && owner.AgencyId == actor.AgencyId
                            select candidate).SingleOrDefaultAsync(cancellationToken);
        if (period is null) return (null, Results.NotFound());
        if (period.Lines.Count == 0)
            return (null, Results.ValidationProblem(new Dictionary<string, string[]>
            { ["period"] = ["The billing period has no claim lines."] }));
        if (period.Status != 1)
            return (null, Results.Conflict(new ApiErrorDto("billing_period_not_submitted",
                "Submit and lock the billing period before generating its 837P file.", string.Empty)));
        if (EdiReadinessConflict(period) is { } readinessConflict) return (null, readinessConflict);

        var noteIds = period.Lines.Select(line => line.NoteId).Distinct().ToList();
        var sources = await (from note in db.Notes.AsNoTracking()
                             join person in db.People.AsNoTracking() on note.PersonId equals person.Id
                             join owner in db.Users.AsNoTracking() on person.UserId equals owner.Id
                             where noteIds.Contains(note.Id) && owner.AgencyId == actor.AgencyId &&
                                   note.AgencyId == actor.AgencyId && person.AgencyId == actor.AgencyId
                             select new { Note = note, Person = person }).ToListAsync(cancellationToken);
        if (sources.Count != noteIds.Count)
            return (null, Results.Conflict(new ApiErrorDto("invalid_billing_source",
                "The billing period contains a note outside the agency boundary or a missing source record.", string.Empty)));

        var personIds = sources.Select(row => row.Person.Id).Distinct().ToList();
        var forms = await db.Forms.AsNoTracking().Where(form => personIds.Contains(form.PersonId))
            .ToListAsync(cancellationToken);
        var requirements = await db.Settings.AsNoTracking().Where(row => row.AgencyId == actor.AgencyId)
            .Select(row => (BillingComplianceRequirements?)row.BillingComplianceRequirements)
            .SingleOrDefaultAsync(cancellationToken) ?? BillingComplianceGate.DefaultRequirements;
        var approverIds = sources.Where(row => row.Note.OverrideApprovedById.HasValue)
            .Select(row => row.Note.OverrideApprovedById!.Value).Distinct().ToList();
        var agencyApprovers = await db.Users.AsNoTracking()
            .Where(user => user.AgencyId == actor.AgencyId && approverIds.Contains(user.Id))
            .Select(user => user.Id).ToListAsync(cancellationToken);
        var today = BillingRules.MaineBusinessDate(DateTimeOffset.UtcNow);
        var errors = new List<string>();
        var days = new Dictionary<(int OwnerId, DateTime Date), List<DayNoteRow>>();
        foreach (var line in period.Lines)
        {
            var source = sources.Single(row => row.Note.Id == line.NoteId);
            var note = source.Note;
            var block = ServiceTimeline.TryCreateBlock(note.Id, note.StartTime, note.Minutes,
                ContractMapper.NoteStatusName(note.Status));
            if (block is not null && note.EventDate is DateTime eventDate)
            {
                if (ServiceTimeline.DescribeWindowViolation(block.StartMinutes, block.Minutes) is string windowError)
                    errors.Add($"Note {note.Id}: {windowError}");
                var dayKey = (OwnerId: source.Person.UserId, Date: eventDate.Date);
                if (!days.TryGetValue(dayKey, out var day))
                {
                    day = await LoadDayNotesAsync(db, dayKey.OwnerId, actor.AgencyId, dayKey.Date, cancellationToken);
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
            errors.AddRange(BillingExportGate.Evaluate(
                ProfessionalClaimSnapshotCodec.Deserialize(line.ClaimSnapshotJson), actor.AgencyId,
                line.DateOfService, line.IsComplianceException, line.ComplianceExceptionReason, facts,
                forms.Where(form => form.PersonId == source.Person.Id).Select(form =>
                    new ComplianceFormSnapshot(form.Type, form.DueDate, form.CompletedDate)).ToList(), today, requirements)
                .Select(error => $"Note {line.NoteId}: {error}"));
        }
        return errors.Count == 0 ? (period, null) :
            (null, Results.Conflict(new ApiErrorDto("billing_export_blocked",
                "The 837P cannot be released. " + string.Join(" ", errors), string.Empty)));
    }
}
