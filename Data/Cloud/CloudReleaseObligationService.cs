using Sati.Contracts.V1;

namespace Sati.Data.Cloud;

/// <summary>Cloud client for the server-authoritative recipient-obligation workflow.</summary>
public sealed class CloudReleaseObligationService(CloudApiClient api) : IReleaseObligationService
{
    public async Task<AdminReleaseNoteCorrectionTargetDto?> GetAdminCorrectionTargetAsync(
        int noteId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await api.GetAsync<AdminReleaseNoteCorrectionTargetDto>(
                $"/api/v1/admin/notes/{noteId}/release-date-correction-target",
                cancellationToken);
        }
        catch (CloudApiException exception) when (
            exception.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task CorrectNoteDateAsAdminAsync(
        int noteId, int expectedRevision, DateTime correctedActivityDate,
        string reason, bool attestationConfirmed,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _ = await api.PostAsync<AdminCorrectReleaseNoteDateRequest, NoteDto>(
                $"/api/v1/admin/notes/{noteId}/correct-release-date",
                new AdminCorrectReleaseNoteDateRequest(expectedRevision,
                    correctedActivityDate.Date, reason, attestationConfirmed),
                cancellationToken);
        }
        catch (CloudApiException exception) when (exception.Code == "stale_note")
        {
            throw new NoteConcurrencyException(exception);
        }
    }
    public Task<ReleaseObligationStatusDto> GetStatusAsync(
        int personId,
        DateTime targetEffectiveDate,
        CancellationToken cancellationToken = default) =>
        api.GetAsync<ReleaseObligationStatusDto>(
            $"/api/v1/people/{personId}/release-obligations?targetEffectiveDate={targetEffectiveDate:yyyy-MM-dd}",
            cancellationToken);

    public Task<ReleaseObligationStatusDto> ReconcileAsync(
        int personId,
        DateTime targetEffectiveDate,
        CancellationToken cancellationToken = default) =>
        api.PostAsync<ReconcileReleaseObligationsRequest, ReleaseObligationStatusDto>(
            $"/api/v1/people/{personId}/release-obligations/reconcile",
            new ReconcileReleaseObligationsRequest(targetEffectiveDate.Date),
            cancellationToken);

    public Task<ReleaseObligationDto> AttestAsync(
        int personId,
        Guid obligationId,
        DateTime completedOn,
        string? reason = null,
        CancellationToken cancellationToken = default) =>
        api.PostAsync<AttestReleaseObligationRequest, ReleaseObligationDto>(
            $"/api/v1/people/{personId}/release-obligations/{obligationId:D}/attest",
            new AttestReleaseObligationRequest(completedOn.Date, reason),
            cancellationToken);

    public Task<ReleaseObligationDto> WithdrawAsync(
        int personId,
        Guid obligationId,
        DateTime withdrawnOn,
        string reason,
        CancellationToken cancellationToken = default) =>
        api.PostAsync<WithdrawReleaseAuthorizationRequest, ReleaseObligationDto>(
            $"/api/v1/people/{personId}/release-obligations/{obligationId:D}/withdraw",
            new WithdrawReleaseAuthorizationRequest(withdrawnOn.Date, reason),
            cancellationToken);

    public Task<ReleaseObligationDto> RevokeAttestationAsync(
        int personId,
        Guid obligationId,
        string reason,
        CancellationToken cancellationToken = default) =>
        api.PostAsync<RevokeReleaseAttestationRequest, ReleaseObligationDto>(
            $"/api/v1/people/{personId}/release-obligations/{obligationId:D}/attestation/revoke",
            new RevokeReleaseAttestationRequest(reason),
            cancellationToken);
}
