namespace Sati.Contracts.V1;

public sealed record AdminReleaseNoteCorrectionTargetDto(
    int NoteId,
    int Revision,
    long ReleaseObligationId,
    string Recipient,
    DateTime ActivityDate,
    DateTime? CurrentCompletedOn,
    DateTime DueDate,
    string Status,
    bool HasClaimRecord);

public sealed record AdminCorrectReleaseNoteDateRequest(
    int ExpectedRevision,
    DateTime CorrectedActivityDate,
    string Reason,
    bool AttestationConfirmed);
