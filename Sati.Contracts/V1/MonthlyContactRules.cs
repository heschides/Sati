namespace Sati.Contracts.V1;

/// <summary>
/// One recorded contact with a consumer: a visit, phone call, or email that
/// actually took place. <paramref name="EvidenceId"/> names the note (for
/// example <c>note:42</c>) when it is persisted.
/// </summary>
public sealed record ContactFact(DateTime OccurredOn, string? EvidenceId = null);

/// <summary>Where a consumer stands against the monthly-contact requirement.</summary>
public sealed record MonthlyContactStatus(
    DateTime? LastContactOn,
    DateTime? NextContactDueOn,
    int? DaysSinceLastContact,
    bool IsOverdue);

/// <summary>
/// The monthly-contact requirement. Each contact starts a rolling 30-day clock,
/// and the plan's initial effective date starts the first one. Service dated after
/// the clock runs out is blocked until the next contact, and the day of that
/// contact is billable again, exactly like any other obligation in
/// <see cref="BillingComplianceGate"/>. A later contact never cures earlier
/// service. Whether the gate applies is the agency policy's
/// <see cref="BillingComplianceRequirements.MonthlyContact"/> choice.
/// </summary>
public static class MonthlyContactRules
{
    public const int IntervalDays = 30;
    public const string ObligationType = "MonthlyContact";
    public const string UnavailableObligationType = "MonthlyContact_Unavailable";

    /// <summary>
    /// Visit, Phone, and Email, plus the retired combined Contact type, which
    /// recorded phone and email contacts before they were split.
    /// </summary>
    public static bool IsContactType(string? noteType) => noteType is
        "Visit" or "Phone" or "Email" or "Contact";

    /// <summary>
    /// A note that records something that happened. Scheduled notes have not
    /// happened; Cancelled, Delayed, and Abandoned notes did not. A note with no
    /// status is legacy data whose meaning is unknown, so it does not count.
    /// </summary>
    public static bool HasOccurred(string? status) => status is
        "Pending" or "Logged" or "HeldForCompliance" or "Approved" or
        "Returned" or "ComplianceBlocked";

    public static ContactFact? ToFact(
        string? noteType,
        string? status,
        DateTime? eventDate,
        int? noteId) =>
        eventDate is DateTime occurred && IsContactType(noteType) && HasOccurred(status)
            ? new ContactFact(occurred.Date, EvidenceIdFor(noteId))
            : null;

    public static string? EvidenceIdFor(int? noteId) =>
        noteId is > 0 ? $"note:{noteId}" : null;

    /// <summary>
    /// Replaces the stored copy of a note being saved with its in-flight state, so a
    /// visit that is being logged counts toward its own service date, and a note being
    /// changed away from a contact stops counting.
    /// </summary>
    public static IReadOnlyList<ContactFact> WithCandidate(
        IEnumerable<ContactFact> stored,
        int? candidateNoteId,
        ContactFact? candidate)
    {
        ArgumentNullException.ThrowIfNull(stored);
        var evidenceId = EvidenceIdFor(candidateNoteId);
        var facts = stored
            .Where(fact => evidenceId is null ||
                           !string.Equals(fact.EvidenceId, evidenceId, StringComparison.Ordinal))
            .ToList();
        if (candidate is not null)
            facts.Add(candidate);
        return facts;
    }

    public static MonthlyContactStatus Status(
        DateTime? initialEffectiveDate,
        IEnumerable<ContactFact> contacts,
        DateTime asOf)
    {
        ArgumentNullException.ThrowIfNull(contacts);
        var date = asOf.Date;
        var last = contacts
            .Select(fact => fact.OccurredOn.Date)
            .Where(occurred => occurred <= date)
            .Select(occurred => (DateTime?)occurred)
            .Max();

        var anchor = Latest(last, initialEffectiveDate?.Date);
        if (anchor is null)
            return new MonthlyContactStatus(last, null, null, false);

        var dueOn = anchor.Value.AddDays(IntervalDays);
        return new MonthlyContactStatus(
            last,
            dueOn,
            last is null ? null : (date - last.Value).Days,
            date > dueOn);
    }

    /// <summary>
    /// The chain of contact obligations from the initial effective date: each is due
    /// 30 days after the previous contact (or the effective date) and is completed by
    /// the next contact after it. The last one is open. Contacts on or before the
    /// effective date do not move the first clock.
    /// </summary>
    /// <param name="contacts">
    /// The consumer's recorded contacts, or null when they were not loaded. Null
    /// yields one obligation that can never be completed, so an agency that requires
    /// monthly contact is blocked with a named reason instead of passing or failing
    /// silently on missing data.
    /// </param>
    public static IReadOnlyList<ComplianceFormSnapshot> BuildObligations(
        DateTime? initialEffectiveDate,
        IEnumerable<ContactFact>? contacts)
    {
        if (initialEffectiveDate is not DateTime effective)
            return [];

        if (contacts is null)
        {
            return
            [
                new ComplianceFormSnapshot(
                    UnavailableObligationType,
                    effective.Date,
                    CompletedDate: null,
                    ObligationId: "monthly-contact:unavailable")
            ];
        }

        var dates = contacts
            .Where(fact => fact.OccurredOn.Date > effective.Date)
            .GroupBy(fact => fact.OccurredOn.Date)
            .Select(group => (
                Date: group.Key,
                EvidenceId: group
                    .Select(fact => fact.EvidenceId)
                    .Where(id => id is not null)
                    .OrderBy(id => id, StringComparer.Ordinal)
                    .FirstOrDefault()))
            .OrderBy(item => item.Date)
            .ToArray();

        var obligations = new List<ComplianceFormSnapshot>(dates.Length + 1);
        var anchor = effective.Date;
        foreach (var contact in dates)
        {
            obligations.Add(Obligation(anchor, contact.Date, contact.EvidenceId));
            anchor = contact.Date;
        }
        obligations.Add(Obligation(anchor, completedOn: null, evidenceId: null));
        return obligations;
    }

    private static ComplianceFormSnapshot Obligation(
        DateTime anchor,
        DateTime? completedOn,
        string? evidenceId)
    {
        var dueOn = anchor.AddDays(IntervalDays);
        return new ComplianceFormSnapshot(
            ObligationType,
            dueOn,
            completedOn,
            ObligationId: $"monthly-contact:{dueOn:yyyy-MM-dd}",
            EvidenceId: evidenceId);
    }

    private static DateTime? Latest(DateTime? first, DateTime? second) =>
        first is null ? second
        : second is null ? first
        : first > second ? first : second;
}
