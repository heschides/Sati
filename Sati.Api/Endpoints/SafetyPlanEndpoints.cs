using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Sati.Api.Data;
using Sati.Api.Infrastructure;
using Sati.Api.Security;
using Sati.Contracts.V1;

namespace Sati.Api.Endpoints;

internal static partial class ApiEndpoints
{
    private static void MapSafetyPlans(RouteGroupBuilder api)
    {
        api.MapGet("/people/{personId:int}/safety-plans/latest", async Task<IResult> (
            int personId, DateTime? cycleStart, ClaimsPrincipal principal, ApiDbContext db,
            ApiClock clock, CancellationToken ct) =>
        {
            var actor = Actor.From(principal);
            var person = await AccessibleSafetyPerson(db, actor, personId, ct);
            if (person is null) return Results.NotFound();
            if (person.EffectiveDate is not DateTime effective) return InvalidSafetyCycle();
            var timing = await GetSafetyTimingAsync(db, actor.AgencyId, ct);
            var requestedCycle = cycleStart ?? AnnualDocumentCycle.SuggestedStart(
                effective, clock.Today, timing.OpenDaysBefore, timing.DueDaysBeforeEffective);
            if (!TrySafetyCycle(effective, requestedCycle, out var cycle)) return InvalidSafetyCycle();
            var plan = await db.SafetyPlans.AsNoTracking().Where(x => x.PersonId == personId && x.CycleStart == cycle)
                .OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct);
            return Results.Json(plan is null ? null : ToSafetyPlan(plan));
        });

