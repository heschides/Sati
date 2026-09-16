using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sati.Api.Data;
using Sati.Api.Security;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Forms;

namespace Sati.Api.Endpoints;

internal static partial class ApiEndpoints
{
    private const string HousingReviewHeader = "X-Sati-Review-Items";

    private static void MapHousingSupportFunds(RouteGroupBuilder api)
    {
        api.MapPost("/people/{personId:int}/housing-support-funds.pdf", async Task<IResult> (
            int personId,
            HousingSupportFundsRequest request,
            ClaimsPrincipal principal,
            HttpContext httpContext,
            ApiDbContext db,
            HousingSupportFundsPdfGenerator generator,
            AuditTrail audit,
            CancellationToken cancellationToken) =>
        {
            var validation = HousingSupportFundsRules.Validate(request);
            if (validation.Count > 0)
                return Results.ValidationProblem(validation);

            var actor = Actor.From(principal);
            if (!await TenantAccess.OwnsPersonAsync(db, actor, personId, cancellationToken))
                return Results.NotFound();
            var person = await db.People.AsNoTracking().SingleOrDefaultAsync(
                candidate => candidate.Id == personId,
                cancellationToken);
            if (person is null)
                return Results.NotFound();
            var caseManager = await db.Users.AsNoTracking().SingleOrDefaultAsync(
                user => user.Id == person.UserId && user.AgencyId == actor.AgencyId,
                cancellationToken);
            var agency = await db.Agencies.AsNoTracking().SingleOrDefaultAsync(
                value => value.Id == actor.AgencyId,
                cancellationToken);

            var subject = new HousingSupportFundsSubject(
                person.Id,
                $"{person.FirstName} {person.LastName}".Trim(),
                ((global::Sati.WaiverType)person.Waiver).ToString(),
                person.HasSharedLiving,
                person.HasGuardian,
                person.GuardianName,
                caseManager?.DisplayName ?? "",
                agency?.Name ?? "",
                AgencyAddress(agency),
                caseManager?.Phone,
                caseManager?.Email);
            var generatedAtUtc = DateTime.UtcNow;
            var pdf = generator.Generate(subject, request, generatedAtUtc);
            var reviewItems = HousingSupportFundsRules.FindReviewItems(subject, request);
            var safeName = SafeFileName($"{person.LastName}-{person.FirstName}");
            var fileName = string.IsNullOrWhiteSpace(safeName)
                ? $"Housing-Support-Funds-Application-DRAFT-{personId}.pdf"
                : $"Housing-Support-Funds-Application-DRAFT-{personId}-{safeName}.pdf";
            var cycleStart = person.EffectiveDate is DateTime effective
                ? AnnualDocumentCycle.CurrentStart(effective, generatedAtUtc.ToLocalTime())
                : generatedAtUtc.ToLocalTime().Date;

            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await DocumentArtifactPersistence.StageGeneratedAsync(
                db, personId, actor.AgencyId, AnnualDocumentKind.HousingSupportFundsApplication,
                cycleStart, DocumentArtifactOrigin.Draft, generatedAtUtc, actor.UserId,
                pdf, fileName, reviewItems, cancellationToken,
                templateOwner: "Maine DHHS OADS",
                templateKey: "Housing-Support-Funds-Application",
                templateVersion: 20250630);
            audit.Record(actor, AuditActions.HousingSupportFundsGenerated, "Person", personId,
                JsonSerializer.Serialize(new
                {
                    sourceRevision = HousingSupportFundsRules.SourceRevision,
                    reviewItemCount = reviewItems.Count
                }));
            audit.Record(actor, AuditActions.DocumentGenerated, "Person", personId,
                JsonSerializer.Serialize(new
                {
                    kind = AnnualDocumentKind.HousingSupportFundsApplication.ToString(),
                    cycleStart = cycleStart.ToString("yyyy-MM-dd"),
                    origin = DocumentArtifactOrigin.Draft.ToString()
                }));
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            PreventSensitiveResponseCaching(httpContext);
            if (reviewItems.Count > 0)
                httpContext.Response.Headers[HousingReviewHeader] = string.Join("|", reviewItems);
            return Results.File(pdf, "application/pdf", fileName);
        });
    }

    private static string? AgencyAddress(ServerAgency? agency)
    {
        if (agency is null) return null;
        var locality = string.Join(" ", new[]
        {
            string.IsNullOrWhiteSpace(agency.City) ? null : $"{agency.City.Trim()},",
            agency.State?.Trim(), agency.Zip?.Trim()
        }.Where(value => !string.IsNullOrWhiteSpace(value)));
        var value = string.Join(" ", new[] { agency.Street?.Trim(), locality }
            .Where(part => !string.IsNullOrWhiteSpace(part)));
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
