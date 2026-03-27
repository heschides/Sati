using Sati.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace Sati.Data
{
    public class FormService : IFormService
    {
        private readonly SatiContext _context;

        public FormService(SatiContext context)
        {
            _context = context;
        }

        public async Task UpdateFormAsync(Form form)
        {
            _context.Forms.Update(form);
            await _context.SaveChangesAsync();
        }
    }
}
