using Sati.Contracts.V1;

namespace Sati.Data.Cloud;

/// <summary>Demo and future cloud-Production generation through the authorized API.</summary>
public sealed class CloudAgencyReleaseService(CloudApiClient client) : IAgencyReleaseService
{
    public async Task<AgencyReleaseResult> GenerateAsync(
        int personId,
        AgencyReleaseRequest request,
        CancellationToken cancellationToken = default)
        => await GenerateCoreAsync(
            personId, request, AnnualDocumentKind.ReleaseAgency, null, cancellationToken);

    public async Task<AgencyReleaseResult> GenerateForObligationAsync(
        int personId,
        AgencyReleaseRequest request,
        Guid releaseObligationId,
        CancellationToken cancellationToken = default)
        => await GenerateCoreAsync(
            personId, request, AnnualDocumentKind.ReleaseAgency,
            RequiredObligationId(releaseObligationId), cancellationToken);

    public async Task<AgencyReleaseResult> GenerateMedicalAsync(
        int personId,
        AgencyReleaseRequest request,
        CancellationToken cancellationToken = default)
        => await GenerateCoreAsync(
            personId, request, AnnualDocumentKind.ReleaseMedical, null, cancellationToken);

    public async Task<AgencyReleaseResult> GenerateMedicalForObligationAsync(
        int personId,
        AgencyReleaseRequest request,
        Guid releaseObligationId,
        CancellationToken cancellationToken = default)
        => await GenerateCoreAsync(
            personId, request, AnnualDocumentKind.ReleaseMedical,
            RequiredObligationId(releaseObligationId), cancellationToken);

    private async Task<AgencyReleaseResult> GenerateCoreAsync(
        int personId,
        AgencyReleaseRequest request,
        AnnualDocumentKind kind,
        Guid? releaseObligationId,
        CancellationToken cancellationToken)
    {
        AgencyReleaseRules.EnsureValid(request);
        var pdf = await client.PostBytesAsync(
            $"/api/v1/people/{personId}/documents/{kind}",
            new RenderAnnualDocumentRequest(
                Release: request,
                ReleaseObligationId: releaseObligationId),
            cancellationToken);
        return new AgencyReleaseResult(
            pdf,
            AgencyReleaseService.SuggestedFileName(
                personId, null, null, request.IsRevocation, kind, request.IsDraft));
    }

    private static Guid RequiredObligationId(Guid value)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("A release obligation is required.", nameof(value));
        return value;
    }
}
