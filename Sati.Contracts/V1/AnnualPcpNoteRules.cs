namespace Sati.Contracts.V1;

public enum AnnualPcpProgressAction
{
    None,
    Open,
    Complete
}

public sealed record AnnualPcpNoteDecision(
    bool IsAnnual,
    bool IsBeforeAvailableWindow,
    bool IsAfterDueDate,
    AnnualPcpProgressAction RequiredAction,
    string Message)
{
    public bool MustBeUnbilled => IsAnnual && IsAfterDueDate;
}

/// <summary>
/// Owns the state transition requested by an Annual PCP note. The note's service
/// date is the asserted opening/completion date; callers never substitute today.
/// </summary>
public static class AnnualPcpNoteRules
{
    public const string FormTypeName = "PCP";

    public static bool IsAnnualSelection(
        bool isAnnualPlan,
        string? formType,
        int? formId) =>
        string.Equals(formType, FormTypeName, StringComparison.Ordinal) &&
        (isAnnualPlan || formId is > 0);

    public static AnnualPcpNoteDecision Evaluate(
        bool isAnnualPlan,
        string? formType,
        int? formId,
        string? status,
        DateTime? serviceDate,
        DateTime? availableOn,
        DateTime? dueOn,
        DateTime? openedOn,
        DateTime? completedOn)
    {
        var annual = IsAnnualSelection(isAnnualPlan, formType, formId);
        if (!annual)
            return new(false, false, false, AnnualPcpProgressAction.None, string.Empty);

        if (formId is not > 0 || serviceDate is not DateTime occurredOn ||
            availableOn is not DateTime available || dueOn is not DateTime due)
        {
            return new(true, false, false, AnnualPcpProgressAction.None,
                "An Annual PCP note requires an exact plan year and service date.");
        }

        occurredOn = occurredOn.Date;
        available = available.Date;
        due = due.Date;
        if (occurredOn < available)
        {
            return new(true, true, false, AnnualPcpProgressAction.None,
                $"The Annual PCP for this plan year does not open until {available:MMM d, yyyy}. " +
                "This note can be saved only as non-annual PCP revision work.");
        }

        var actionableStatus = status is "Pending" or "Logged";
        var action = !actionableStatus || completedOn is not null
            ? AnnualPcpProgressAction.None
            : openedOn is null
                ? AnnualPcpProgressAction.Open
                : AnnualPcpProgressAction.Complete;
        var late = occurredOn > due;
        var message = action switch
        {
            AnnualPcpProgressAction.Open =>
                $"Confirm that the Annual PCP was opened on {occurredOn:MMM d, yyyy}.",
            AnnualPcpProgressAction.Complete =>
                $"Confirm that the Annual PCP was completed on {occurredOn:MMM d, yyyy}.",
            _ => "The Annual PCP is already recorded complete."
        };
        if (late)
            message += $" The due date was {due:MMM d, yyyy}; this note will be marked Unbilled.";

        return new(true, false, late, action, message);
    }

    public static string? ValidateConfirmation(
        AnnualPcpNoteDecision decision,
        AnnualPcpProgressAction confirmedAction)
    {
        if (!decision.IsAnnual)
            return confirmedAction == AnnualPcpProgressAction.None
                ? null
                : "Only an Annual PCP note may advance an annual plan.";
        if (decision.IsBeforeAvailableWindow)
            return decision.Message;
        return decision.RequiredAction == confirmedAction
            ? null
            : decision.RequiredAction switch
            {
                AnnualPcpProgressAction.Open =>
                    "Confirm that this note opened the Annual PCP before saving.",
                AnnualPcpProgressAction.Complete =>
                    "Confirm that this note completed the Annual PCP before saving.",
                _ => "This Annual PCP does not have a pending workflow action."
            };
    }
}