        api.MapPost("/people/{personId:int}/safety-plans/draft", async Task<IResult> (
            int personId, int authorUserId, DateTime cycleStart, ClaimsPrincipal principal,
            ApiDbContext db, AuditTrail audit, ApiClock clock, CancellationToken ct) =>
        {
            var actor = Actor.From(principal);
            if (authorUserId != actor.UserId || !await TenantAccess.OwnsPersonAsync(db, actor, personId, ct)) return Results.NotFound();
            var person = await db.People.AsNoTracking().SingleAsync(x => x.Id == personId, ct);
            if (!TrySafetyCycle(person.EffectiveDate, cycleStart, out var cycle)) return InvalidSafetyCycle();
            var timing = await GetSafetyTimingAsync(db, actor.AgencyId, ct);
            if (!AnnualDocumentCycle.IsAvailable(
                    cycle, clock.Today, timing.OpenDaysBefore, timing.DueDaysBeforeEffective))
                return SafetyCycleNotAvailable(cycle, timing);
            var prior = await db.SafetyPlans.AsNoTracking().Where(x => x.PersonId == personId && x.CycleStart == cycle)
                .OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct);
            if (prior?.Status is "Draft" or "ReadyForReview")
                return Results.Ok(ToSafetyPlan(prior));
            if (!SafetyPlanRules.CanStartRevision(prior?.Status)) return Results.Conflict();
            var now = DateTime.UtcNow;
            var plan = new ServerSafetyPlan { PersonId = personId, AuthorUserId = actor.UserId, CycleStart = cycle,
                Version = (prior?.Version ?? 0) + 1, CreatedAtUtc = now, UpdatedAtUtc = now,
                DocumentJson = prior?.DocumentJson ?? SafetyPlanRules.EmptyDocumentJson() };
            db.SafetyPlans.Add(plan);
            audit.Record(actor, AuditActions.SafetyPlanCreated, "Person", personId);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException error) when (IsSafetyVersionCollision(error)) { return SafetyConflict(); }
            return Results.Ok(ToSafetyPlan(plan));
        });

        api.MapPut("/safety-plans/{planId:int}/document", async Task<IResult> (
            int planId, SaveSafetyPlanDocumentRequest request, ClaimsPrincipal principal,
            ApiDbContext db, AuditTrail audit, CancellationToken ct) =>
        {
            var errors = SafetyPlanRules.Validate(request.DocumentJson, false);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            return await ChangeSafetyPlan(db, audit, Actor.From(principal), planId, "save",
                request.ExpectedRevision, request.DocumentJson, null, ct);
        });
        api.MapPost("/safety-plans/{planId:int}/submit", async Task<IResult> (
            int planId, int authorUserId, int expectedRevision, ClaimsPrincipal principal,
            ApiDbContext db, AuditTrail audit, CancellationToken ct) =>
        {
            var actor = Actor.From(principal);
            if (authorUserId != actor.UserId) return Results.NotFound();
            return await ChangeSafetyPlan(db, audit, actor, planId, "submit", expectedRevision, null, null, ct);
        });
        api.MapPost("/safety-plans/{planId:int}/approve", async Task<IResult> (
            int planId, ReviewSafetyPlanRequest request, ClaimsPrincipal principal,
            ApiDbContext db, AuditTrail audit, CancellationToken ct) =>
            await ChangeSafetyPlan(db, audit, Actor.From(principal), planId, "approve", request.ExpectedRevision, null, null, ct));
        api.MapPost("/safety-plans/{planId:int}/return", async Task<IResult> (
            int planId, ReviewSafetyPlanRequest request, ClaimsPrincipal principal,
            ApiDbContext db, AuditTrail audit, CancellationToken ct) =>
            await ChangeSafetyPlan(db, audit, Actor.From(principal), planId, "return", request.ExpectedRevision, null, request.ReturnReason, ct));
    }

    private static async Task<IResult> ChangeSafetyPlan(ApiDbContext db, AuditTrail audit, Actor actor,
        int id, string action, int revision, string? document, string? reason, CancellationToken ct)
    {
        var plan = await db.SafetyPlans.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (plan is null || await AccessibleSafetyPerson(db, actor, plan.PersonId, ct) is not { } person)
            return Results.NotFound();
        var review = action is "approve" or "return";
        if (review ? !SafetyPlanRules.CanReview(actor.UserId, actor.Permissions, plan.AuthorUserId)
            : !SafetyPlanRules.CanAuthor(actor.UserId, actor.Permissions, person.UserId) || plan.AuthorUserId != actor.UserId)
            return Results.NotFound();
        try
        {
            var next = SafetyPlanRules.Change(ToSafetyPlan(plan), action, revision, actor.UserId,
                actor.Permissions, DateTime.UtcNow, document, reason);
            plan.Status = next.Status; plan.Revision = next.Revision; plan.DocumentJson = next.DocumentJson;
            plan.UpdatedAtUtc = next.UpdatedAtUtc; plan.SubmittedAtUtc = next.SubmittedAtUtc;
            plan.ApprovedAtUtc = next.ApprovedAtUtc; plan.ApprovedByUserId = next.ApprovedByUserId;
            plan.ReturnReason = next.ReturnReason;
            var auditAction = action switch { "save" => AuditActions.SafetyPlanUpdated,
                "submit" => AuditActions.SafetyPlanSubmitted, "approve" => AuditActions.SafetyPlanApproved,
                _ => AuditActions.SafetyPlanReturned };
            audit.Record(actor, auditAction, "SafetyPlan", plan.Id);
            await db.SaveChangesAsync(ct);
            return Results.Ok(next);
        }
        catch (DbUpdateConcurrencyException) { return SafetyConflict(); }
        catch (SafetyPlanWorkflowException error)
        {
            return error.Code == "safety_plan_invalid"
                ? Results.ValidationProblem(new Dictionary<string, string[]> { ["document"] = [error.Message] })
                : Results.Conflict(new ApiErrorDto(error.Code, error.Message, string.Empty));
        }
    }

    private static async Task<ServerPerson?> AccessibleSafetyPerson(ApiDbContext db, Actor actor, int id, CancellationToken ct)
    {
        var person = await db.People.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.AgencyId == actor.AgencyId, ct);
        return person is not null && await TenantAccess.CanAccessUserAsync(db, actor, person.UserId, ct) ? person : null;
    }

    private static bool TrySafetyCycle(DateTime? effective, DateTime? requested, out DateTime cycle)
    {
        cycle = default;
        if (effective is null || effective.Value.Year is < 2 or > 9997) return false;
        if (requested is null) return false;
        cycle = requested.Value.Date;
        return cycle.Year is > 1 and < 9998 && cycle >= effective.Value.Date &&
            AnnualDocumentCycle.CurrentStart(effective.Value, cycle) == cycle;
    }
    private static async Task<SafetyCycleTiming> GetSafetyTimingAsync(
        ApiDbContext db, int agencyId, CancellationToken ct)
    {
        var timing = await db.Settings.AsNoTracking()
            .Where(x => x.AgencyId == agencyId)
            .Select(x => new SafetyCycleTiming(
                x.SafetyPlanOpenDaysBefore,
                x.SafetyPlanDaysBeforeAnniversary))
            .SingleOrDefaultAsync(ct);
        return timing is null
            ? new SafetyCycleTiming(90, 0)
            : new SafetyCycleTiming(
                Math.Max(0, timing.OpenDaysBefore),
                Math.Max(0, timing.DueDaysBeforeEffective));
    }
    private static IResult SafetyCycleNotAvailable(DateTime cycle, SafetyCycleTiming timing)
    {
        var availableOn = cycle.Date
            .AddDays(-timing.DueDaysBeforeEffective)
            .AddDays(-timing.OpenDaysBefore);
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["cycleStart"] =
                [$"This safety plan becomes available on {availableOn:yyyy-MM-dd}."]
        }, statusCode: StatusCodes.Status422UnprocessableEntity);
    }
    private sealed record SafetyCycleTiming(int OpenDaysBefore, int DueDaysBeforeEffective);
    private static IResult InvalidSafetyCycle() => Results.ValidationProblem(
        new Dictionary<string, string[]> { ["cycleStart"] = ["Choose an effective-date anniversary on or after enrollment."] });
    private static IResult SafetyConflict() => Results.Conflict(new ApiErrorDto(
        "safety_plan_stale", "This plan changed. Reload before continuing.", string.Empty));
    private static bool IsSafetyVersionCollision(DbUpdateException error) =>
        error.InnerException?.Message.Contains("IX_SafetyPlans_PersonId_CycleStart_Version", StringComparison.Ordinal) == true ||
        error.InnerException?.Message.Contains("SafetyPlans.PersonId, SafetyPlans.CycleStart, SafetyPlans.Version", StringComparison.Ordinal) == true;
    private static SafetyPlanDto ToSafetyPlan(ServerSafetyPlan plan) => new(plan.Id, plan.PersonId,
        plan.AuthorUserId, plan.CycleStart, plan.Status, plan.Version, plan.Revision, plan.CreatedAtUtc,
        plan.UpdatedAtUtc, plan.SubmittedAtUtc, plan.ApprovedAtUtc, plan.ApprovedByUserId, plan.ReturnReason, plan.DocumentJson);
}
