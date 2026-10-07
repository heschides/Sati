using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using Sati.Contracts.V1;

namespace Sati.Models
{
    public class Scratchpad
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public DateTime Date { get; set; }
        public int Revision { get; set; } = 1;
        public string Content { get; set; } = string.Empty;
        public ObservableCollection<ScratchpadComment> Comments { get; set; } = [];

        [NotMapped]
        public string PlainContent => JournalDocument.Parse(Content).ToPlainText();

        [NotMapped]
        public string DisplayContent => string.IsNullOrWhiteSpace(PlainContent)
            ? "No text was entered on this day."
            : PlainContent;

        [NotMapped]
        public bool HasComments => Comments.Count > 0;

        [NotMapped]
        public string ContentPreview
        {
            get
            {
                var firstLine = PlainContent.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                                       .FirstOrDefault() ?? string.Empty;
                return firstLine.Length > 80 ? firstLine[..80] + "…" : firstLine;
            }
        }
    }
}
