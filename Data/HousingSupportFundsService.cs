using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Forms;
using Sati.Models;

namespace Sati.Data;

/// <summary>Local Production counterpart to the server-side Housing Support Funds route.</summary>
public sealed class HousingSupportFundsService(
    IDbContextFactory<SatiContext> contextFactory,
    ISessionService session,
    HousingSupportFundsPdfGenerator generator) : IHousingSupportFundsService
{
    public async Task<HousingSupportFundsResult> GenerateAsync(
        int personId,
        HousingSupportFundsRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var validation = HousingSupportFundsRules.Validate(request);
        if (validation.Count > 0)
            throw new ArgumentException(string.Join(" ", validation.SelectMany(entry => entry.Value)), nameof(request));

        var actor = session.CurrentUser
            ?? throw new InvalidOperationException("A Housing Support Funds application cannot be prepared without a signed-in user.");
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await LocalTenantAccess.EnsureSessionAsync(context, session);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        if (!await LocalTenantAccess.OwnsPersonAsync(context, actor, personId, cancellationToken))
            throw new UnauthorizedAccessException("That consumer is not on your current caseload.");

        var person = await context.People.AsNoTracking()
            .Include(candidate => candidate.User)
            .Include(candidate => candidate.Agency)
            .SingleAsync(candidate => candidate.Id == personId && candidate.AgencyId == actor.AgencyId,
                cancellationToken);
        var subject = new HousingSupportFundsSubject(
            person.Id,
            person.FullName,
            person.Waiver.ToString(),
            person.HasSharedLiving,
            person.HasGuardian,
            person.GuardianName,
            person.User?.DisplayName ?? actor.DisplayName,
            person.Agency?.Name ?? actor.Agency?.Name ?? "",
            AddressOf(person.Agency ?? actor.Agency),
            person.User?.Phone ?? actor.Phone,
            person.User?.Email ?? actor.Email);
        var generatedAtUtc = DateTime.UtcNow;
        var pdf = generator.Generate(subject, request, generatedAtUtc);
        var reviewItems = HousingSupportFundsRules.FindReviewItems(subject, request);
        var fileName = SuggestedFileName(personId, person.LastName, person.FirstName);
        var cycleStart = person.EffectiveDate is DateTime effective
            ? AnnualDocumentCycle.CurrentStart(effective, generatedAtUtc.ToLocalTime())
            : generatedAtUtc.ToLocalTime().Date;

        await DocumentArtifactStore.StageGeneratedAsync(
            context, personId, actor.AgencyId, AnnualDocumentKind.HousingSupportFundsApplication,
            cycleStart, DocumentArtifactOrigin.Draft, generatedAtUtc, actor.Id,
            pdf, fileName, reviewItems, cancellationToken,
            templateOwner: "Maine DHHS OADS",
            templateKey: "Housing-Support-Funds-Application",
            templateVersion: 20250630);
        LocalAuditTrail.Record(context, actor, "housing-support-funds.generated", "Person", personId,
            System.Text.Json.JsonSerializer.Serialize(new
            {
                sourceRevision = HousingSupportFundsRules.SourceRevision,
                reviewItemCount = reviewItems.Count
            }));
        LocalAuditTrail.Record(context, actor, LocalAuditActions.DocumentGenerated, "Person", personId,
            System.Text.Json.JsonSerializer.Serialize(new
            {
                kind = AnnualDocumentKind.HousingSupportFundsApplication.ToString(),
                cycleStart = cycleStart.ToString("yyyy-MM-dd"),
                origin = DocumentArtifactOrigin.Draft.ToString()
            }));
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new HousingSupportFundsResult(
            pdf, fileName, reviewItems, HousingSupportFundsRules.SourceRevision);
    }

    internal static string SuggestedFileName(int personId, string? lastName, string? firstName)
    {
        var name = new string($"{lastName}-{firstName}"
            .Select(character => char.IsLetterOrDigit(character) ? character : '-')
            .ToArray()).Trim('-');
        return string.IsNullOrEmpty(name)
            ? $"Housing-Support-Funds-Application-DRAFT-{personId}.pdf"
            : $"Housing-Support-Funds-Application-DRAFT-{personId}-{name}.pdf";
    }

    internal static string? AddressOf(Agency? agency)
    {
        if (agency is null) return null;
        var locality = string.Join(" ", new[]
        {
            string.IsNullOrWhiteSpace(agency.City) ? null : $"{agency.City?.Trim()},",
            agency.State?.Trim(), agency.Zip?.Trim()
        }.Where(value => !string.IsNullOrWhiteSpace(value)));
        var parts = new[] { agency.Street?.Trim(), locality };
        var value = string.Join(" ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
