using Sati.Models;


using Sati.Contracts.V1;

namespace Sati.Data
{
    public interface IFormService
    {
        Task UpdateFormAsync(Form form);
        Task AttestAsync(Form form, DateTime completedOn, int? evidenceNoteId = null);
        Task AttestAsync(
            Form form,
            DateTime completedOn,
            int? evidenceNoteId,
            bool confirmScheduledNoteConversion,
            string? scheduledNoteConversionToken = null) =>
            confirmScheduledNoteConversion
                ? throw new NotSupportedException("This data path cannot convert scheduled form work.")
                : AttestAsync(form, completedOn, evidenceNoteId);
        Task AttestAsync(
            Form form,
            DateTime completedOn,
            int? evidenceNoteId,
            string? supervisorOverrideReason) =>
            string.IsNullOrWhiteSpace(supervisorOverrideReason)
                ? AttestAsync(form, completedOn, evidenceNoteId)
                : throw new NotSupportedException("Form prerequisite overrides are no longer supported.");
        /// <summary>
        /// Records a Reclassification attestation and, when needed, the separate
        /// Comprehensive Assessment attestation it implies in one transaction.
        /// The dates are the actual occurrence dates, not the recording timestamp.
        /// </summary>
        Task AttestReclassificationAsync(
            Form form,
            DateTime reclassificationCompletedOn,
            DateTime? comprehensiveAssessmentCompletedOn,
            int? evidenceNoteId = null) =>
            comprehensiveAssessmentCompletedOn is null
                ? AttestAsync(form, reclassificationCompletedOn, evidenceNoteId)
                : throw new NotSupportedException(
                    "Atomic Reclassification and Comprehensive Assessment attestation is not available on this data path.");
        Task AttestReclassificationAsync(
            Form form,
            DateTime reclassificationCompletedOn,
            DateTime? comprehensiveAssessmentCompletedOn,
            int? evidenceNoteId,
            bool confirmScheduledNoteConversion,
            string? scheduledNoteConversionToken = null) =>
            confirmScheduledNoteConversion
                ? throw new NotSupportedException("This data path cannot convert scheduled form work.")
                : AttestReclassificationAsync(form, reclassificationCompletedOn,
                    comprehensiveAssessmentCompletedOn, evidenceNoteId);
        Task<FormPrerequisiteStatusDto> GetPrerequisiteStatusAsync(Form form) =>
            Task.FromResult(new FormPrerequisiteStatusDto(
                PrerequisiteKind.None.ToString(), true,
                "No additional prerequisite applies.", [], false));
        Task<IReadOnlyList<FormAttestationHistoryDto>> GetAttestationHistoryAsync(Form form) =>
            Task.FromResult<IReadOnlyList<FormAttestationHistoryDto>>([]);
        Task<DocumentArtifactDto> RecordExternalPrerequisiteAsync(Form form, string note) =>
            throw new NotSupportedException("External document recording is not available on this data path.");
        Task RevokeAttestationAsync(Form form, string reason);
        Task OpenFormAsync(Form form);
        Task OpenFormAsync(Form form, DateTime openedOn)
        {
            form.OpenedDate = openedOn.Date;
            return OpenFormAsync(form);
        }
        /// <summary>
        /// Compatibility boundary only: an authorized empty selection is a no-op;
        /// persisted forms cannot be deleted because their billing history must survive.
        /// Completion corrections use attestation/revocation instead.
        /// </summary>
        Task DeleteFormsAsync(IEnumerable<Form> forms);
    }

    public sealed class ScheduledFormNoteConversionRequiredException(
        string message,
        string confirmationToken)
        : InvalidOperationException(message)
    {
        public string ConfirmationToken { get; } = confirmationToken;
    }
}
