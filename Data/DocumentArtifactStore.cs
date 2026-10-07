using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Models;
using System.Text.Json;

namespace Sati.Data;

internal static class DocumentArtifactStore
{
    public static async Task<DocumentArtifact> StageGeneratedAsync(
        SatiContext context,
        int personId,
        int agencyId,
        AnnualDocumentKind kind,
        DateTime cycleStart,
        DocumentArtifactOrigin origin,
        DateTime generatedAtUtc,
        int generatedByUserId,
        byte[] content,
        string suggestedFileName,
        IReadOnlyCollection<string>? blankFields,
        CancellationToken cancellationToken,
        string? templateOwner = null,
        string? templateKey = null,
        int? templateVersion = null,
        int? sourceContentId = null,
        int? sourceContentVersion = null,
        long? releaseObligationId = null,
        Guid? oneOffReleaseId = null,
        OneOffReleaseRecipientDto? oneOffRecipient = null)
    {
        var artifact = DocumentArtifact.Generated(
            personId, agencyId, kind, cycleStart, origin, generatedAtUtc,
            generatedByUserId, content, suggestedFileName, blankFields,
            templateOwner, templateKey, templateVersion, sourceContentId, sourceContentVersion,
            releaseObligationId, oneOffReleaseId, oneOffRecipient);
        return await StageReplacementAsync(context, artifact, personId, kind, cycleStart, cancellationToken);
    }

    public static async Task<DocumentArtifact> StageExternalAsync(
        SatiContext context,
        int personId,
        int agencyId,
        AnnualDocumentKind kind,
        DateTime cycleStart,
        DateTime recordedAtUtc,
        int recordedByUserId,
        string note,
        CancellationToken cancellationToken,
        long? releaseObligationId = null)
    {
        var artifact = DocumentArtifact.External(
            personId, agencyId, kind, cycleStart, recordedAtUtc, recordedByUserId, note,
            releaseObligationId);
        return await StageReplacementAsync(context, artifact, personId, kind, cycleStart, cancellationToken);
    }

    public static DocumentArtifactDto ToDto(DocumentArtifact artifact) => new(
        artifact.Id,
        artifact.PersonId,
        artifact.AgencyId,
        artifact.Kind.ToString(),
        artifact.CycleStart,
        artifact.Origin.ToString(),
        artifact.GeneratedAtUtc,
        artifact.GeneratedByUserId,
        artifact.ContentSha256,
        artifact.ByteCount,
        artifact.SuggestedFileName,
        JsonSerializer.Deserialize<string[]>(artifact.BlankFieldsJson) ?? [],
        artifact.ExternalNote,
        artifact.TemplateOwner,
        artifact.TemplateKey,
        artifact.TemplateVersion,
        artifact.SourceContentId,
        artifact.SourceContentVersion,
        artifact.ReleaseObligationId,
        artifact.OneOffReleaseId,
        string.IsNullOrWhiteSpace(artifact.OneOffRecipientJson)
            ? null
            : JsonSerializer.Deserialize<OneOffReleaseRecipientDto>(artifact.OneOffRecipientJson),
        artifact.PromotedProviderId);

    private static async Task<DocumentArtifact> StageReplacementAsync(
        SatiContext context,
        DocumentArtifact replacement,
        int personId,
        AnnualDocumentKind kind,
        DateTime cycleStart,
        CancellationToken cancellationToken)
    {
        var prior = await context.DocumentArtifacts.SingleOrDefaultAsync(candidate =>
            candidate.PersonId == personId && candidate.Kind == kind &&
            candidate.CycleStart == cycleStart.Date &&
            candidate.ReleaseObligationId == replacement.ReleaseObligationId &&
            candidate.OneOffReleaseId == replacement.OneOffReleaseId &&
            candidate.SupersededByArtifactId == null,
            cancellationToken);

        if (prior is not null)
        {
            if (kind == AnnualDocumentKind.ComprehensiveAssessment &&
                (replacement.SourceContentId < prior.SourceContentId ||
                 replacement.Origin == DocumentArtifactOrigin.Draft && prior.Origin == DocumentArtifactOrigin.GeneratedInSati))
            {
                // A PDF of earlier review evidence must not replace the operative document.
                replacement.MarkSuperseded(prior.Id);
                context.DocumentArtifacts.Add(replacement);
                await context.SaveChangesAsync(cancellationToken);
                return replacement;
            }
            // Release the filtered unique slot inside the caller's transaction. The
            // self-reference is replaced with the real successor id before commit.
            prior.MarkSuperseded(prior.Id);
            await context.SaveChangesAsync(cancellationToken);
            await SignaturePersistenceMutations.RevokeOpenForArtifactAsync(
                context, prior.Id, replacement.GeneratedByUserId, DateTime.UtcNow, cancellationToken);
        }

        context.DocumentArtifacts.Add(replacement);
        await context.SaveChangesAsync(cancellationToken);
        if (prior is not null)
            prior.MarkSuperseded(replacement.Id);
        return replacement;
    }
}
