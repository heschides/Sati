namespace Sati.Contracts.V1;

/// <summary>Live source facts only; financial rendering still uses the frozen claim.</summary>
public sealed record BillingExportSource(
    int PersonId,
    int? Status,
    DateTime? ServiceDate,
    bool ComplianceOverride,
    string? OverrideReason,
    int? ApprovedById,
    DateTime? ApprovedAt,
    int? OverrideApprovedById,
    DateTime? OverrideApprovedAt,
    bool OverrideApproverInAgency);

/// <summary>
/// Release gate, not a financial correction mechanism. Releasing an old file is
/// still an export and must pass this gate without rewriting the retained bytes.
/// </summary>
public static class BillingExportGate
{
    public static IReadOnlyList<string> Evaluate(
        ProfessionalClaimSnapshot frozen,
        int agencyId,
        DateTime claimServiceDate,
        bool claimException,
        string? claimExceptionReason,
        BillingExportSource source,
        IReadOnlyList<ComplianceFormSnapshot> forms,
        DateTime today,
        BillingComplianceRequirements requirements)
    {
        var errors = new List<string>();
        if (frozen.AgencyId != agencyId || frozen.PersonId != source.PersonId)
            errors.Add("The frozen claim no longer matches its agency and consumer source.");
        if (source.Status != NoteWorkflow.Approved)
            errors.Add("The source service note is no longer approved.");
        if (source.ServiceDate is null || source.ServiceDate.Value.Date != claimServiceDate.Date)
            errors.Add("The source service date no longer matches the frozen claim.");
        if (!BillingComplianceGate.IsSupported(requirements))
            errors.Add("The agency billing compliance configuration is invalid.");

        if (claimException || source.ComplianceOverride)
        {
            // A later user-role change does not erase a historical approval. Its
            // retained identity must still belong to this agency, and the source
            // approval and frozen exception must agree; export cannot grant one.
            if (!claimException || !source.ComplianceOverride ||
                string.IsNullOrWhiteSpace(claimExceptionReason) ||
                !string.Equals(claimExceptionReason, source.OverrideReason, StringComparison.Ordinal) ||
                source.OverrideApprovedById is not > 0 || source.OverrideApprovedAt is null ||
                source.ApprovedById != source.OverrideApprovedById ||
                source.ApprovedAt != source.OverrideApprovedAt || !source.OverrideApproverInAgency)
                errors.Add("The frozen compliance exception does not match a complete stored supervisory approval.");
        }
        else
        {
            errors.AddRange(BillingComplianceGate.Evaluate(
                null, forms, today, requirements: requirements).Reasons);
            errors.AddRange(BillingComplianceGate.EvaluateBillingWindow(forms, claimServiceDate, requirements));
        }
        return errors.Distinct(StringComparer.Ordinal).ToArray();
    }
}
