namespace Sati.Contracts.V1;

public enum CheckRequestWorkflowCheckpoint
{
    Submission,
    Decision,
    Release,
    Receipt
}

public enum CheckRequestWorkflowAction
{
    Submitted,
    Approved,
    Returned,
    Released,
    ReceiptAcknowledged
}

public enum CheckRequestWorkflowStatus
{
    Draft,
    Prepared,
    Submitted,
    Approved,
    Returned,
    Released,
    ReceiptAcknowledged
}

public static class CheckRequestWorkflowRules
{
    public const int NoteMaxLength = 1_000;

    public static CheckRequestWorkflowCheckpoint Checkpoint(CheckRequestWorkflowAction action) => action switch
    {
        CheckRequestWorkflowAction.Submitted => CheckRequestWorkflowCheckpoint.Submission,
        CheckRequestWorkflowAction.Approved or CheckRequestWorkflowAction.Returned => CheckRequestWorkflowCheckpoint.Decision,
        CheckRequestWorkflowAction.Released => CheckRequestWorkflowCheckpoint.Release,
        CheckRequestWorkflowAction.ReceiptAcknowledged => CheckRequestWorkflowCheckpoint.Receipt,
        _ => throw new ArgumentOutOfRangeException(nameof(action))
    };

    public static CheckRequestWorkflowStatus Resolve(
        bool pdfPrepared,
        IEnumerable<CheckRequestWorkflowAction> actions)
    {
        var set = actions.ToHashSet();
        if (set.Contains(CheckRequestWorkflowAction.ReceiptAcknowledged))
            return CheckRequestWorkflowStatus.ReceiptAcknowledged;
        if (set.Contains(CheckRequestWorkflowAction.Released))
            return CheckRequestWorkflowStatus.Released;
        if (set.Contains(CheckRequestWorkflowAction.Returned))
            return CheckRequestWorkflowStatus.Returned;
        if (set.Contains(CheckRequestWorkflowAction.Approved))
            return CheckRequestWorkflowStatus.Approved;
        if (set.Contains(CheckRequestWorkflowAction.Submitted))
            return CheckRequestWorkflowStatus.Submitted;
        return pdfPrepared ? CheckRequestWorkflowStatus.Prepared : CheckRequestWorkflowStatus.Draft;
    }

    public static bool CanApply(CheckRequestWorkflowStatus current, CheckRequestWorkflowAction action) => action switch
    {
        CheckRequestWorkflowAction.Submitted => current == CheckRequestWorkflowStatus.Prepared,
        CheckRequestWorkflowAction.Approved or CheckRequestWorkflowAction.Returned =>
            current == CheckRequestWorkflowStatus.Submitted,
        CheckRequestWorkflowAction.Released => current == CheckRequestWorkflowStatus.Approved,
        CheckRequestWorkflowAction.ReceiptAcknowledged => current == CheckRequestWorkflowStatus.Released,
        _ => false
    };

    public static string Describe(CheckRequestWorkflowStatus status) => status switch
    {
        CheckRequestWorkflowStatus.Draft => "Draft",
        CheckRequestWorkflowStatus.Prepared => "PDF prepared",
        CheckRequestWorkflowStatus.Submitted => "Pending supervisor review",
        CheckRequestWorkflowStatus.Approved => "Approved — ready for Finance",
        CheckRequestWorkflowStatus.Returned => "Returned by supervisor",
        CheckRequestWorkflowStatus.Released => "Check released",
        CheckRequestWorkflowStatus.ReceiptAcknowledged => "Receipt acknowledged",
        _ => status.ToString()
    };

    public static IReadOnlyList<string> ValidateNote(CheckRequestWorkflowAction action, string? note)
    {
        var errors = new List<string>();
        var normalized = note?.Trim();
        if (action == CheckRequestWorkflowAction.Returned && string.IsNullOrWhiteSpace(normalized))
            errors.Add("Enter a reason for returning the check request.");
        if (normalized?.Length > NoteMaxLength)
            errors.Add($"The workflow note must be {NoteMaxLength} characters or fewer.");
        return errors;
    }
}

public enum RepresentativePayeeLedgerEntryKind
{
    Deposit,
    Expense,
    CheckRelease,
    Correction
}

public static class RepresentativePayeeLedgerRules
{
    public const int DescriptionMaxLength = 500;

    public static IReadOnlyList<string> ValidateManualEntry(
        DateTime? entryDate,
        decimal amount,
        string? description)
    {
        var errors = new List<string>();
        if (entryDate is null) errors.Add("Choose the ledger date.");
        if (amount == 0) errors.Add("Enter a non-zero amount. Use a positive amount for money received and a negative amount for money spent.");
        if (Math.Abs(amount) > CheckRequestPublication.MaximumAmount)
            errors.Add("The ledger amount is too large to store.");
        if (string.IsNullOrWhiteSpace(description)) errors.Add("Enter a ledger description.");
        if (description?.Trim().Length > DescriptionMaxLength)
            errors.Add($"The ledger description must be {DescriptionMaxLength} characters or fewer.");
        return errors;
    }
}

