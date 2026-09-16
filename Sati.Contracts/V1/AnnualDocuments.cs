namespace Sati.Contracts.V1;

public enum AnnualDocumentKind
{
    ReleaseAgency,
    ReleaseDhhs,
    ReleaseMedical,
    SafetyPlan,
    PrivacyPractices,
    MedicalRecordsRequest,
    CwicReferralPacket,
    HousingSupportFundsApplication,
    DhhsAuthorizedRepresentative
}

public enum DocumentArtifactOrigin
{
    GeneratedInSati,
    Draft,
    RecordedAsExternal
}

public sealed record AnnualDocumentCatalogEntry(
    AnnualDocumentKind Kind,
    string DisplayName,
    string? SatisfiesFormType,
    bool IncludedInAnnualPacket,
    bool CanRenderWithoutConsumerInput);

/// <summary>Single mapping owner for annual-document identity and compliance meaning.</summary>
public static class AnnualDocumentCatalog
{
    public static IReadOnlyList<AnnualDocumentCatalogEntry> All { get; } =
    [
        new(AnnualDocumentKind.ReleaseAgency, "Agency release", "Release_Agency", true, false),
        new(AnnualDocumentKind.ReleaseDhhs, "DHHS authorization to release", "Release_DHHS", true, false),
        new(AnnualDocumentKind.ReleaseMedical, "Medical release", "Release_Medical", true, false),
        new(AnnualDocumentKind.SafetyPlan, "Consumer safety plan", "SafetyPlan", true, false),
        new(AnnualDocumentKind.PrivacyPractices, "Notice of Privacy Practices", "PrivacyPractices", true, true),
        new(AnnualDocumentKind.MedicalRecordsRequest, "Medical records request", null, true, true),
        new(AnnualDocumentKind.CwicReferralPacket, "CWIC referral packet", null, false, false),
        new(AnnualDocumentKind.HousingSupportFundsApplication, "Housing Support Funds application", null, false, false),
        new(AnnualDocumentKind.DhhsAuthorizedRepresentative, "DHHS Authorized Representative", null, false, false)
    ];

    public static AnnualDocumentCatalogEntry? ForFormType(string formType) =>
        All.SingleOrDefault(entry => string.Equals(
            entry.SatisfiesFormType, formType, StringComparison.OrdinalIgnoreCase));

    public static AnnualDocumentCatalogEntry ForKind(AnnualDocumentKind kind) =>
        All.Single(entry => entry.Kind == kind);
}

public static class AnnualDocumentCycle
{
    // Advance the original enrollment date, not a February-28 anniversary produced by clamping.
    public static DateTime EndInclusive(DateTime effectiveDate, DateTime cycleStart) =>
        effectiveDate.AddYears(cycleStart.Year - effectiveDate.Year + 1).Date.AddDays(-1);

    public static DateTime CurrentStart(DateTime effectiveDate, DateTime onDate) =>
        ComplianceScheduleRules.CurrentTargetEffectiveDate(effectiveDate, onDate);

    /// <summary>
    /// Selects the annual target that can be worked on as of <paramref name="onDate"/>.
    /// Before enrollment, the first effective date remains the identity; an earlier,
    /// invented cycle is never returned. Once the next target's configured work window
    /// opens, that next target is selected even though the prior cycle is still in force.
    /// </summary>
    public static DateTime SuggestedStart(
        DateTime effectiveDate,
        DateTime onDate,
        int openDaysBefore,
        int dueDaysBeforeEffective = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(openDaysBefore);
        ArgumentOutOfRangeException.ThrowIfNegative(dueDaysBeforeEffective);

        var current = CurrentStart(effectiveDate, onDate);
        if (onDate.Date < effectiveDate.Date)
            return effectiveDate.Date;

        // Advance from the original effective date so a February-29 enrollment can
        // recover its leap-day anniversary instead of drifting permanently to Feb 28.
        var next = effectiveDate.AddYears(current.Year - effectiveDate.Year + 1).Date;
        return IsAvailable(next, onDate, openDaysBefore, dueDaysBeforeEffective)
            ? next
            : current;
    }

    public static bool IsAvailable(
        DateTime targetEffectiveDate,
        DateTime onDate,
        int openDaysBefore,
        int dueDaysBeforeEffective = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(openDaysBefore);
        ArgumentOutOfRangeException.ThrowIfNegative(dueDaysBeforeEffective);
        return onDate.Date >= targetEffectiveDate.Date
            .AddDays(-dueDaysBeforeEffective)
            .AddDays(-openDaysBefore);
    }
}

public sealed record DocumentArtifactDto(
    int Id,
    int PersonId,
    int AgencyId,
    string Kind,
    DateTime CycleStart,
    string Origin,
    DateTime GeneratedAtUtc,
    int GeneratedByUserId,
    string? ContentSha256,
    long? ByteCount,
    string? SuggestedFileName,
    IReadOnlyList<string> BlankFields,
    string? ExternalNote,
    string? TemplateOwner = null,
    string? TemplateKey = null,
    int? TemplateVersion = null,
    int? SourceContentId = null,
    int? SourceContentVersion = null,
    long? ReleaseObligationRecordId = null);

public sealed record RecordExternalDocumentRequest(
    DateTime CycleStart,
    string Note,
    Guid? ReleaseObligationId = null);

public static class AnnualDocumentRules
{
    public const int ExternalNoteMaxLength = 1_000;

    public static string? ValidateExternalNote(string? note)
    {
        if (string.IsNullOrWhiteSpace(note))
            return "A note is required for an external document.";
        return note.Trim().Length > ExternalNoteMaxLength
            ? $"The external-document note cannot exceed {ExternalNoteMaxLength} characters."
            : null;
    }
}

public sealed record RenderAnnualDocumentRequest(
    DateTime? CycleStart = null,
    AgencyReleaseRequest? Release = null,
    DhhsFormRequest? Dhhs = null,
    Guid? ReleaseObligationId = null);

public sealed record FormPrerequisiteStatusDto(
    string Kind,
    bool IsSatisfied,
    string Summary,
    IReadOnlyList<int> ArtifactIds,
    bool CanSupervisorOverride);
