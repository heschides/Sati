using Sati.Models;
using Sati.Models.Billing;
using Sati.Contracts.V1;

namespace Sati.Data.Billing
{
    public interface IBillingService
    {
        bool SupportsMockClearinghouse => false;
        bool SupportsResponseImport => false;
        Task<ClaimResponseIngestResultDto> ImportResponseAsync(
            AgencyActor actor, string document, CancellationToken cancellationToken = default) =>
            Task.FromException<ClaimResponseIngestResultDto>(new NotSupportedException(
                "Clearinghouse response import requires an API connection."));
        Task<BillingPeriod> GetOrCreateBillingPeriodAsync(AgencyActor actor, int userId, int month, int year);
        Task<IEnumerable<BillingPeriod>> GetBillingPeriodsAsync(AgencyActor actor, int userId);
        Task<IEnumerable<BillingPeriod>> GetAllBillingPeriodsAsync(AgencyActor actor);
        Task<ClaimLine> CreateClaimLineAsync(AgencyActor actor, int noteId, bool isComplianceException = false, string? complianceExceptionReason = null);
        Task<IEnumerable<ClaimLine>> GetUnbilledClaimLinesAsync(AgencyActor actor, int userId);
        Task SubmitBillingPeriodAsync(AgencyActor actor, int billingPeriodId);
        Task ReturnBillingPeriodToDraftAsync(AgencyActor actor, int billingPeriodId);
        Task<IEnumerable<Note>> GetApprovedUnbilledNotesAsync(AgencyActor actor);
        Task<BillingComplianceRecoveryPlan> PrepareComplianceRecoveryAsync(
            AgencyActor actor,
            int personId,
            CancellationToken cancellationToken = default) =>
            Task.FromException<BillingComplianceRecoveryPlan>(new NotSupportedException(
                "Billing compliance recovery is not available through this service."));
        Task<Sati.Contracts.V1.BillingComplianceRecoveryDecision> RecordComplianceRecoveryAsync(
            AgencyActor actor,
            int personId,
            CreateBillingComplianceRecoveryRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromException<Sati.Contracts.V1.BillingComplianceRecoveryDecision>(
                new NotSupportedException(
                    "Billing compliance recovery is not available through this service."));
        BillingValidationResult ValidateNoteForBilling(Note note);
        Task<BillingConfiguration> GetBillingConfigurationAsync(AgencyActor actor);
        Task SaveBillingConfigurationAsync(AgencyActor actor, BillingConfiguration configuration);
        Task<IReadOnlyList<BillingSubmissionHistoryDto>> GetSubmissionHistoryAsync(AgencyActor actor);
        Task<IReadOnlyList<BillingCompliancePolicyReviewFlagDto>>
            GetBillingCompliancePolicyReviewFlagsAsync(AgencyActor actor) =>
            Task.FromResult<IReadOnlyList<BillingCompliancePolicyReviewFlagDto>>([]);
        Task<MockClearinghouseResultDto> SubmitToMockClearinghouseAsync(
            AgencyActor actor,
            int billingPeriodId,
            MockClearinghouseScenario scenario) => Task.FromException<MockClearinghouseResultDto>(
                new NotSupportedException("The mock clearinghouse is available only in Demo."));
        Task<IReadOnlyList<RemittanceClaimOutcomeDto>> GetRemittanceOutcomesAsync(AgencyActor actor);
        Task<IReadOnlyList<RemittanceDepositDto>> GetRemittanceDepositsAsync(AgencyActor actor);

        // Recording a bank deposit and correcting a sent claim both depend on responses the
        // payer sent, which only the API ingests. The local path has no such records, so it
        // says so rather than pretending to have a half of the workflow.
        bool SupportsClaimCorrections => false;

        Task<IReadOnlyList<EftDepositRecordDto>> GetEftDepositRecordsAsync(AgencyActor actor, long depositId) =>
            Task.FromException<IReadOnlyList<EftDepositRecordDto>>(new NotSupportedException(
                "Recording a bank deposit requires an API connection."));

        Task<RemittanceDepositDto> RecordEftDepositAsync(
            AgencyActor actor, long depositId, RecordEftDepositRequest request) =>
            Task.FromException<RemittanceDepositDto>(new NotSupportedException(
                "Recording a bank deposit requires an API connection."));

        Task<IReadOnlyList<BillingClaimStatusDto>> GetBillingPeriodClaimsAsync(AgencyActor actor, int billingPeriodId) =>
            Task.FromException<IReadOnlyList<BillingClaimStatusDto>>(new NotSupportedException(
                "Claim corrections require an API connection."));

        Task<ClaimCorrectionDto> CreateClaimCorrectionAsync(
            AgencyActor actor, int billingPeriodId, CreateClaimCorrectionRequest request) =>
            Task.FromException<ClaimCorrectionDto>(new NotSupportedException(
                "Claim corrections require an API connection."));

        // Returns the saved correction file's path.
        Task<string> GenerateCorrectionEdiAsync(
            AgencyActor actor, int billingPeriodId, bool isTest, string idempotencyKey) =>
            Task.FromException<string>(new NotSupportedException(
                "Claim corrections require an API connection."));
    }
}
