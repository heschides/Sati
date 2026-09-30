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
    private static void MapBenefitsApplications(RouteGroupBuilder api)
    {
        api.MapPost("/people/{personId:int}/benefits-application.pdf", async Task<IResult> (
            int personId,
            BenefitsApplicationRequest request,
            ClaimsPrincipal principal,
            HttpContext httpContext,
            ApiDbContext db,
            EnvelopeProtector protector,
            BenefitsApplicationPdfGenerator generator,
            AuditTrail audit,
            ApiClock clock,
            CancellationToken cancellationToken) =>
        {
            var validation = BenefitsApplicationRules.Validate(request);
            if (validation.Count > 0)
                return Results.ValidationProblem(validation);

            var actor = Actor.From(principal);
            if (!await TenantAccess.OwnsPersonAsync(db, actor, personId, cancellationToken))
                return Results.NotFound();
            var person = await db.People.AsNoTracking().SingleOrDefaultAsync(
                candidate => candidate.Id == personId, cancellationToken);
            if (person is null) return Results.NotFound();

            string? ssn = null;
            if (person.SsnCiphertext is not null && person.SsnKeyId is not null)
            {
                ssn = await protector.UnprotectAsync(new ProtectedValue(
                        person.SsnCiphertext, person.SsnNonce!, person.SsnTag!,
                        person.SsnWrappedKey!, person.SsnKeyId),
                    new FieldBinding(actor.AgencyId, person.Id, "Ssn"), cancellationToken);
                audit.Record(actor, AuditActions.PersonSsnDecrypted, "Person", personId);
            }

            var now = clock.UtcNow.UtcDateTime;
            var businessDate = clock.ToAgencyDate(now);
            byte[] pdf;
            try
            {
                pdf = generator.Generate(new BenefitsApplicationSubject(
                    $"{person.FirstName} {person.LastName}".Trim(), person.BirthDate, ssn),
                    request, now);
            }
            catch (ArgumentException)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["answers"] = ["An answer does not fit in its printed box. Shorten it and retry."]
                });
            }
            var reviewItems = BenefitsApplicationRules.ReviewItems(request);
            var fileName = $"OFI-Application-for-Benefits-DRAFT-{personId}.pdf";
            var cycleStart = person.EffectiveDate is DateTime effective
                ? AnnualDocumentCycle.CurrentStart(effective, businessDate)
                : businessDate;

            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await DocumentArtifactPersistence.StageGeneratedAsync(db, personId, actor.AgencyId,
                AnnualDocumentKind.BenefitsApplication, cycleStart, DocumentArtifactOrigin.Draft,
                now, actor.UserId, pdf, fileName, reviewItems, cancellationToken,
                templateOwner: "Maine DHHS OFI", templateKey: "Application-for-Benefits",
                templateVersion: 20240430);
            audit.Record(actor, AuditActions.BenefitsApplicationGenerated, "Person", personId,
                JsonSerializer.Serialize(new
                {
                    sourceRevision = BenefitsApplicationRules.SourceRevision,
                    answeredFieldCount = request.Answers.Count
                }));
            audit.Record(actor, AuditActions.DocumentGenerated, "Person", personId,
                JsonSerializer.Serialize(new
                {
                    kind = AnnualDocumentKind.BenefitsApplication.ToString(),
                    cycleStart = cycleStart.ToString("yyyy-MM-dd"),
                    origin = DocumentArtifactOrigin.Draft.ToString()
                }));
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            PreventSensitiveResponseCaching(httpContext);
            return Results.File(pdf, "application/pdf", fileName);
        });
    }
}
