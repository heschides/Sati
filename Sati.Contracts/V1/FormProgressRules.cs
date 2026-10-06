namespace Sati.Contracts.V1;

/// <summary>Explicit choice and the state shown to the author, never a persisted note field.</summary>
public sealed record FormProgressRequest(
    AnnualPcpProgressAction Action,
    DateTime? OpenedOn,
    DateTime? CompletedOn,
    DateTime? ExpectedOpenedOn,
    DateTime? ExpectedCompletedOn,
    DateTime ExpectedDueOn,
    DateTime ExpectedTargetEffectiveDate);

public static class FormProgressRules
{
    public const string StaleStateMessage = "The document changed since it was displayed. Reload the note and review its current state.";
    public static bool Supports(string? type) => type is "PCP" or "ComprehensiveAssessment";

    public static string? Validate(
        FormProgressRequest request, string? type, string? status,
        DateTime availableOn, DateTime today, DateTime dueOn, DateTime target,
        DateTime? openedOn, DateTime? completedOn)
    {
        if (!Supports(type) || status is not ("Pending" or "Logged"))
            return "Document progress can be recorded only with a Pending or Logged PCP or Comprehensive Assessment note.";
        if (request.ExpectedOpenedOn?.Date != openedOn?.Date ||
            request.ExpectedCompletedOn?.Date != completedOn?.Date ||
            request.ExpectedDueOn.Date != dueOn.Date ||
            request.ExpectedTargetEffectiveDate.Date != target.Date)
            return StaleStateMessage;
        if (!Enum.IsDefined(request.Action))
            return "Choose a valid document action.";
        if (request.Action == AnnualPcpProgressAction.None)
            return request.OpenedOn is null && request.CompletedOn is null
                ? null : "Leaving the document unchanged cannot change its dates.";
        if (completedOn is not null)
            return "This document is already completed. Use the attestation correction workflow to change its dates.";
        if (request.Action == AnnualPcpProgressAction.Open && openedOn is not null)
            return "This document is already open.";
        var opening = openedOn ?? request.OpenedOn;
        if (opening is null)
            return "Enter the actual opening date before completing this document.";
        if (openedOn is not null && request.OpenedOn is not null && request.OpenedOn.Value.Date != openedOn.Value.Date)
            return "The existing opening date cannot be changed through a note save.";
        var openingError = FormOpeningRules.Validate(opening.Value, availableOn, today);
        if (openingError is not null) return openingError;
        if (request.Action == AnnualPcpProgressAction.Open)
            return request.CompletedOn is null ? null : "Opening a document cannot record completion.";
        if (request.CompletedOn is not DateTime completion)
            return "Enter the actual completion date.";
        if (completion.Date > today.Date)
            return "The completion date cannot be in the future.";
        if (completion.Date < opening.Value.Date)
            return "The opening date cannot follow the completion date.";
        return null;
    }
}
