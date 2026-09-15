using Sati.Models;
using System;
using System.Collections.Generic;
using System.Text;
using Sati.Contracts.V1;

namespace Sati.Data
{
        public interface ISettingsService
        {
            Task<Settings> LoadAsync();
            Task SaveAsync(Settings settings);
            async Task<BillingComplianceRequirements>
                ResolveBillingComplianceRequirementsAsync(DateTime serviceDate)
            {
                // Test doubles and transitional implementations retain a safe
                // compatibility default. Both runtime implementations override
                // this and resolve the append-only policy for the exact date.
                var settings = await LoadAsync();
                return settings.BillingComplianceRequirements;
            }
            Task<IReadOnlyList<BillingCompliancePolicyVersionDto>>
                LoadBillingCompliancePolicyHistoryAsync() =>
                Task.FromResult<IReadOnlyList<BillingCompliancePolicyVersionDto>>([]);
            Task<BillingCompliancePolicyImpactPreviewDto>
                PreviewBillingCompliancePolicyAsync(
                    PreviewBillingCompliancePolicyRequest request) =>
                throw new NotSupportedException(
                    "Billing-compliance policy impact preview is not available through this settings service.");
            Task<IReadOnlyList<BillingCompliancePolicyReviewFlagDto>>
                LoadBillingCompliancePolicyReviewFlagsAsync() =>
                Task.FromResult<IReadOnlyList<BillingCompliancePolicyReviewFlagDto>>([]);
            Task<BillingCompliancePolicyVersionDto> AppendBillingCompliancePolicyAsync(
                AppendBillingCompliancePolicyRequest request) =>
                throw new NotSupportedException(
                    "Billing-compliance policy history is not available through this settings service.");
        }
}
