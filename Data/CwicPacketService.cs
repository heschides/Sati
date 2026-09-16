using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Forms;
using Sati.Models;

namespace Sati.Data;

/// <summary>Local Production counterpart to the server-side CWIC packet route.</summary>
public sealed class CwicPacketService(
    IDbContextFactory<SatiContext> contextFactory,
    ISessionService session,
    LocalSsnStore ssnStore,
    CwicPacketPdfGenerator generator) : ICwicPacketService
{
    public async Task<CwicPacketResult> GenerateAsync(
        int personId,
        CwicPacketRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var validation = CwicPacketRules.Validate(request);
        if (validation.Count > 0)
            throw new ArgumentException(string.Join(" ", validation.SelectMany(entry => entry.Value)), nameof(request));

        var actor = session.CurrentUser
            ?? throw new InvalidOperationException("A CWIC packet cannot be prepared without a signed-in user.");
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await LocalTenantAccess.EnsureSessionAsync(context, session);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        if (!await LocalTenantAccess.OwnsPersonAsync(context, actor, personId, cancellationToken))
            throw new UnauthorizedAccessException("That consumer is not on your current caseload.");

        // Tracked because the encrypted SSN is held in shadow properties.
        var person = await context.People.SingleAsync(candidate =>
            candidate.Id == personId && candidate.UserId == actor.Id && candidate.AgencyId == actor.AgencyId,
            cancellationToken);
        string? ssn = null;
        if (LocalSsnStore.IsOnFile(context, person))
        {
            ssn = await ssnStore.RevealAsync(context, person, cancellationToken);
            LocalAuditTrail.Record(context, actor, LocalAuditActions.PersonSsnRevealed, "Person", personId);
        }

        var subject = new CwicPacketSubject(
            person.Id,
            $"{person.FirstName} {person.LastName}".Trim(),
            person.BirthDate,
            ssn);
        var generatedAtUtc = DateTime.UtcNow;
        var pdf = generator.Generate(subject, request, generatedAtUtc);
        var blankFields = CwicPacketRules.BlankFields(subject, request);
        var fileName = SuggestedFileName(personId, person.LastName, person.FirstName);
        var cycleStart = person.EffectiveDate is DateTime effective
            ? AnnualDocumentCycle.CurrentStart(effective, generatedAtUtc.ToLocalTime())
            : generatedAtUtc.ToLocalTime().Date;

        await DocumentArtifactStore.StageGeneratedAsync(
            context, personId, actor.AgencyId, AnnualDocumentKind.CwicReferralPacket,
            cycleStart, DocumentArtifactOrigin.Draft, generatedAtUtc, actor.Id,
            pdf, fileName, blankFields, cancellationToken,
            templateOwner: "MaineHealth",
            templateKey: "BCS-Referral-Packet",
            templateVersion: 202012);
        LocalAuditTrail.Record(context, actor, "cwic-packet.generated", "Person", personId,
            System.Text.Json.JsonSerializer.Serialize(new
            {
                sourceRevision = CwicPacketRules.SourceRevision,
                blankFieldCount = blankFields.Count
            }));
        LocalAuditTrail.Record(context, actor, LocalAuditActions.DocumentGenerated, "Person", personId,
            System.Text.Json.JsonSerializer.Serialize(new
            {
                kind = AnnualDocumentKind.CwicReferralPacket.ToString(),
                cycleStart = cycleStart.ToString("yyyy-MM-dd"),
                origin = DocumentArtifactOrigin.Draft.ToString()
            }));
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new CwicPacketResult(pdf, fileName, blankFields, CwicPacketRules.SourceRevision);
    }

    internal static string SuggestedFileName(int personId, string? lastName, string? firstName)
    {
        var name = new string($"{lastName}-{firstName}"
            .Select(character => char.IsLetterOrDigit(character) ? character : '-')
            .ToArray()).Trim('-');
        return string.IsNullOrEmpty(name)
            ? $"CWIC-Referral-Packet-DRAFT-{personId}.pdf"
            : $"CWIC-Referral-Packet-DRAFT-{personId}-{name}.pdf";
    }
}
