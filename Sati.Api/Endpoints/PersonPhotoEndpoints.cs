using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Sati.Api.Data;
using Sati.Api.Security;
using Sati.Contracts.V1;

namespace Sati.Api.Endpoints;

internal static partial class ApiEndpoints
{
    private static void MapPersonPhotos(RouteGroupBuilder api)
    {
        api.MapGet("/people/{personId:int}/photo", async Task<IResult> (
            int personId,
            ClaimsPrincipal principal,
            ApiDbContext db,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            var person = await db.People.AsNoTracking().SingleOrDefaultAsync(candidate =>
                candidate.Id == personId && candidate.AgencyId == actor.AgencyId,
                cancellationToken);
            if (person is null || !await TenantAccess.CanAccessPersonAsync(db, actor, person, cancellationToken))
                return Results.NotFound();

            PreventPhotoCaching(httpContext);
            var photo = await db.PersonPhotos.AsNoTracking()
                .Where(candidate => candidate.PersonId == personId && candidate.AgencyId == actor.AgencyId)
                .Select(candidate => new PersonPhotoDto(
                    candidate.ContentType,
                    candidate.Content,
                    candidate.PixelWidth,
                    candidate.PixelHeight,
                    candidate.Revision))
                .SingleOrDefaultAsync(cancellationToken);
            return Results.Ok(new PersonPhotoStateDto(photo));
        });

        api.MapPut("/people/{personId:int}/photo", async Task<IResult> (
            int personId,
            SavePersonPhotoRequest request,
            ClaimsPrincipal principal,
            ApiDbContext db,
            AuditTrail audit,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            await using var transaction = await db.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable,
                cancellationToken);
            if (!await TenantAccess.OwnsPersonAsync(db, actor, personId, cancellationToken))
                return Results.NotFound();

            var content = request.Content ?? [];
            var inspection = PersonPhotoRules.Inspect(content, request.ContentType, out var problem);
            if (inspection is null)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["photo"] = [problem ?? "Choose a valid JPG or PNG photo."]
                });
            }

            var stored = await db.PersonPhotos.SingleOrDefaultAsync(candidate =>
                candidate.PersonId == personId && candidate.AgencyId == actor.AgencyId,
                cancellationToken);
            if (stored is null)
            {
                if (request.ExpectedRevision is not null)
                    return StalePhoto(httpContext);
                stored = new ServerPersonPhoto
                {
                    PersonId = personId,
                    AgencyId = actor.AgencyId,
                    Revision = 1
                };
                db.PersonPhotos.Add(stored);
            }
            else
            {
                if (request.ExpectedRevision != stored.Revision)
                    return StalePhoto(httpContext);
                stored.Revision++;
            }

            stored.Content = content.ToArray();
            stored.ContentType = inspection.ContentType;
            stored.ContentSha256 = Convert.ToHexString(SHA256.HashData(content));
            stored.PixelWidth = inspection.PixelWidth;
            stored.PixelHeight = inspection.PixelHeight;
            stored.UpdatedAtUtc = DateTime.UtcNow;
            stored.UpdatedByUserId = actor.UserId;
            audit.Record(actor, AuditActions.PersonPhotoUpdated, "Person", personId);

            try
            {
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                return StalePhoto(httpContext);
            }

            PreventPhotoCaching(httpContext);
            return Results.Ok(ToDto(stored));
        });

        api.MapDelete("/people/{personId:int}/photo", async Task<IResult> (
            int personId,
            long expectedRevision,
            ClaimsPrincipal principal,
            ApiDbContext db,
            AuditTrail audit,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            await using var transaction = await db.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable,
                cancellationToken);
            if (!await TenantAccess.OwnsPersonAsync(db, actor, personId, cancellationToken))
                return Results.NotFound();

            var stored = await db.PersonPhotos.SingleOrDefaultAsync(candidate =>
                candidate.PersonId == personId && candidate.AgencyId == actor.AgencyId,
                cancellationToken);
            if (stored is null || stored.Revision != expectedRevision)
                return StalePhoto(httpContext);

            db.PersonPhotos.Remove(stored);
            audit.Record(actor, AuditActions.PersonPhotoRemoved, "Person", personId);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return StalePhoto(httpContext);
            }

            PreventPhotoCaching(httpContext);
            return Results.NoContent();
        });
    }

    private static PersonPhotoDto ToDto(ServerPersonPhoto photo) => new(
        photo.ContentType,
        photo.Content,
        photo.PixelWidth,
        photo.PixelHeight,
        photo.Revision);

    private static IResult StalePhoto(HttpContext context) => Results.Conflict(new ApiErrorDto(
        "stale_person_photo",
        "This photo changed after you opened the consumer. Select the consumer again before trying once more.",
        context.TraceIdentifier));

    private static void PreventPhotoCaching(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store, no-cache";
        context.Response.Headers.Pragma = "no-cache";
    }
}
