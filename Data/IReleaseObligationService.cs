using Sati.Contracts.V1;

namespace Sati.Data;

/// <summary>
/// Release-compliance boundary used by the desktop. Demo calls the authoritative API; the
/// transitional local implementation applies the same contract rules to a short-lived context.
/// </summary>
public interface IReleaseObligationService
{
    Task<AdminReleaseNoteCorrectionTargetDto?> GetAdminCorrectionTargetAsync(
        int noteId, CancellationToken cancellationToken = default);

    Task CorrectNoteDateAsAdminAsync(
        int noteId, int expectedRevision, DateTime correctedActivityDate,
        string reason, bool attestationConfirmed,
        CancellationToken cancellationToken = default);
    Task<ReleaseObligationStatusDto> GetStatusAsync(
        int personId,
        DateTime targetEffectiveDate,
        CancellationToken cancellationToken = default);

    Task<ReleaseObligationStatusDto> ReconcileAsync(
        int personId,
        DateTime targetEffectiveDate,
        CancellationToken cancellationToken = default);

    Task<ReleaseObligationDto> AttestAsync(
        int personId,
        Guid obligationId,
        DateTime completedOn,
        string? reason = null,
        CancellationToken cancellationToken = default);

    Task<ReleaseObligationDto> RevokeAttestationAsync(
        int personId,
        Guid obligationId,
        string reason,
        CancellationToken cancellationToken = default);

    Task<ReleaseObligationDto> WithdrawAsync(
        int personId,
        Guid obligationId,
        DateTime withdrawnOn,
        string reason,
        CancellationToken cancellationToken = default);
}
