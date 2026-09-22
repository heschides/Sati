namespace Sati.Contracts.V1;

/// <summary>
/// Corrects the source activity and completion date of one already-submitted form note.
/// The prior note revision prevents an administrator from overwriting a newer edit.
/// </summary>
public sealed record AdminCorrectFormNoteDateRequest(
    int ExpectedRevision,
    DateTime CorrectedActivityDate,
    string Reason,
    bool AttestationConfirmed);
