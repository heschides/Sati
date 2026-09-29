using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sati.Api.Data;
using Sati.Api.Infrastructure;
using Sati.Api.Security;
using Sati.Contracts.V1;
using Sati.Forms;

namespace Sati.Api.Endpoints;

internal static partial class ApiEndpoints
{
    private const string SafetyDeviceReviewHeader = "X-Sati-Review-Items";

    private static void MapSafetyDeviceRequests(RouteGroupBuilder api)
    {
        api.MapPost("/people/{personId:int}/safety-device-request.pdf", async Task<IResult> (
            int personId,
            SafetyDeviceRequest request,
            ClaimsPrincipal principal,
            HttpContext httpContext,
            ApiDbContext db,
            SafetyDevicePdfGenerator generator,
            AuditTrail audit,
            ApiClock clock,
            CancellationToken cancellationToken) =>
        {
            var generatedAtUtc = clock.UtcNow.UtcDateTime;
            var businessDate = clock.ToAgencyDate(generatedAtUtc);
            var today = DateOnly.FromDateTime(businessDate);
            var errors = SafetyDeviceRules.Validate(request, today);
            if (errors.Count > 0)
                return Results.ValidationProblem(errors);
            var actor = Actor.From(principal);
            if (!await TenantAccess.OwnsPersonAsync(db, actor, personId, cancellationToken))
                return Results.NotFound();
            var person = await db.People.AsNoTracking().SingleOrDefaultAsync(
                value => value.Id == personId, cancellationToken);
            if (person is null)
                return Results.NotFound();
            var caseManager = await db.Users.AsNoTracking().SingleOrDefaultAsync(
                user => user.Id == person.UserId && user.AgencyId == actor.AgencyId,
                cancellationToken);
            var subject = new SafetyDeviceSubject(person.Id,
                $"{person.FirstName} {person.LastName}".Trim(), person.BirthDate,
                person.EvergreenId, person.MaineCareId, person.Address,
                person.HasGuardian ? person.GuardianName : null,
                caseManager?.DisplayName, caseManager?.Email);
            var pdf = generator.Generate(subject, request, generatedAtUtc, today);
            var reviewItems = SafetyDeviceRules.FindReviewItems(subject, request);
            var safeName = SafeFileName($"{person.LastName}-{person.FirstName}");
            var fileName = string.IsNullOrWhiteSpace(safeName)
                ? $"Safety-Device-Request-DRAFT-{personId}.pdf"
                : $"Safety-Device-Request-DRAFT-{personId}-{safeName}.pdf";
            var cycleStart = person.EffectiveDate is DateTime effective
                ? AnnualDocumentCycle.CurrentStart(effective, businessDate)
                : businessDate;

            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await DocumentArtifactPersistence.StageGeneratedAsync(db, personId, actor.AgencyId,
                AnnualDocumentKind.SafetyDeviceRequest, cycleStart, DocumentArtifactOrigin.Draft,
                generatedAtUtc, actor.UserId, pdf, fileName, reviewItems, cancellationToken,
                templateOwner: "Maine DHHS OADS", templateKey: "Safety-Device-Request-Form",
                templateVersion: 202604);
            audit.Record(actor, AuditActions.SafetyDeviceRequestGenerated, "Person", personId,
                JsonSerializer.Serialize(new
                {
                    sourceRevision = SafetyDeviceRules.SourceRevision,
                    deviceCount = request.Devices?.Count(device => !string.IsNullOrWhiteSpace(device.NameAndType)) ?? 0,
                    reviewItemCount = reviewItems.Count
                }));
            audit.Record(actor, AuditActions.DocumentGenerated, "Person", personId,
                JsonSerializer.Serialize(new
                {
                    kind = AnnualDocumentKind.SafetyDeviceRequest.ToString(),
                    cycleStart = cycleStart.ToString("yyyy-MM-dd"),
                    origin = DocumentArtifactOrigin.Draft.ToString()
                }));
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            PreventSensitiveResponseCaching(httpContext);
            httpContext.Response.Headers[SafetyDeviceReviewHeader] = string.Join("|", reviewItems);
            return Results.File(pdf, "application/pdf", fileName);
        });
    }
}
