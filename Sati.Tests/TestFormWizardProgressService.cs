using Sati.Contracts.V1;
using Sati.Data;

namespace Sati.Tests;

internal sealed class TestFormWizardProgressService : IFormWizardProgressService
{
    private readonly Dictionary<(int PersonId, string Key), FormWizardProgressDto> rows = [];

    public Task<FormWizardProgressDto?> GetAsync(int personId, string formKey,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(rows.GetValueOrDefault((personId, formKey)));

    public Task<FormWizardProgressDto> SaveAsync(int personId, string formKey,
        SaveFormWizardProgressRequest request, CancellationToken cancellationToken = default)
    {
        var current = rows.GetValueOrDefault((personId, formKey));
        if ((current?.Revision ?? 0) != request.ExpectedRevision)
            throw new Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException("Stale draft.");
        var updated = new FormWizardProgressDto(formKey, request.ExpectedRevision + 1,
            request.StepIndex, request.AnswersJson, DateTime.UtcNow);
        rows[(personId, formKey)] = updated;
        return Task.FromResult(updated);
    }

}
