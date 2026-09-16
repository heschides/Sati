using Sati.Contracts.V1;

namespace Sati.Models;

/// <summary>
/// Append-only representative-payee ledger transaction. Corrections are new
/// transactions, never edits to earlier financial history.
/// </summary>
public sealed class RepresentativePayeeLedgerEntry
{
    public long Id { get; set; }
    public int PersonId { get; set; }
    public Person? Person { get; set; }
    public int? CheckRequestId { get; set; }
    public CheckRequest? CheckRequest { get; set; }
    public DateTime EntryDate { get; set; }
    public RepresentativePayeeLedgerEntryKind Kind { get; set; }
    public decimal Amount { get; set; }
    public string Description { get; set; } = string.Empty;
    public DateTime RecordedAtUtc { get; set; }
    public int RecordedByUserId { get; set; }
    public string RecordedByName { get; set; } = string.Empty;
}
