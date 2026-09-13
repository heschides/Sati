namespace Sati.Models;

/// <summary>
/// The current consumer profile photo. It is intentionally separate from Person so
/// ordinary caseload/profile projections never pull a multi-megabyte binary column.
/// </summary>
public sealed class PersonPhoto
{
    public int PersonId { get; set; }
    public int AgencyId { get; set; }
    public byte[] Content { get; set; } = [];
    public string ContentType { get; set; } = string.Empty;
    public string ContentSha256 { get; set; } = string.Empty;
    public int PixelWidth { get; set; }
    public int PixelHeight { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public int UpdatedByUserId { get; set; }
    public long Revision { get; set; } = 1;
}
