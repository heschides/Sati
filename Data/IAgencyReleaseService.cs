using Sati.Contracts.V1;

namespace Sati.Data;

/// <summary>
/// Generates the agency-owned release behind the same local/cloud seam as other
/// protected files. Implementations own authorization, identity derivation, and
/// the disclosure audit event.
/// </summary>
public interface IAgencyReleaseService
{
    Task<AgencyReleaseResult> GenerateAsync(
        int personId,
        AgencyReleaseRequest request,
        CancellationToken cancellationToken = default);

    Task<AgencyReleaseResult> GenerateMedicalAsync(
        int personId,
        AgencyReleaseRequest request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Medical-release generation is not available on this data path.");

    Task<AgencyReleaseResult> GenerateOneOffAsync(
        int personId,
        AgencyReleaseRequest request,
        Guid oneOffReleaseId,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("One-off release generation is not available on this data path.");

    Task<AgencyReleaseResult> GenerateOneOffMedicalAsync(
        int personId,
        AgencyReleaseRequest request,
        Guid oneOffReleaseId,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("One-off medical-release generation is not available on this data path.");

    Task<OneOffReleasePromotionDto> PromoteOneOffToProviderAsync(
        int personId,
        PromoteOneOffReleaseRequest request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("One-off provider promotion is not available on this data path.");

    /// <summary>
    /// Generates an agency release for one exact recipient-specific compliance obligation.
    /// The ordinary generation method remains available for releases that are not compliance
    /// documents (for example, an authorization for an unassigned family contact).
    /// </summary>
    Task<AgencyReleaseResult> GenerateForObligationAsync(
        int personId,
        AgencyReleaseRequest request,
        Guid releaseObligationId,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(
            "Recipient-specific agency-release generation is not available on this data path.");

    /// <summary>
    /// Generates a medical release for one exact recipient-specific compliance obligation.
    /// </summary>
    Task<AgencyReleaseResult> GenerateMedicalForObligationAsync(
        int personId,
        AgencyReleaseRequest request,
        Guid releaseObligationId,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(
            "Recipient-specific medical-release generation is not available on this data path.");
}
