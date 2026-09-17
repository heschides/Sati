namespace Sati.Models
{
    /// <summary>
    /// One case manager's decision about a single day of their own calendar: whether the
    /// documented daily average divides by it while that day's documentation window is still
    /// open. Absent means Sati decides — a day counts once it has documented work and nothing
    /// left on its schedule. Either way the row stops mattering once the window closes, because
    /// a settled day always counts.
    /// </summary>
    /// <remarks>
    /// This is a presentation preference about a forecast, never evidence about service. It does
    /// not change a note's status, its billability, or the monthly requirement; time off remains
    /// <see cref="ExemptDate"/>.
    /// </remarks>
    public class ServiceDayInclusion
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public DateTime Date { get; set; }
        public bool IsIncluded { get; set; }
        public User User { get; set; } = null!;
    }
}
