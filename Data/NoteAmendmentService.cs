using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using SatiLogica.Contracts;
using Sati.Models;
using System.Text.Json;

namespace Sati.Data;

public sealed class NoteAmendmentService(IDbContextFactory<SatiContext> factory, ISessionService session,
    TimeProvider? timeProvider = null) : INoteAmendmentService
{
    private TimeProvider Clock => timeProvider ?? TimeProvider.System;
    public async Task<IReadOnlyList<NoteAmendmentFinancialItem>> GetFinancialQueueAsync(int afterNoteId = 0)
    {
        await using var db = factory.CreateDbContext(); var actor = await LocalTenantAccess.EnsureSessionAsync(db, session);
        if (!actor.HasBillingPermissions) throw new UnauthorizedAccessException("Billing permission is required.");
        var rows = await db.Notes.AsNoTracking().Where(n => n.Id > afterNoteId && n.AgencyId == actor.AgencyId &&
            n.Person.AgencyId == actor.AgencyId && n.Person.User!.AgencyId == actor.AgencyId && n.Person.UserId != actor.Id &&
            db.NoteAmendments.Any(a => a.NoteId == n.Id && a.Status == NoteAmendmentStatus.Approved && a.ChangesFinancialFacts))
            .OrderBy(n => n.Id).Take(NoteAmendmentRules.PageSize).Select(n => new { n.Id, n.Person.FirstName, n.Person.LastName,
                n.Revision, n.EventDate, n.Minutes, n.StartTime, n.IsUnbilled }).ToListAsync();
        var result = new List<NoteAmendmentFinancialItem>();
        foreach (var n in rows)
        {
            var a = await db.NoteAmendments.AsNoTracking().Where(a => a.NoteId == n.Id && a.AgencyId == actor.AgencyId && a.Status == NoteAmendmentStatus.Approved && a.ChangesFinancialFacts).OrderByDescending(a => a.ApprovedVersionId).Select(a => new { a.ApprovedVersionId, a.ChangesFinancialFacts }).FirstAsync();
            if (!a.ChangesFinancialFacts) continue;
            var v = await db.NoteAmendmentVersions.AsNoTracking().Where(v => v.Id == a.ApprovedVersionId).Select(v => new { v.Id, v.FinancialContentJson }).SingleAsync();
            result.Add(new(n.Id, $"{n.FirstName} {n.LastName}".Trim(), v.Id, n.Revision, new("", n.EventDate, n.Minutes, n.StartTime, n.IsUnbilled),
                JsonSerializer.Deserialize<NoteAmendmentContent>(v.FinancialContentJson)!, await db.ClaimLines.AnyAsync(l => l.NoteId == n.Id),
                await db.Set<NoteAmendmentFinancialReview>().AnyAsync(r => r.ApprovedVersionId == v.Id)));
        }
        return result;
    }
    public async Task<NoteAmendmentFinancialReviewDto> ReviewFinancialAsync(int noteId, NoteAmendmentFinancialReviewRequest request)
    {
        await using var db = factory.CreateDbContext(); var actor = await LocalTenantAccess.EnsureSessionAsync(db, session);
        if (!actor.HasBillingPermissions) throw new UnauthorizedAccessException("Billing permission is required.");
        var key = await db.Notes.AsNoTracking().Where(n => n.Id == noteId && n.AgencyId == actor.AgencyId && n.Person.AgencyId == actor.AgencyId && n.Person.User!.AgencyId == actor.AgencyId)
            .Select(n => new { n.Person.UserId, n.Revision }).SingleOrDefaultAsync() ?? throw new NoteAmendmentWorkflowException(404, "not_found", "The note is unavailable.");
        await using var transaction = await ServiceTimeWriteScope.BeginAsync(db, actor.AgencyId, key.UserId);
        await LocalTenantAccess.EnsureCurrentActorAsync(db, actor);
        var revision = await db.Notes.Where(n => n.Id == noteId && n.Person.UserId == key.UserId).Select(n => n.Revision).SingleAsync();
        var result = await NoteAmendmentBilling.ReviewAsync(db, actor.ToAgencyActor(), noteId, key.UserId, revision, request, Clock.GetUtcNow().UtcDateTime,
            v => LocalAuditTrail.Record(db, actor, "note.amendment.financial-reviewed", "Note", noteId, JsonSerializer.Serialize(new { versionId = v })));
        await transaction.CommitAsync(); return result;
    }
    private static IQueryable<Note> Scoped(SatiContext db, User actor, bool review) =>
        from note in db.Notes
        join person in db.People on note.PersonId equals person.Id
        join owner in db.Users on person.UserId equals owner.Id
        where note.AgencyId == actor.AgencyId && person.AgencyId == actor.AgencyId && owner.AgencyId == actor.AgencyId &&
            (owner.Permissions & UserPermissions.CaseManagement) != 0 &&
            (review ? actor.HasSupervisorPermissions && owner.Id != actor.Id &&
                (UserPermissionRules.HasAgencyWideSupervisionPermissions(actor.Permissions) || owner.SupervisorId == actor.Id) :
                actor.HasCaseManagerPermissions && owner.Id == actor.Id)
        select note;

    public async Task<NoteAmendmentQueuePage> GetQueueAsync(bool review, int afterNoteId = 0)
    {
        await using var db = factory.CreateDbContext();
        var actor = await LocalTenantAccess.EnsureSessionAsync(db, session);
        var rows = await Scoped(db, actor, review).AsNoTracking().Include(n => n.Person)
            .Where(n => n.Id > afterNoteId && n.Status == NoteStatus.Approved && (!review ||
                db.NoteAmendments.Any(a => a.NoteId == n.Id && a.Status == NoteAmendmentStatus.Submitted && a.AuthorId != actor.Id)))
            .OrderBy(n => n.Id).Take(NoteAmendmentRules.PageSize + 1).ToListAsync();
        var page = rows.Take(NoteAmendmentRules.PageSize).ToArray();
        var ids = page.Select(n => n.Id).ToArray();
        var amendments = await db.NoteAmendments.AsNoTracking().Where(a => ids.Contains(a.NoteId) && a.AgencyId == actor.AgencyId)
            .Select(a => new { a.NoteId, a.CurrentVersionId, a.Status }).ToListAsync();
        var held = await NoteAmendmentBilling.HeldNoteIds(db).Where(id => ids.Contains(id)).ToListAsync();
        return new(page.Select(n => new NoteAmendmentQueueItem(n.Id, n.PersonId, n.Person.FullName ?? string.Empty, n.EventDate,
            amendments.Where(a => a.NoteId == n.Id).OrderByDescending(a => a.CurrentVersionId).FirstOrDefault()?.Status.ToString() ?? "Approved original",
            held.Contains(n.Id))).ToArray(),
            rows.Count > NoteAmendmentRules.PageSize ? page[^1].Id : null);
    }
    public async Task<NoteAmendmentWorkspaceDto> GetAsync(int noteId)
    {
        await using var db = factory.CreateDbContext();
        var actor = await LocalTenantAccess.EnsureSessionAsync(db, session);
        var note = await LoadAsync(db, actor, noteId);
        return await NoteAmendmentWorkflow.ReadAsync(db, actor.ToAgencyActor(), ToDto(note), note.Person.UserId,
            actor.HasSupervisorPermissions && note.Person.UserId != actor.Id,
            await db.ClaimLines.AnyAsync(l => l.NoteId == noteId));
    }
    public async Task<NoteAmendmentResultDto> ActAsync(int noteId, NoteAmendmentRequest request)
    {
        await using var db = factory.CreateDbContext();
        var actor = await LocalTenantAccess.EnsureSessionAsync(db, session);
        var key = await LoadAsync(db, actor, noteId);
        await using var transaction = await ServiceTimeWriteScope.BeginAsync(db, actor.AgencyId, key.Person.UserId);
        db.ChangeTracker.Clear();
        await LocalTenantAccess.EnsureCurrentActorAsync(db, actor);
        var note = await LoadAsync(db, actor, noteId);
        if (note.Person.UserId != key.Person.UserId) throw new NoteAmendmentWorkflowException(409, NoteAmendmentRules.RevisionCode, "The caseload assignment changed.");
        try
        {
            var result = await NoteAmendmentWorkflow.ExecuteAsync(db, actor.ToAgencyActor(), ToDto(note), note.Person.UserId,
                actor.HasSupervisorPermissions && note.Person.UserId != actor.Id, request,
                TenantClock.MaineDate(Clock.GetUtcNow()), Clock.GetUtcNow().UtcDateTime,
                async (content, financialChange) =>
                {
                    if (request.Action == NoteAmendmentAction.Approve && financialChange)
                        await NoteAmendmentBilling.EnsureTransportResolvedAsync(db, actor.AgencyId,
                            from g in db.EdiGenerations join l in db.ClaimLines on g.BillingPeriodId equals l.BillingPeriodId where l.NoteId == noteId && g.AgencyId == actor.AgencyId select g.Id);
                    await ValidateScheduleAsync(db, note, content);
                },
                (action, amendmentId, versionId) => LocalAuditTrail.Record(db, actor, action, "Note", noteId,
                    JsonSerializer.Serialize(new { amendmentId, versionId })), default);
            await transaction.CommitAsync();
            return result;
        }
        catch (DbUpdateException e) when (e is DbUpdateConcurrencyException || e.InnerException is Microsoft.Data.SqlClient.SqlException sql && sql.Number is 2601 or 2627 or 1205)
        { throw new NoteAmendmentWorkflowException(409, NoteAmendmentRules.RevisionCode, "The amendment changed. Reload before retrying."); }
    }
    private static async Task<Note> LoadAsync(SatiContext db, User actor, int noteId) =>
        await Scoped(db, actor, false).Concat(Scoped(db, actor, true)).Include(n => n.Person).FirstOrDefaultAsync(n => n.Id == noteId)
        ?? throw new NoteAmendmentWorkflowException(404, "not_found", "The note is unavailable.");

    private static async Task ValidateScheduleAsync(SatiContext db, Note note, NoteAmendmentContent content)
    {
        if (content.EventDate?.Date != note.EventDate?.Date && (note.FormId is not null || note.ReleaseObligationId is not null))
            throw new NoteAmendmentWorkflowException(400, "note_amendment_linked_date", "Correct the exact form or release evidence through its existing correction workflow before changing this service date.");
        if (ServiceTimeline.TryCreateBlock(note.Id, content.StartTime, content.Minutes, "Approved") is not { } candidate) return;
        var rows = await db.Notes.AsNoTracking().Where(n => n.AgencyId == note.AgencyId && n.Person.UserId == note.Person.UserId &&
            n.Person.AgencyId == note.AgencyId && n.EventDate >= content.EventDate!.Value.Date && n.EventDate < content.EventDate.Value.Date.AddDays(1) && n.Id != note.Id)
            .Select(n => new { n.Id, n.StartTime, n.Minutes, n.Status }).ToListAsync();
        var blocks = rows.Select(n => ServiceTimeline.TryCreateBlock(n.Id, n.StartTime, n.Minutes, n.Status?.ToString())).OfType<ServiceBlock>();
        if (ServiceTimeline.FindConflicts(candidate, blocks).Count != 0 || await NoteAmendmentSchedule.ConflictsAsync(db,
            db.Notes.Where(n => n.AgencyId == note.AgencyId && n.Person.AgencyId == note.AgencyId && n.Person.UserId == note.Person.UserId).Select(n => n.Id),
            note.AgencyId!.Value, content.EventDate!.Value, candidate))
            throw new NoteAmendmentWorkflowException(409, "note_amendment_time_overlap", "The proposed service time overlaps another recorded service. No amendment was submitted or approved.");
    }
    internal static NoteDto ToDto(Note n) => new(n.Id, n.Narrative, n.EventDate, n.Status?.ToString(), n.Minutes, n.StartTime,
        n.PersonId, n.FormType?.ToString(), n.NoteType?.ToString(), n.AgencyId, n.ReturnReason, n.ReturnedById, n.ApprovedById,
        n.ApprovedAt, n.ReturnedAt, n.CaseManagerJustification, n.VisitDocumentationJson, n.ComplianceOverride, n.OverrideReason,
        n.OverrideApprovedById, n.OverrideApprovedAt, n.Revision, null, OverrideObligationIds: n.OverrideObligationIds,
        OverrideAttestationConfirmed: n.OverrideAttestationConfirmed, GoalProgress: n.GoalProgress?.ToString(), FormId: n.FormId,
        FormDateCorrectionReason: n.FormDateCorrectionReason, Activities: (int?)n.Activities, ReleaseObligationId: n.ReleaseObligationId,
        IsAnnualPlan: n.IsAnnualPlan, IsUnbilled: n.IsUnbilled);
}




