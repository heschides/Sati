using Sati.Models;

namespace Sati.ViewModels.Children
{
    public class CalendarDay
    {
        public DateTime Date { get; init; }
        public bool IsExempt { get; set; }
        public int? ExemptDateId { get; set; }
        public bool IsWeekend { get; init; }
        public bool IsToday => Date.Date == DateTime.Today;
        public List<Note> Notes { get; init; } = [];
        public bool HasNotes => Notes.Count > 0;
    }
}