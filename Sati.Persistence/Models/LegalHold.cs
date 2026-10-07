namespace Sati.Models;

/// <summary>
/// One legal hold blocking rule-3 consumer deletion for a specific person.
///
/// <para>
/// Deliberately narrower than OPERATIONS.md's full record-class/scope hold model — this exists
/// to retain compatibility with existing person holds. RecordsGovernance preserves its effect,
/// imports active holds, and requires independent approval before marking this row released.
/// </para>
/// </summary>
public sealed class LegalHold
{
    public int Id { get; set; }
    public int AgencyId { get; set; }
    public int PersonId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? CaseReference { get; set; }
    public string? IssuedBy { get; set; }
    public DateTime EffectiveAtUtc { get; set; }
    public int PlacedByUserId { get; set; }
    public DateTime PlacedAtUtc { get; set; } = DateTime.UtcNow;
    public bool IsReleased { get; set; }
    public int? ReleasedByUserId { get; set; }
    public DateTime? ReleasedAtUtc { get; set; }
    public string? ReleaseNote { get; set; }
}
