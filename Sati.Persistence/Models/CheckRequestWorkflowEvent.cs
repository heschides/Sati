using Sati.Contracts.V1;

namespace Sati.Models;

/// <summary>
/// Append-only evidence of one check-request workflow checkpoint. The unique
/// (request, checkpoint) key prevents duplicate submission, decision, release, or receipt.
/// </summary>
public sealed class CheckRequestWorkflowEvent
{
    public long Id { get; set; }
    public int CheckRequestId { get; set; }
    public CheckRequest? CheckRequest { get; set; }
    public CheckRequestWorkflowCheckpoint Checkpoint { get; set; }
    public CheckRequestWorkflowAction Action { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public int ActorUserId { get; set; }
    public string ActorName { get; set; } = string.Empty;
    public string? Note { get; set; }
}
