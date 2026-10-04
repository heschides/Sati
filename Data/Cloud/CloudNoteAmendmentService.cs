using Sati.Contracts.V1;

namespace Sati.Data.Cloud;

public sealed class CloudNoteAmendmentService(CloudApiClient api) : INoteAmendmentService
{
    public Task<IReadOnlyList<NoteAmendmentFinancialItem>> GetFinancialQueueAsync(int afterNoteId = 0) =>
        api.GetAsync<IReadOnlyList<NoteAmendmentFinancialItem>>($"/api/v1/billing/note-amendments?afterNoteId={afterNoteId}");
    public Task<NoteAmendmentFinancialReviewDto> ReviewFinancialAsync(int noteId, NoteAmendmentFinancialReviewRequest request) =>
        api.PostAsync<NoteAmendmentFinancialReviewRequest, NoteAmendmentFinancialReviewDto>($"/api/v1/billing/notes/{noteId}/amendment-review", request);
    public Task<NoteAmendmentQueuePage> GetQueueAsync(bool review, int afterNoteId = 0) =>
        api.GetAsync<NoteAmendmentQueuePage>($"/api/v1/note-amendments?review={review.ToString().ToLowerInvariant()}&afterNoteId={afterNoteId}");
    public Task<NoteAmendmentWorkspaceDto> GetAsync(int noteId) => api.GetAsync<NoteAmendmentWorkspaceDto>($"/api/v1/notes/{noteId}/amendments");
    public Task<NoteAmendmentResultDto> ActAsync(int noteId, NoteAmendmentRequest request) =>
        api.PostAsync<NoteAmendmentRequest, NoteAmendmentResultDto>($"/api/v1/notes/{noteId}/amendments", request);
}
