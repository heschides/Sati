using Sati.Contracts.V1;
using System.Security.Cryptography;
using System.Text.Json;

namespace Sati.Models;

/// <summary>Metadata proving that a document was prepared. PDF bytes are never stored here.</summary>
public sealed class DocumentArtifact
{
    public int Id { get; private set; }
    public int PersonId { get; private set; }
    public Person Person { get; private set; } = null!;
    public int AgencyId { get; private set; }
    public AnnualDocumentKind Kind { get; private set; }
    public DateTime CycleStart { get; private set; }
    public DocumentArtifactOrigin Origin { get; private set; }
    public DateTime GeneratedAtUtc { get; private set; }
    public int GeneratedByUserId { get; private set; }
    public string? ContentSha256 { get; private set; }
    public long? ByteCount { get; private set; }
    public string? SuggestedFileName { get; private set; }
    public string? TemplateOwner { get; private set; }
    public string? TemplateKey { get; private set; }
    public int? TemplateVersion { get; private set; }
    public int? SourceContentId { get; private set; }
    public int? SourceContentVersion { get; private set; }
    public string BlankFieldsJson { get; private set; } = "[]";
    public string? ExternalNote { get; private set; }
    public int? SupersededByArtifactId { get; private set; }
    public long? ReleaseObligationId { get; private set; }
    public Guid? OneOffReleaseId { get; private set; }
    public string? OneOffRecipientJson { get; private set; }
    public int? PromotedProviderId { get; private set; }

    private DocumentArtifact() { }

    public static DocumentArtifact Generated(
        int personId,
        int agencyId,
        AnnualDocumentKind kind,
        DateTime cycleStart,
        DocumentArtifactOrigin origin,
        DateTime generatedAtUtc,
        int generatedByUserId,
        byte[] content,
        string suggestedFileName,
        IReadOnlyCollection<string>? blankFields = null,
        string? templateOwner = null,
        string? templateKey = null,
        int? templateVersion = null,
        int? sourceContentId = null,
        int? sourceContentVersion = null,
        long? releaseObligationId = null,
        Guid? oneOffReleaseId = null,
        OneOffReleaseRecipientDto? oneOffRecipient = null)
    {
        if (origin == DocumentArtifactOrigin.RecordedAsExternal)
            throw new ArgumentException("Generated content cannot use the external origin.", nameof(origin));
        ArgumentNullException.ThrowIfNull(content);
        if (content.Length == 0)
            throw new ArgumentException("Generated document content cannot be empty.", nameof(content));
        if (releaseObligationId is not null && oneOffReleaseId is not null)
            throw new ArgumentException(
                "A release cannot be both one-off and linked to annual compliance.",
                nameof(oneOffReleaseId));
        if (oneOffReleaseId == Guid.Empty)
            throw new ArgumentException(
                "A one-off release identifier cannot be empty.", nameof(oneOffReleaseId));
        if (oneOffReleaseId is not null &&
            kind is not (AnnualDocumentKind.ReleaseAgency or AnnualDocumentKind.ReleaseMedical))
            throw new ArgumentException(
                "Only agency and medical releases can be one-off documents.",
                nameof(oneOffReleaseId));
        if ((oneOffReleaseId is null) != (oneOffRecipient is null))
            throw new ArgumentException(
                "A one-off release identifier and recipient snapshot must be recorded together.",
                nameof(oneOffRecipient));

        return new DocumentArtifact
        {
            PersonId = personId,
            AgencyId = agencyId,
            Kind = kind,
            CycleStart = cycleStart.Date,
            Origin = origin,
            GeneratedAtUtc = DateTime.SpecifyKind(generatedAtUtc, DateTimeKind.Utc),
            GeneratedByUserId = generatedByUserId,
            ContentSha256 = Convert.ToHexString(SHA256.HashData(content)),
            ByteCount = content.LongLength,
            SuggestedFileName = Normalize(suggestedFileName),
            TemplateOwner = Normalize(templateOwner),
            TemplateKey = Normalize(templateKey),
            TemplateVersion = templateVersion,
            SourceContentId = sourceContentId,
            SourceContentVersion = sourceContentVersion,
            ReleaseObligationId = releaseObligationId,
            OneOffReleaseId = oneOffReleaseId,
            OneOffRecipientJson = oneOffRecipient is null
                ? null
                : JsonSerializer.Serialize(oneOffRecipient),
            BlankFieldsJson = JsonSerializer.Serialize(
                (blankFields ?? []).Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value.Trim()).Distinct(StringComparer.Ordinal).Order().ToArray())
        };
    }

    public static DocumentArtifact External(
        int personId,
        int agencyId,
        AnnualDocumentKind kind,
        DateTime cycleStart,
        DateTime recordedAtUtc,
        int recordedByUserId,
        string note,
        long? releaseObligationId = null)
    {
        var noteError = AnnualDocumentRules.ValidateExternalNote(note);
        if (noteError is not null)
            throw new ArgumentException(noteError, nameof(note));
        return new DocumentArtifact
        {
            PersonId = personId,
            AgencyId = agencyId,
            Kind = kind,
            CycleStart = cycleStart.Date,
            Origin = DocumentArtifactOrigin.RecordedAsExternal,
            GeneratedAtUtc = DateTime.SpecifyKind(recordedAtUtc, DateTimeKind.Utc),
            GeneratedByUserId = recordedByUserId,
            ExternalNote = note.Trim(),
            ReleaseObligationId = releaseObligationId
        };
    }

    public void MarkSuperseded(int replacementArtifactId)
    {
        if (replacementArtifactId <= 0)
            throw new ArgumentOutOfRangeException(nameof(replacementArtifactId));
        if (SupersededByArtifactId is not null && SupersededByArtifactId != Id)
            throw new InvalidOperationException("This document artifact has already been superseded.");
        SupersededByArtifactId = replacementArtifactId;
    }

    public void MarkPromotedToProvider(int providerId)
    {
        if (providerId <= 0)
            throw new ArgumentOutOfRangeException(nameof(providerId));
        if (OneOffReleaseId is null)
            throw new InvalidOperationException("Only a one-off release can be added to the provider directory.");
        if (Origin != DocumentArtifactOrigin.GeneratedInSati || BlankFieldsJson != "[]")
            throw new InvalidOperationException(
                "Only a complete, prepared one-off release can be added to the provider directory.");
        if (PromotedProviderId is not null && PromotedProviderId != providerId)
            throw new InvalidOperationException(
                "This one-off release has already been added to the provider directory.");
        PromotedProviderId = providerId;
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
