namespace Sati.Data;

/// <summary>
/// Narrative-free productivity projections for Statistics and Overview. The full
/// note entity can contain a clinical narrative that neither view needs to transfer.
/// </summary>
public sealed record ProductivityMonthUnits(int Year, int Month, int Units);
public sealed record ProductivityDayUnits(DateTime Date, int Units, int NoteCount);

public interface IProductivityReportService
{
    Task<IReadOnlyList<ProductivityMonthUnits>> GetUnitsAsync(
        DateTime windowStart,
        DateTime windowEnd);

    Task<IReadOnlyList<ProductivityDayUnits>> GetDaysAsync(int year, int month);
}
