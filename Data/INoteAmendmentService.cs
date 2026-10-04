using Sati.Contracts.V1;

namespace Sati.Data;

public interface INoteAmendmentService
{
    Task<IReadOnlyList<NoteAmendmentFinancialItem>> GetFinancialQueueAsync(int afterNoteId = 0);
    Task<NoteAmendmentFinancialReviewDto> ReviewFinancialAsync(int noteId, NoteAmendmentFinancialReviewRequest request);
    Task<NoteAmendmentQueuePage> GetQueueAsync(bool review, int afterNoteId = 0);
    Task<NoteAmendmentWorkspaceDto> GetAsync(int noteId);
    Task<NoteAmendmentResultDto> ActAsync(int noteId, NoteAmendmentRequest request);
}
