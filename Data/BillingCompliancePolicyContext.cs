using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Models;

namespace Sati.Data;

/// <summary>
/// Immutable facts needed to evaluate billing compliance for one agency. A
/// caller may reuse the loaded history for many notes, but must resolve the mask
/// again for each note's service date.
/// </summary>
internal sealed record BillingCompliancePolicyContext(
    int AgencyId,
    BillingComplianceRequirements FallbackRequirements,
    int PcpOpenDaysBefore,
    ComplianceScheduleSettings Schedule,
    IReadOnlyList<BillingCompliancePolicyVersionSnapshot> Versions)
{
    public BillingCompliancePolicyVersionSnapshot ResolveSnapshot(DateTime serviceDate) =>
        BillingCompliancePolicyRules.ResolveForServiceDate(
            Versions,
            AgencyId,
            serviceDate) ?? new BillingCompliancePolicyVersionSnapshot(
                long.MinValue, AgencyId, DateTime.MinValue, FallbackRequirements);

    public BillingComplianceRequirements Resolve(DateTime serviceDate) =>
        ResolveSnapshot(serviceDate).Requirements;

    public IReadOnlyList<BillingCompliancePolicyVersionSnapshot> RecoveryVersions =>
        [new BillingCompliancePolicyVersionSnapshot(
            long.MinValue, AgencyId, DateTime.MinValue, FallbackRequirements), .. Versions];

    public static BillingCompliancePolicyContext Default(int agencyId = 1) => new(
        agencyId,
        BillingComplianceGate.DefaultRequirements,
        90,
        new ComplianceScheduleSettings(),
        []);
}

internal static class BillingCompliancePolicyContextLoader
{
    public static async Task<BillingCompliancePolicyContext> LoadAsync(
        SatiContext context,
        int agencyId,
        CancellationToken cancellationToken = default)
    {
        var settings = await context.Settings.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.AgencyId == agencyId,
                cancellationToken);
        var versions = await context.BillingCompliancePolicyVersions.AsNoTracking()
            .Where(candidate => candidate.AgencyId == agencyId)
            .OrderBy(candidate => candidate.EffectiveOn)
            .ThenBy(candidate => candidate.Id)
            .ToListAsync(cancellationToken);
        return new BillingCompliancePolicyContext(
            agencyId,
            settings?.BillingComplianceRequirements ?? BillingComplianceGate.DefaultRequirements,
            settings?.PcpOpenDaysBefore ?? 90,
            settings is null
                ? new ComplianceScheduleSettings()
                : FormDueDateCalculator.ToSchedule(settings),
            versions.Select(candidate => candidate.ToSnapshot()).ToList());
    }
}
