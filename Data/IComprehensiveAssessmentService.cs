using Sati.Models.Assessments;
using Sati.Contracts.V1;

namespace Sati.Data;

public interface IComprehensiveAssessmentService
{
    Task<ComprehensiveAssessment?> GetLatestForAgendaAsync(int personId);
    Task<ComprehensiveAssessment> GetOrCreateDraftAsync(int personId, int authorUserId);
    Task SaveDocumentAsync(ComprehensiveAssessment assessment, AssessmentDocument document);
    Task SubmitForReviewAsync(ComprehensiveAssessment assessment);
    Task<ComprehensiveAssessmentDto> ReopenLegacyAsync(int assessmentId, int expectedRevision) => throw new NotSupportedException();
    Task<IReadOnlyList<AssessmentQueueItemDto>> GetReviewQueueAsync() => throw new NotSupportedException();
    Task<AssessmentReviewDetailsDto> GetReviewAsync(int assessmentId) => throw new NotSupportedException();
    Task<AssessmentReviewDetailsDto> ReviewAsync(int assessmentId, AssessmentReviewRequest request) => throw new NotSupportedException();
    Task<AssessmentPdfDto> GeneratePdfAsync(int assessmentId, int submissionId) => throw new NotSupportedException();
}
