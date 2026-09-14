using Sati.Contracts.V1;

namespace Sati.Models;

/// <summary>
/// One consumer's current weekly defaults. Updating it affects future generated
/// drafts only; every generated CheckRequest remains a frozen field snapshot.
/// </summary>
public sealed class CheckRequestTemplate
{
    public int Id { get; set; }
    public int PersonId { get; set; }
    public Person? Person { get; set; }
    public int Revision { get; set; } = 1;
    public bool IsEnabled { get; set; } = true;
    public DayOfWeek GenerateOn { get; set; } = DayOfWeek.Monday;
    public int NeededByDaysAfterRequest { get; set; }
    public string PayableTo { get; set; } = string.Empty;
    public string MailingAddress { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime EffectiveFrom { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
