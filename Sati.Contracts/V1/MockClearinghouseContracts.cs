namespace Sati.Contracts.V1;

/// <summary>
/// What the mock clearinghouse should pretend happened.
/// </summary>
/// <remarks>
/// The point of naming scenarios rather than always producing a happy path is that a
/// denial worklist cannot be exercised by a payer that never denies, and a deposit
/// reconciliation screen cannot be exercised by one whose totals always agree. Each of
/// these drives a state the read models already know how to display and that nothing has
/// ever actually produced.
///
/// Every scenario emits test interchanges only — ISA15 is <c>T</c> — so anything ingested
/// from them records itself as synthetic without depending on where it ran.
/// </remarks>
public enum MockClearinghouseScenario
{
    /// <summary>Syntax accepted, claims accepted, paid in full.</summary>
    Accepted,

    /// <summary>The 999 rejects the file. Nothing reaches the payer, so no 277CA and no 835.</summary>
    SyntaxRejected,

    /// <summary>Syntax accepted, every claim rejected by the payer. No 835 follows.</summary>
    ClaimsRejected,

    /// <summary>Syntax accepted, some claims accepted and some rejected.</summary>
    PartiallyAccepted,

    /// <summary>Accepted and paid below the billed amount, with a contractual adjustment.</summary>
    PartialPayment,

    /// <summary>Accepted, then denied on the remittance: the filing limit has passed (CO-29).</summary>
    Denied,

    /// <summary>Paid in full, with a provider-level adjustment so the deposit differs from the claim total.</summary>
    ProviderLevelAdjustment,

    /// <summary>A previously paid claim reversed.</summary>
    Reversal,

    // Appended, never inserted: the value takes part in response control numbers and may
    // cross the wire as a number, so existing members keep theirs.

    /// <summary>Denied as a duplicate of a claim already processed (CO-18).</summary>
    DeniedDuplicate,

    /// <summary>Denied because coverage had ended before the date of service (CO-27).</summary>
    DeniedCoverageEnded,

    /// <summary>Denied because the required authorization was absent (CO-197).</summary>
    DeniedNoAuthorization,

    /// <summary>Denied for missing or invalid claim information (CO-16).</summary>
    DeniedMissingInformation,

    /// <summary>Denied as a non-covered service (CO-96).</summary>
    DeniedNotCovered,

    /// <summary>Denied because the benefit maximum for the period was already reached (CO-119).</summary>
    DeniedBenefitMaximum,

    /// <summary>
    /// One remittance with every kind of claim outcome, in rotation: paid, partially paid
    /// (CO-45), denied for missing information (CO-16), denied as a duplicate (CO-18). The
    /// worklist's filters and the deposit total have to be right with all of them at once.
    /// </summary>
    MixedOutcomes
}

/// <param name="Scenario">What the mock should produce.</param>
public sealed record MockClearinghouseRequest(MockClearinghouseScenario Scenario);

/// <summary>
/// The documents the mock produced, returned alongside what ingesting them recorded.
/// </summary>
/// <param name="FunctionalAcknowledgement">The 999, always produced.</param>
/// <param name="ClaimAcknowledgement">The 277CA, absent when the 999 rejected the file.</param>
/// <param name="RemittanceAdvice">The 835, absent when nothing was accepted for payment.</param>
/// <param name="StagesRecorded">The transmitted stage and response stages written, in order.</param>
/// <param name="ClaimOutcomesRecorded">How many claim outcomes were written.</param>
/// <param name="DepositRecorded">Whether a deposit row was written.</param>
public sealed record MockClearinghouseResultDto(
    string Scenario,
    string FunctionalAcknowledgement,
    string? ClaimAcknowledgement,
    string? RemittanceAdvice,
    IReadOnlyList<string> StagesRecorded,
    int ClaimOutcomesRecorded,
    bool DepositRecorded);

/// <param name="Document">A 999, 277CA, or 835 to ingest.</param>
public sealed record ClaimResponseIngestRequest(string Document);

/// <param name="Kind">What the document was recognised as.</param>
/// <param name="IsSynthetic">Taken from the document's own ISA15 usage indicator.</param>
/// <param name="StageRecorded">The submission stage written, when one was.</param>
/// <param name="ClaimOutcomesRecorded">How many claim outcomes were written.</param>
/// <param name="DepositRecorded">Whether a deposit row was written.</param>
public sealed record ClaimResponseIngestResultDto(
    string Kind,
    bool IsSynthetic,
    string? StageRecorded,
    int ClaimOutcomesRecorded,
    bool DepositRecorded,
    string Explanation)
{
    public Guid ResponseId { get; init; }
    public bool AlreadyImported { get; init; }
    public DateTime ReceivedAtUtc { get; init; }
    public IReadOnlyList<int> BillingPeriodIds { get; init; } = [];
}
