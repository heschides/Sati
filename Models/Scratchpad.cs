using System;
using System.Collections.Generic;
using System.Text;

namespace Sati.Models
{
    public class Scratchpad
    {
        public int Id { get; set;  }
        public int UserId { get; set; }
        public DateTime Date { get; set; }
        public string Content { get; set;  } = string.Empty;
    }
}
