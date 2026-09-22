using Sati.Contracts.V1;
using Sati.Models;
using System.Net;

namespace Sati.Data.Cloud;

public sealed class CloudAdminFormNoteCorrectionService(CloudApiClient api)
    : IAdminFormNoteCorrectionService
{
    public async Task<AdminFormNoteCorrectionTargetDto?> GetTargetAsync(
        int noteId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await api.GetAsync<AdminFormNoteCorrectionTargetDto>(
                $"/api/v1/admin/notes/{noteId}/form-date-correction-target",
                cancellationToken);
        }
        catch (CloudApiException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<Note> CorrectAsync(
        int noteId,
        int expectedRevision,
        DateTime correctedActivityDate,
        string reason,
        bool attestationConfirmed,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await api.PostAsync<AdminCorrectFormNoteDateRequest, NoteDto>(
                $"/api/v1/admin/notes/{noteId}/correct-form-date",
                new AdminCorrectFormNoteDateRequest(expectedRevision,
                    correctedActivityDate, reason, attestationConfirmed),
                cancellationToken);
            return CloudContractMapper.ToNote(result);
        }
        catch (CloudApiException exception) when (exception.Code == "stale_note")
        {
            throw new NoteConcurrencyException(exception);
        }
    }
}
