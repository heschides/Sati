namespace Sati.Contracts.V1;

/// <summary>Non-secret account choice shown to billing staff; configuration remains API-owned.</summary>
public sealed record ClearinghouseAccountOptionDto(Guid Id, string Partner, string Label);

public sealed record ClearinghouseGenerationDto(
    long Id, int BillingPeriodId, string FileName, DateTime GeneratedAtUtc,
    bool IsCorrection, Guid? MatchingAccountId, string? DispatchState);

public sealed record ClearinghouseDispatchDto(
    Guid Id, long EdiGenerationId, Guid AccountId, string State, DateTime RequestedAtUtc,
    string? ExternalFileId, string? SafeErrorCode);

public sealed record ClearinghouseWorkspaceDto(
    bool Enabled, string Status,
    IReadOnlyList<ClearinghouseAccountOptionDto> Accounts,
    IReadOnlyList<ClearinghouseGenerationDto> Generations,
    IReadOnlyList<ClearinghouseDispatchDto> Dispatches);

public sealed record QueueClearinghouseDispatchRequest(long EdiGenerationId, Guid AccountId);
