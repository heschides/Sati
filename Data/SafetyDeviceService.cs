using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Forms;

namespace Sati.Data;

/// <summary>Local Production counterpart of the API's tenant-scoped draft generator.</summary>
public sealed class SafetyDeviceService(
    IDbContextFactory<SatiContext> contextFactory,
    ISessionService session,
    SafetyDevicePdfGenerator generator) : ISafetyDeviceService
{
    public async Task<SafetyDeviceResult> GenerateAsync(int personId, SafetyDeviceRequest request,
        CancellationToken cancellationToken = default)
    {
        var errors = SafetyDeviceRules.Validate(request);
        if (errors.Count > 0)
            throw new ArgumentException(string.Join(" ", errors.SelectMany(entry => entry.Value)), nameof(request));
        var actor = session.CurrentUser
            ?? throw new InvalidOperationException("Sign in before preparing a safety device request.");
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await LocalTenantAccess.EnsureSessionAsync(context, session);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        if (!await LocalTenantAccess.OwnsPersonAsync(context, actor, personId, cancellationToken))
            throw new UnauthorizedAccessException("That consumer is not on your current caseload.");

        var person = await context.People.AsNoTracking().Include(value => value.User)
            .SingleAsync(value => value.Id == personId && value.AgencyId == actor.AgencyId,
                cancellationToken);
        var subject = new SafetyDeviceSubject(person.Id, person.FullName, person.BirthDate,
            person.EvergreenId, person.MaineCareId, person.Address,
            person.HasGuardian ? person.GuardianName : null,
            person.User?.DisplayName ?? actor.DisplayName,
            person.User?.Email ?? actor.Email);
        var generatedAtUtc = DateTime.UtcNow;
        var pdf = generator.Generate(subject, request, generatedAtUtc);
        var reviewItems = SafetyDeviceRules.FindReviewItems(subject, request);
        var fileName = SuggestedFileName(personId, person.LastName, person.FirstName);
        var cycleStart = person.EffectiveDate is DateTime effective
            ? AnnualDocumentCycle.CurrentStart(effective, generatedAtUtc.ToLocalTime())
            : generatedAtUtc.ToLocalTime().Date;
        await DocumentArtifactStore.StageGeneratedAsync(context, personId, actor.AgencyId,
            AnnualDocumentKind.SafetyDeviceRequest, cycleStart, DocumentArtifactOrigin.Draft,
            generatedAtUtc, actor.Id, pdf, fileName, reviewItems, cancellationToken,
            templateOwner: "Maine DHHS OADS", templateKey: "Safety-Device-Request-Form",
            templateVersion: 202604);
        LocalAuditTrail.Record(context, actor, "safety-device-request.generated", "Person", personId,
            System.Text.Json.JsonSerializer.Serialize(new
            {
                sourceRevision = SafetyDeviceRules.SourceRevision,
                deviceCount = request.Devices?.Count(device => !string.IsNullOrWhiteSpace(device.NameAndType)) ?? 0,
                reviewItemCount = reviewItems.Count
            }));
        LocalAuditTrail.Record(context, actor, LocalAuditActions.DocumentGenerated, "Person", personId,
            System.Text.Json.JsonSerializer.Serialize(new
            {
                kind = AnnualDocumentKind.SafetyDeviceRequest.ToString(),
                cycleStart = cycleStart.ToString("yyyy-MM-dd"),
                origin = DocumentArtifactOrigin.Draft.ToString()
            }));
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new SafetyDeviceResult(pdf, fileName, reviewItems, SafetyDeviceRules.SourceRevision);
    }

    internal static string SuggestedFileName(int personId, string? lastName, string? firstName)
    {
        var safe = new string($"{lastName}-{firstName}"
            .Select(character => char.IsLetterOrDigit(character) ? character : '-')
            .ToArray()).Trim('-');
        return string.IsNullOrEmpty(safe)
            ? $"Safety-Device-Request-DRAFT-{personId}.pdf"
            : $"Safety-Device-Request-DRAFT-{personId}-{safe}.pdf";
    }
}
