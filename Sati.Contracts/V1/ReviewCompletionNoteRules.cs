namespace Sati.Contracts.V1;

public static class ReviewCompletionNoteRules
{
    public static bool IsReview(string? formType) =>
        formType is "Q1R" or "Q2R" or "Q3R" or "Q4R";

    public static bool IsOverdue(DateTime completedOn, DateTime dueOn) =>
        FormWorkBillingRules.CompletedAfterDeadline(completedOn, dueOn);
}
