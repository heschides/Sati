using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sati.Api.Data;
using Sati.Api.Infrastructure;
using Sati.Api.Security;
using Sati.Contracts.V1;
using Sati.Data;

namespace Sati.Api.Endpoints;

internal static partial class ApiEndpoints
{
    private static async Task<long?> ApplyFinancialAmendmentAsync(ApiDbContext db, int agencyId, ServerNote note, CancellationToken ct)
    {
        var approved = await NoteAmendmentBilling.AuthorizedContentAsync(db, agencyId, note.Id, ct);
        if (approved is not { } a) return null;
        note.EventDate = a.Content.EventDate; note.Minutes = a.Content.Minutes; note.StartTime = a.Content.StartTime; note.IsUnbilled = a.Content.IsUnbilled;
        return a.VersionId;
    }
    private static IQueryable<ServerNote> AmendmentScope(ApiDbContext db, Actor actor, bool review) =>
        from note in db.Notes join person in db.People on note.PersonId equals person.Id join owner in db.Users on person.UserId equals owner.Id
        where note.AgencyId == actor.AgencyId && person.AgencyId == actor.AgencyId && owner.AgencyId == actor.AgencyId &&
            (owner.Permissions & UserPermissions.CaseManagement) != 0 &&
            (review ? actor.HasSupervisorPermissions && owner.Id != actor.UserId &&
                (actor.HasAgencyWideSupervisionPermissions || owner.SupervisorId == actor.UserId) : actor.HasCaseManagerPermissions && owner.Id == actor.UserId)
        select note;

