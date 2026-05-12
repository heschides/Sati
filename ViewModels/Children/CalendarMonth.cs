namespace Sati.ViewModels.Children
{
    public class CalendarMonth
    {
        public string Name { get; init; } = string.Empty;
        public int Month { get; init; }
        public int Year { get; init; }
        // null entries are leading empty cells for grid alignment
        public List<CalendarDay?> Cells { get; init; } = [];
    }
}