public sealed record CheckRequestWorkflowEventDto(
    int Id,
    CheckRequestWorkflowAction Action,
    DateTime OccurredAtUtc,
    int ActorUserId,
    string ActorName,
    string? Note);

public sealed record CheckRequestWorkflowQueueItemDto(
    int CheckRequestId,
    int PersonId,
    string ConsumerName,
    string CaseManagerName,
    string SupervisorName,
    string? PayableTo,
    decimal Amount,
    DateTime? NeededByDate,
    string? Reason,
    CheckRequestWorkflowStatus Status,
    DateTime? LastActionAtUtc,
    string? LastActionByName,
    string? LastActionNote);

public sealed record RepresentativePayeeConsumerDto(
    int PersonId,
    string ConsumerName,
    decimal? MonthlyIncome);

public sealed record RepresentativePayeeLedgerEntryDto(
    long Id,
    int PersonId,
    int? CheckRequestId,
    DateTime EntryDate,
    RepresentativePayeeLedgerEntryKind Kind,
    decimal Amount,
    string Description,
    DateTime RecordedAtUtc,
    int RecordedByUserId,
    string RecordedByName);

public sealed record RepresentativePayeeWorkspaceDto(
    RepresentativePayeeConsumerDto Consumer,
    IReadOnlyList<RepresentativePayeeLedgerEntryDto> Entries,
    decimal Balance);

public sealed record ApplyCheckRequestWorkflowActionRequest(
    CheckRequestWorkflowAction Action,
    string? Note);

public sealed record AddRepresentativePayeeLedgerEntryRequest(
    int PersonId,
    DateTime? EntryDate,
    decimal Amount,
    string? Description);

public static class CheckRequestTemplateRules
{
    public const int MaximumNeededByDaysAfterRequest = 30;

    public static IReadOnlyList<string> Validate(
        DayOfWeek generateOn,
        int neededByDaysAfterRequest,
        string? payableTo,
        string? mailingAddress,
        decimal amount,
        string? reason)
    {
        var errors = new List<string>();
        if (!Enum.IsDefined(generateOn)) errors.Add("Choose a valid weekly generation day.");
        if (neededByDaysAfterRequest is < 0 or > MaximumNeededByDaysAfterRequest)
            errors.Add($"The needed-by offset must be between 0 and {MaximumNeededByDaysAfterRequest} days.");
        errors.AddRange(CheckRequestPublication.FindDraftErrors(
            DateTime.Today,
            payableTo,
            mailingAddress,
            amount,
            DateTime.Today.AddDays(Math.Clamp(neededByDaysAfterRequest, 0, MaximumNeededByDaysAfterRequest)),
            reason));
        return errors;
    }
}

public static class WeeklyCheckRequestSchedule
{
    public static DateTime? MostRecentOccurrence(
        DayOfWeek generateOn,
        DateTime effectiveFrom,
        DateTime throughDate)
    {
        if (!Enum.IsDefined(generateOn))
            throw new ArgumentOutOfRangeException(nameof(generateOn));
        var through = throughDate.Date;
        var effective = effectiveFrom.Date;
        var daysBack = ((int)through.DayOfWeek - (int)generateOn + 7) % 7;
        var occurrence = through.AddDays(-daysBack);
        return occurrence < effective ? null : occurrence;
    }
}

public sealed record CheckRequestTemplateDto(
    int Id,
    int PersonId,
    int Revision,
    bool IsEnabled,
    DayOfWeek GenerateOn,
    int NeededByDaysAfterRequest,
    string PayableTo,
    string MailingAddress,
    decimal Amount,
    string Reason,
    DateTime EffectiveFrom,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record SaveCheckRequestTemplateRequest(
    int ExpectedRevision,
    bool IsEnabled,
    DayOfWeek GenerateOn,
    int NeededByDaysAfterRequest,
    string? PayableTo,
    string? MailingAddress,
    decimal Amount,
    string? Reason);

public sealed record GeneratedCheckRequestDraftDto(
    int CheckRequestId,
    int TemplateId,
    int PersonId,
    string ConsumerName,
    DateTime ScheduledForDate,
    DateTime? NeededByDate,
    string? PayableTo,
    decimal Amount,
    CheckRequestWorkflowStatus Status);

public sealed record WeeklyCheckRequestDraftResultDto(
    int CreatedCount,
    IReadOnlyList<GeneratedCheckRequestDraftDto> PendingDrafts);

public sealed record TimeOffCheckRequestCollisionDto(
    int TemplateId,
    int PersonId,
    string ConsumerName,
    DateTime TimeOffDate,
    string PayableTo,
    decimal Amount,
    int? PendingCheckRequestId);

public sealed record EnsureTimeOffCheckRequestDraftsRequest(DateTime TimeOffDate);
