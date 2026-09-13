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
    private const string CwicUnfilledHeader = "X-Sati-Unfilled-Fields";

    private static void MapCwicPackets(RouteGroupBuilder api)
    {
        api.MapPost("/people/{personId:int}/cwic-referral.pdf", async Task<IResult> (
            int personId,
            CwicPacketRequest request,
            ClaimsPrincipal principal,
            HttpContext httpContext,
            ApiDbContext db,
            EnvelopeProtector protector,
            CwicPacketPdfGenerator generator,
            AuditTrail audit,
            CancellationToken cancellationToken) =>
        {
            var validation = CwicPacketRules.Validate(request);
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

            string? ssn = null;
            if (person.SsnCiphertext is not null && person.SsnKeyId is not null)
            {
                ssn = await protector.UnprotectAsync(
                    new ProtectedValue(
                        person.SsnCiphertext,
                        person.SsnNonce!,
                        person.SsnTag!,
                        person.SsnWrappedKey!,
                        person.SsnKeyId),
                    new FieldBinding(actor.AgencyId, person.Id, "Ssn"),
                    cancellationToken);
                audit.Record(actor, AuditActions.PersonSsnDecrypted, "Person", personId);
            }

            var subject = new CwicPacketSubject(
                person.Id,
                $"{person.FirstName} {person.LastName}".Trim(),
                person.BirthDate,
                ssn);
            var generatedAtUtc = DateTime.UtcNow;
            var pdf = generator.Generate(subject, request, generatedAtUtc);
            var blankFields = CwicPacketRules.BlankFields(subject, request);
            var safeName = SafeFileName($"{person.LastName}-{person.FirstName}");
            var fileName = string.IsNullOrWhiteSpace(safeName)
                ? $"CWIC-Referral-Packet-DRAFT-{personId}.pdf"
                : $"CWIC-Referral-Packet-DRAFT-{personId}-{safeName}.pdf";
            var cycleStart = person.EffectiveDate is DateTime effective
                ? AnnualDocumentCycle.CurrentStart(effective, generatedAtUtc.ToLocalTime())
                : generatedAtUtc.ToLocalTime().Date;

            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await DocumentArtifactPersistence.StageGeneratedAsync(
                db, personId, actor.AgencyId, AnnualDocumentKind.CwicReferralPacket,
                cycleStart, DocumentArtifactOrigin.Draft, generatedAtUtc, actor.UserId,
                pdf, fileName, blankFields, cancellationToken,
                templateOwner: "MaineHealth",
                templateKey: "BCS-Referral-Packet",
                templateVersion: 202012);
            audit.Record(actor, AuditActions.CwicPacketGenerated, "Person", personId,
                JsonSerializer.Serialize(new
                {
                    sourceRevision = CwicPacketRules.SourceRevision,
                    blankFieldCount = blankFields.Count
                }));
            audit.Record(actor, AuditActions.DocumentGenerated, "Person", personId,
                JsonSerializer.Serialize(new
                {
                    kind = AnnualDocumentKind.CwicReferralPacket.ToString(),
                    cycleStart = cycleStart.ToString("yyyy-MM-dd"),
                    origin = DocumentArtifactOrigin.Draft.ToString()
                }));
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            PreventSensitiveResponseCaching(httpContext);
            if (blankFields.Count > 0)
                httpContext.Response.Headers[CwicUnfilledHeader] = string.Join("|", blankFields);
            return Results.File(pdf, "application/pdf", fileName);
        });
    }
}
