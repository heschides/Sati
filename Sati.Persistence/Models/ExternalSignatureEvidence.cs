namespace Sati.Models;

/// <summary>
/// Immutable evidence for a signed PDF returned through a workflow outside Sati.
/// The PDF bytes live in private write-once object storage; this row binds their
/// fingerprint to the exact generated artifact, obligation, signer assertion, and
/// authenticated staff attestation.
/// </summary>
public sealed class ExternalSignatureEvidence
{
    public int Id { get; set; }
    public Guid ClientRequestId { get; set; }
    public int AgencyId { get; set; }
    public int PersonId { get; set; }
    public int DocumentArtifactId { get; set; }
    public long ReleaseObligationId { get; set; }
    public string Method { get; set; } = string.Empty;
    public DateTime SignedOn { get; set; }
    public string SignerName { get; set; } = string.Empty;
    public string SignerCapacity { get; set; } = string.Empty;
    public int AttestedByUserId { get; set; }
    public DateTime AttestedAtUtc { get; set; }
    public string AttestationText { get; set; } = string.Empty;
    public string BlobPath { get; set; } = string.Empty;
    public string ContentSha256 { get; set; } = string.Empty;
    public long ByteCount { get; set; }
    public string? VerificationNote { get; set; }
}
