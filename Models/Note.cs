using System;
using System.Collections.Generic;
using System.Text;
using static Proofer.Enums;

namespace Proofer.Models
{
    public class Note
    {
        public int Id { get; private set; }
        public string Narrative { get; set;  } = string.Empty;
        public DateTime? EventDate { get; set; }
        public NoteStatus? Status { get; set; } 
        public int? Units {  get; set; }
        public int PersonId { get; set; }
        public Person Person { get; set; } = null!; 

        private protected Note() { }

        public static Note Create( string narrative, DateTime? eventDate, NoteStatus? status, int? unitCount, int personId)
        {
            var _note = new Note()
            {
                Narrative = narrative,
                EventDate = eventDate,
                Status = status,
                Units = unitCount,
                PersonId = personId
            };
            return _note;
        }
    }
}
