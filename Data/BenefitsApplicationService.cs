using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Forms;
using Sati.Models;

namespace Sati.Data;

/// <summary>Local Production counterpart to the API's benefits draft writer.</summary>
public sealed class BenefitsApplicationService(
    IDbContextFactory<SatiContext> contextFactory,
    ISessionService session,
    LocalSsnStore ssnStore,
    BenefitsApplicationPdfGenerator generator) : IBenefitsApplicationService
{
    public async Task<BenefitsApplicationResult> GenerateAsync(int personId,
        BenefitsApplicationRequest request, CancellationToken cancellationToken = default)
    {
        if (BenefitsApplicationRules.Validate(request).Count > 0)
            throw new ArgumentException("The application contains an invalid answer.", nameof(request));
        var actor = session.CurrentUser
            ?? throw new InvalidOperationException("Sign in before preparing an application.");
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await LocalTenantAccess.EnsureSessionAsync(context, session);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        if (!await LocalTenantAccess.OwnsPersonAsync(context, actor, personId, cancellationToken))
            throw new UnauthorizedAccessException("That consumer is not on your current caseload.");

        var person = await context.People.SingleAsync(candidate =>
            candidate.Id == personId && candidate.UserId == actor.Id && candidate.AgencyId == actor.AgencyId,
            cancellationToken);
        string? ssn = null;
        if (LocalSsnStore.IsOnFile(context, person))
        {
            ssn = await ssnStore.RevealAsync(context, person, cancellationToken);
            LocalAuditTrail.Record(context, actor, LocalAuditActions.PersonSsnRevealed, "Person", personId);
        }

        var now = DateTime.UtcNow;
        var pdf = generator.Generate(new BenefitsApplicationSubject(
            $"{person.FirstName} {person.LastName}".Trim(), person.BirthDate, ssn), request, now);
        var reviewItems = BenefitsApplicationRules.ReviewItems(request);
        var fileName = SuggestedFileName(personId);
        var cycleStart = person.EffectiveDate is DateTime effective
            ? AnnualDocumentCycle.CurrentStart(effective, now.ToLocalTime())
            : now.ToLocalTime().Date;

        await DocumentArtifactStore.StageGeneratedAsync(context, personId, actor.AgencyId,
            AnnualDocumentKind.BenefitsApplication, cycleStart, DocumentArtifactOrigin.Draft,
            now, actor.Id, pdf, fileName, reviewItems, cancellationToken,
            templateOwner: "Maine DHHS OFI", templateKey: "Application-for-Benefits",
            templateVersion: 20240430);
        LocalAuditTrail.Record(context, actor, "benefits-application.generated", "Person", personId,
            System.Text.Json.JsonSerializer.Serialize(new
            {
                sourceRevision = BenefitsApplicationRules.SourceRevision,
                answeredFieldCount = request.Answers.Count
            }));
        LocalAuditTrail.Record(context, actor, LocalAuditActions.DocumentGenerated, "Person", personId,
            System.Text.Json.JsonSerializer.Serialize(new
            {
                kind = AnnualDocumentKind.BenefitsApplication.ToString(),
                cycleStart = cycleStart.ToString("yyyy-MM-dd"),
                origin = DocumentArtifactOrigin.Draft.ToString()
            }));
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new BenefitsApplicationResult(pdf, fileName, reviewItems);
    }

    public static string SuggestedFileName(int personId) =>
        $"OFI-Application-for-Benefits-DRAFT-{personId}.pdf";
}
