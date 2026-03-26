using System;
using System.Collections.Generic;
using System.Text;

namespace Sati.Models
{
    public enum UpcomingEventKind
    {
        OpenReview,
        LateReview,
        ScheduledVisit
    }
    public record UpcomingEvent
    {
        public string ClientName { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public DateTime Date { get; init; }
        public UpcomingEventKind Kind { get; init; }
    }
}
