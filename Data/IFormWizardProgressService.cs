using Sati.Contracts.V1;

namespace Sati.Data;

public interface IFormWizardProgressService
{
    Task<FormWizardProgressDto?> GetAsync(int personId, string formKey,
        CancellationToken cancellationToken = default);
    Task<FormWizardProgressDto> SaveAsync(int personId, string formKey,
        SaveFormWizardProgressRequest request, CancellationToken cancellationToken = default);
}
