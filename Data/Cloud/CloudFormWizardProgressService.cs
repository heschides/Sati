using Sati.Contracts.V1;

namespace Sati.Data.Cloud;

public sealed class CloudFormWizardProgressService(CloudApiClient client)
    : IFormWizardProgressService
{
    private static string Path(int personId, string formKey) =>
        $"/api/v1/people/{personId}/form-wizards/{Uri.EscapeDataString(formKey)}/progress";

    public async Task<FormWizardProgressDto?> GetAsync(int personId, string formKey,
        CancellationToken cancellationToken = default)
    {
        var result = await client.GetAsync<FormWizardProgressDto>(
            Path(personId, formKey), cancellationToken);
        return result.Revision == 0 ? null : result;
    }

    public Task<FormWizardProgressDto> SaveAsync(int personId, string formKey,
        SaveFormWizardProgressRequest request, CancellationToken cancellationToken = default) =>
        client.PutAsync<SaveFormWizardProgressRequest, FormWizardProgressDto>(
            Path(personId, formKey), request, cancellationToken);

}
