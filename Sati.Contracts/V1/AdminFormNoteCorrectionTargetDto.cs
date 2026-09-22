namespace Sati.Contracts.V1;

/// <summary>Current source facts needed to review one administrator date correction.</summary>
public sealed record AdminFormNoteCorrectionTargetDto(
    int NoteId,
    int Revision,
    DateTime ActivityDate,
    string Status,
    int FormId,
    DateTime? CurrentCompletedOn,
    DateTime DueDate,
    bool HasClaimRecord);
