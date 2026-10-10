namespace Sati.Models;

/// <summary>Server scheduling positions, independent of signature and mail evidence.</summary>
public enum SignatureWorkKind { Projection = 1, Package = 2, Mail = 3 }

public sealed class SignatureWorkRotation
{
    public SignatureWorkKind WorkKind { get; set; }
    public int? LastAgencyId { get; set; }
    public long Revision { get; set; } = 1;
}

public sealed class SignatureAgencyWorkRotation
{
    public int AgencyId { get; set; }
    public SignatureWorkKind WorkKind { get; set; }
    public long? LastItemId { get; set; }
    public long Revision { get; set; } = 1;
}
