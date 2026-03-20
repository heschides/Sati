using System;
using System.Collections.Generic;
using System.Text;

namespace Sati
{
    public class Enums
    {
        public enum WaiverType
        {
            None,
            Section21,
            Section29
        }

        public enum FormType
        {
            Q1R,
            Q2R,
            Q3R,
            Q4R,

            PCP,
            ComprehensiveAssessment,
            Reclassification,

            Release_Agency,
            Release_DHHS,
            Release_Medical
        }

        public enum NoteStatus
        {
            Scheduled,
            Pending,
            Logged,
            Cancelled,
            Delayed,
            Abandoned
        }

        public enum NoteType
        {
            Visit,
            Contact,
            Documentation
        }
    }
}

