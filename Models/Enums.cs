using System;
using System.Collections.Generic;
using System.Text;

namespace Proofer
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
            Schdeduled,
            NotePending,
            Logged,
            Cancelled,
            Delayed
        }
    }
}
