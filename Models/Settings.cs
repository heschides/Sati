using System;
using System.Collections.Generic;
using System.Text;

namespace Sati.Models
{
    public class Settings
    {
        public int Id { get; set; }

        // Abandonment
        public int AbandonedAfterDays { get; set; } = 7;

        // Productivity
        public int ProductivityThreshold { get; set; } = 100;
        public decimal BaseIncentive { get; set; } = 0;
        public decimal PerUnitIncentive { get; set; } = 0;

        // Note templates
        public string VisitTemplate { get; set; } = string.Empty;
        public string ContactTemplate { get; set; } = string.Empty;
        public string DocumentationTemplate { get; set; } = string.Empty;
    }
}