    private static void MapNoteAmendments(RouteGroupBuilder api)
    {
        api.MapGet("/billing/note-amendments", async Task<IResult> (int afterNoteId, ClaimsPrincipal principal, ApiDbContext db, CancellationToken ct) =>
        {
            var actor = Actor.From(principal); if (!actor.HasBillingPermissions) return Results.Forbid();
            var rows = await (from n in db.Notes.AsNoTracking() join p in db.People.AsNoTracking() on n.PersonId equals p.Id
                join u in db.Users.AsNoTracking() on p.UserId equals u.Id where n.Id > afterNoteId && n.AgencyId == actor.AgencyId &&
                p.AgencyId == actor.AgencyId && u.AgencyId == actor.AgencyId && u.Id != actor.UserId &&
                db.NoteAmendments.Any(a => a.NoteId == n.Id && a.Status == NoteAmendmentStatus.Approved && a.ChangesFinancialFacts)
                orderby n.Id select new { n.Id, p.FirstName, p.LastName, n.Revision, n.EventDate, n.Minutes, n.StartTime, n.IsUnbilled }).Take(NoteAmendmentRules.PageSize).ToListAsync(ct);
            var result = new List<NoteAmendmentFinancialItem>();
            foreach (var n in rows)
            {
                var a = await db.NoteAmendments.AsNoTracking().Where(a => a.NoteId == n.Id && a.AgencyId == actor.AgencyId && a.Status == NoteAmendmentStatus.Approved && a.ChangesFinancialFacts).OrderByDescending(a => a.ApprovedVersionId).Select(a => new { a.ApprovedVersionId, a.ChangesFinancialFacts }).FirstAsync(ct);
                if (!a.ChangesFinancialFacts) continue;
                var v = await db.NoteAmendmentVersions.AsNoTracking().Where(v => v.Id == a.ApprovedVersionId).Select(v => new { v.Id, v.FinancialContentJson }).SingleAsync(ct);
                result.Add(new(n.Id, $"{n.FirstName} {n.LastName}".Trim(), v.Id, n.Revision, new("", n.EventDate, n.Minutes, n.StartTime, n.IsUnbilled),
                    JsonSerializer.Deserialize<NoteAmendmentContent>(v.FinancialContentJson)!, await db.ClaimLines.AnyAsync(l => l.NoteId == n.Id, ct),
                    await db.Set<Sati.Models.NoteAmendmentFinancialReview>().AnyAsync(r => r.ApprovedVersionId == v.Id, ct)));
            }
            return Results.Ok(result);
        });
        api.MapPost("/billing/notes/{noteId:int}/amendment-review", async Task<IResult> (int noteId, NoteAmendmentFinancialReviewRequest request,
            ClaimsPrincipal principal, ApiDbContext db, AuditTrail audit, ApiClock clock, CancellationToken ct) =>
        {
            var actor = Actor.From(principal); if (!actor.HasBillingPermissions) return Results.Forbid();
            var key = await (from n in db.Notes join p in db.People on n.PersonId equals p.Id join u in db.Users on p.UserId equals u.Id
                where n.Id == noteId && n.AgencyId == actor.AgencyId && p.AgencyId == actor.AgencyId && u.AgencyId == actor.AgencyId
                select new { p.UserId }).SingleOrDefaultAsync(ct);
            if (key is null) return Results.NotFound();
            await using var transaction = await ServiceTimeWriteScope.BeginAsync(db, actor.AgencyId, key.UserId, ct);
            if (!await TenantAccess.IsCurrentActorAsync(db, actor, ct)) return Results.Unauthorized();
            var revision = await (from n in db.Notes join p in db.People on n.PersonId equals p.Id
                where n.Id == noteId && n.AgencyId == actor.AgencyId && p.AgencyId == actor.AgencyId && p.UserId == key.UserId select n.Revision).SingleOrDefaultAsync(ct);
            var result = await NoteAmendmentBilling.ReviewAsync(db, actor.ToAgencyActor(), noteId, key.UserId, revision, request, clock.UtcNow.UtcDateTime,
                v => audit.Record(actor, "note.amendment.financial-reviewed", "Note", noteId, JsonSerializer.Serialize(new { versionId = v })), ct);
            await transaction.CommitAsync(ct); return Results.Ok(result);
        }).AddEndpointFilter<NoteAmendmentConflictFilter>();
        api.MapGet("/note-amendments", async Task<IResult> (bool review, int afterNoteId, ClaimsPrincipal principal, ApiDbContext db, CancellationToken ct) =>
        {
            var actor = Actor.From(principal);
            var rows = await (from note in AmendmentScope(db, actor, review).AsNoTracking()
                join person in db.People.AsNoTracking() on note.PersonId equals person.Id
                where note.Id > afterNoteId && note.Status == NoteWorkflow.Approved && (!review ||
                    db.NoteAmendments.Any(a => a.NoteId == note.Id && a.Status == NoteAmendmentStatus.Submitted && a.AuthorId != actor.UserId))
                orderby note.Id select new { note.Id, note.PersonId, note.EventDate, person.FirstName, person.LastName })
                .Take(NoteAmendmentRules.PageSize + 1).ToListAsync(ct);
            var page = rows.Take(NoteAmendmentRules.PageSize).ToArray();
            var ids = page.Select(n => n.Id).ToArray();
            var amendments = await db.NoteAmendments.AsNoTracking().Where(a => ids.Contains(a.NoteId) && a.AgencyId == actor.AgencyId)
                .Select(a => new { a.NoteId, a.CurrentVersionId, a.Status }).ToListAsync(ct);
            var held = await NoteAmendmentBilling.HeldNoteIds(db).Where(id => ids.Contains(id)).ToListAsync(ct);
            return Results.Ok(new NoteAmendmentQueuePage(page.Select(n => new NoteAmendmentQueueItem(n.Id, n.PersonId,
                $"{n.FirstName} {n.LastName}".Trim(), n.EventDate,
                amendments.Where(a => a.NoteId == n.Id).OrderByDescending(a => a.CurrentVersionId).FirstOrDefault()?.Status.ToString() ?? "Approved original",
                held.Contains(n.Id))).ToArray(),
                rows.Count > NoteAmendmentRules.PageSize ? page[^1].Id : null));
        });
        api.MapGet("/notes/{noteId:int}/amendments", async Task<IResult> (int noteId, ClaimsPrincipal principal, ApiDbContext db, CancellationToken ct) =>
        {
            var actor = Actor.From(principal);
            var row = await LoadAmendmentSourceAsync(db, actor, noteId, ct);
            if (row is null) return Results.NotFound();
            return Results.Ok(await NoteAmendmentWorkflow.ReadAsync(db, actor.ToAgencyActor(), ContractMapper.ToNote(row.Note), row.Person.UserId,
                actor.HasSupervisorPermissions && row.Person.UserId != actor.UserId,
                await db.ClaimLines.AnyAsync(l => l.NoteId == noteId, ct), ct));
        });
        api.MapPost("/notes/{noteId:int}/amendments", async Task<IResult> (int noteId, NoteAmendmentRequest request,
            ClaimsPrincipal principal, ApiDbContext db, AuditTrail audit, ApiClock clock, CancellationToken ct) =>
        {
            var actor = Actor.From(principal);
            var key = await LoadAmendmentSourceAsync(db, actor, noteId, ct);
            if (key is null) return Results.NotFound();
            await using var transaction = await ServiceTimeWriteScope.BeginAsync(db, actor.AgencyId, key.Person.UserId, ct);
            db.ChangeTracker.Clear();
            if (!await TenantAccess.IsCurrentActorAsync(db, actor, ct)) return Results.Unauthorized();
            var row = await LoadAmendmentSourceAsync(db, actor, noteId, ct);
            if (row is null) return Results.NotFound();
            if (row.Person.UserId != key.Person.UserId) return AmendmentConflict();
            var result = await NoteAmendmentWorkflow.ExecuteAsync(db, actor.ToAgencyActor(), ContractMapper.ToNote(row.Note), row.Person.UserId,
                actor.HasSupervisorPermissions && row.Person.UserId != actor.UserId, request, clock.Today, clock.UtcNow.UtcDateTime,
                async (content, financialChange) =>
                {
                    if (request.Action == NoteAmendmentAction.Approve && financialChange)
                        await NoteAmendmentBilling.EnsureTransportResolvedAsync(db, actor.AgencyId,
                            from g in db.EdiGenerations join l in db.ClaimLines on g.BillingPeriodId equals l.BillingPeriodId where l.NoteId == noteId && g.AgencyId == actor.AgencyId select g.Id, ct);
                    if (content.EventDate?.Date != row.Note.EventDate?.Date && (row.Note.FormId is not null || row.Note.ReleaseObligationId is not null))
                        throw new NoteAmendmentWorkflowException(400, "note_amendment_linked_date", "Correct the exact form or release evidence through its existing correction workflow before changing this service date.");
                    if (ServiceTimeline.TryCreateBlock(noteId, content.StartTime, content.Minutes, "Approved") is not { } candidate) return;
                    var others = await (from n in db.Notes.AsNoTracking() join p in db.People.AsNoTracking() on n.PersonId equals p.Id
                        where n.AgencyId == actor.AgencyId && p.AgencyId == actor.AgencyId && p.UserId == row.Person.UserId &&
                            n.EventDate >= content.EventDate!.Value.Date && n.EventDate < content.EventDate.Value.Date.AddDays(1) && n.Id != noteId select new { n.Id, n.StartTime, n.Minutes, n.Status }).ToListAsync(ct);
                    if (ServiceTimeline.FindConflicts(candidate, others.Select(n => ServiceTimeline.TryCreateBlock(n.Id, n.StartTime, n.Minutes,
                        n.Status is int status ? NoteWorkflow.StatusName(status) : null)).OfType<ServiceBlock>()).Count > 0 || await NoteAmendmentSchedule.ConflictsAsync(db,
                        from n in db.Notes join p in db.People on n.PersonId equals p.Id where n.AgencyId == actor.AgencyId && p.AgencyId == actor.AgencyId && p.UserId == row.Person.UserId select n.Id,
                        actor.AgencyId, content.EventDate!.Value, candidate, ct))
                        throw new NoteAmendmentWorkflowException(409, "note_amendment_time_overlap", "The proposed service time overlaps another recorded service. No amendment was submitted or approved.");
                },
                (action, amendmentId, versionId) => audit.Record(actor, action, "Note", noteId, JsonSerializer.Serialize(new { amendmentId, versionId })), ct);
            await transaction.CommitAsync(ct);
            return Results.Ok(result);
        }).AddEndpointFilter<NoteAmendmentConflictFilter>();
    }
    private static async Task<ReviewableNote?> LoadAmendmentSourceAsync(ApiDbContext db, Actor actor, int id, CancellationToken ct) =>
        await (from n in AmendmentScope(db, actor, false).Concat(AmendmentScope(db, actor, true))
            join p in db.People on n.PersonId equals p.Id where n.Id == id select new ReviewableNote(n, p)).FirstOrDefaultAsync(ct);
    private static IResult AmendmentConflict() => Results.Conflict(new ApiErrorDto(NoteAmendmentRules.RevisionCode, "The note or amendment changed. Reload before retrying.", string.Empty));
}

internal sealed class NoteAmendmentConflictFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try { return await next(context); }
        catch (NoteAmendmentWorkflowException e)
        { return Results.Json(new ApiErrorDto(e.Code, e.Message, string.Empty), statusCode: e.StatusCode); }
        catch (ServiceTimeWriteConflictException e)
        { return Results.Conflict(new ApiErrorDto(NoteAmendmentRules.RevisionCode, e.Message, string.Empty)); }
        catch (Microsoft.Data.SqlClient.SqlException e) when (e.Number == 1205)
        { return Conflict(); }
        catch (DbUpdateException e) when (e is DbUpdateConcurrencyException ||
            e.InnerException is Microsoft.Data.SqlClient.SqlException sql && sql.Number is 1205 or 2601 or 2627)
        { return Conflict(); }
    }
    private static IResult Conflict() => Results.Conflict(new ApiErrorDto(NoteAmendmentRules.RevisionCode,
        "The amendment changed. Reload before retrying.", string.Empty));
}

internal sealed class NoteAmendmentDomainFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try { return await next(context); }
        catch (NoteAmendmentWorkflowException e) { return Results.Json(new ApiErrorDto(e.Code, e.Message, string.Empty), statusCode: e.StatusCode); }
    }
}



