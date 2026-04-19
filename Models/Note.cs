using System;
using System.Collections.Generic;
using System.Text;


namespace Sati.Models
{
    public class Note
    {
        public int Id { get; private set; }
        public string Narrative { get; set;  } = string.Empty;
        public DateTime? EventDate { get; set; }
        public NoteStatus? Status { get; set; } 
        public decimal? Units {  get; set; }
        public int PersonId { get; set; }
        public Person Person { get; set; } = null!;
        public FormType? FormType { get; set; }
        public NoteType? NoteType { get; set; }
        public int? AgencyId { get; set; }
        public Agency? Agency { get; set; }
        public string? ReturnReason { get; set; }
        public int? ReturnedById { get; set; }
        public int? ApprovedById {  get; set; }
        public DateTime? ApprovedAt { get; set; }
        public DateTime? ReturnedAt { get; set; }

        private protected Note() { }

        public static Note Create( string narrative, DateTime? eventDate, NoteStatus? status, decimal? unitCount, int personId, FormType? formType=null, NoteType? noteType = null)
        {
            var _note = new Note()
            {
                Narrative = narrative,
                EventDate = eventDate,
                Status = status,
                Units = unitCount,
                PersonId = personId,
                FormType = formType,
                NoteType = noteType
            };
            return _note;
        }
    }
}
