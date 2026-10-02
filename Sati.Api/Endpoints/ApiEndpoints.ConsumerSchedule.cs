using System.Data;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sati.Api.Data;
using Sati.Api.Security;
using Sati.Contracts.V1;
using Sati.Models;

namespace Sati.Api.Endpoints;

internal static partial class ApiEndpoints
{
    private static void MapConsumerSchedule(RouteGroupBuilder api)
    {
        const string collection = "/people/{personId:int}/schedule";
        const string item = "/people/{personId:int}/schedule/{entryId:int}";

        api.MapGet(collection, async Task<IResult> (int personId, ClaimsPrincipal principal,
            ApiDbContext db, AuditTrail audit, HttpContext http, CancellationToken token) =>
        {
            var actor = Actor.From(principal);
            if (!await TenantAccess.OwnsPersonAsync(db, actor, personId, token))
                return Results.NotFound();
            var rows = await db.ConsumerScheduleEntries.AsNoTracking()
                .Where(x => x.PersonId == personId)
                .OrderBy(x => x.Kind).ThenBy(x => x.Date).ThenBy(x => x.EffectiveStart)
                .ThenBy(x => x.StartMinute).ThenBy(x => x.Id)
                .ToListAsync(token);
            audit.Record(actor, AuditActions.ConsumerScheduleViewed, "Person", personId);
            await db.SaveChangesAsync(token);
            PreventSensitiveResponseCaching(http);
            return Results.Ok(rows.Select(x => x.ToDto()).ToList());
        });

        api.MapPost(collection, async Task<IResult> (int personId,
            SaveConsumerScheduleEntryRequest request, ClaimsPrincipal principal,
            ApiDbContext db, AuditTrail audit, HttpContext http, CancellationToken token) =>
        {
            var errors = ConsumerScheduleRules.Validate(request);
            if (errors.Count != 0) return Results.ValidationProblem(errors);
            var actor = Actor.From(principal);
            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, token);
            if (!await TenantAccess.OwnsPersonAsync(db, actor, personId, token))
                return Results.NotFound();
            if (request.ExpectedRevision != 0) return ConsumerScheduleConflict();
            var row = new ConsumerScheduleEntry { PersonId = personId };
            row.Apply(request);
            db.ConsumerScheduleEntries.Add(row);
            await db.SaveChangesAsync(token);
            audit.Record(actor, AuditActions.ConsumerScheduleCreated, "Person", personId,
                JsonSerializer.Serialize(new { entryId = row.Id, row.Revision, row.Kind }));
            await db.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
            PreventSensitiveResponseCaching(http);
            return Results.Ok(row.ToDto());
        });

        api.MapPut(item, async Task<IResult> (int personId, int entryId,
            SaveConsumerScheduleEntryRequest request, ClaimsPrincipal principal,
            ApiDbContext db, AuditTrail audit, HttpContext http, CancellationToken token) =>
        {
            var errors = ConsumerScheduleRules.Validate(request);
            if (errors.Count != 0) return Results.ValidationProblem(errors);
            var actor = Actor.From(principal);
            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, token);
            if (!await TenantAccess.OwnsPersonAsync(db, actor, personId, token))
                return Results.NotFound();
            var row = await db.ConsumerScheduleEntries.SingleOrDefaultAsync(x =>
                x.Id == entryId && x.PersonId == personId, token);
            if (row is null) return Results.NotFound();
            if (row.Revision != request.ExpectedRevision) return ConsumerScheduleConflict();
            row.Apply(request);
            audit.Record(actor, AuditActions.ConsumerScheduleUpdated, "Person", personId,
                JsonSerializer.Serialize(new { entryId, row.Revision, row.Kind }));
            try { await db.SaveChangesAsync(token); }
            catch (DbUpdateConcurrencyException) { return ConsumerScheduleConflict(); }
            await transaction.CommitAsync(token);
            PreventSensitiveResponseCaching(http);
            return Results.Ok(row.ToDto());
        });

        api.MapDelete(item, async Task<IResult> (int personId, int entryId,
            int expectedRevision, ClaimsPrincipal principal, ApiDbContext db,
            AuditTrail audit, CancellationToken token) =>
        {
            var actor = Actor.From(principal);
            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, token);
            if (!await TenantAccess.OwnsPersonAsync(db, actor, personId, token))
                return Results.NotFound();
            var row = await db.ConsumerScheduleEntries.SingleOrDefaultAsync(x =>
                x.Id == entryId && x.PersonId == personId, token);
            if (row is null) return Results.NotFound();
            if (row.Revision != expectedRevision) return ConsumerScheduleConflict();
            db.ConsumerScheduleEntries.Remove(row);
            audit.Record(actor, AuditActions.ConsumerScheduleDeleted, "Person", personId,
                JsonSerializer.Serialize(new { entryId, row.Revision, row.Kind }));
            try { await db.SaveChangesAsync(token); }
            catch (DbUpdateConcurrencyException) { return ConsumerScheduleConflict(); }
            await transaction.CommitAsync(token);
            return Results.NoContent();
        });
    }

    private static IResult ConsumerScheduleConflict() => Results.Conflict(new ApiErrorDto(
        "consumer_schedule_conflict",
        "This schedule changed in another session. Reload it before continuing.", string.Empty));
}
