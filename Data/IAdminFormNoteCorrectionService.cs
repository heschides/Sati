using Sati.Models;
using Sati.Contracts.V1;

namespace Sati.Data;

public interface IAdminFormNoteCorrectionService
{
    Task<AdminFormNoteCorrectionTargetDto?> GetTargetAsync(
        int noteId,
        CancellationToken cancellationToken = default);

    Task<Note> CorrectAsync(
        int noteId,
        int expectedRevision,
        DateTime correctedActivityDate,
        string reason,
        bool attestationConfirmed,
        CancellationToken cancellationToken = default);
}
