using Microsoft.EntityFrameworkCore;
using Sati.Models;
using Windows.UI;


namespace Sati.Data
{
    public class FormService : IFormService
    {
        private readonly IDbContextFactory<SatiContext> _contextFactory;

        public FormService(IDbContextFactory<SatiContext> contextFactory)
        {
            _contextFactory = contextFactory;
        }

        public async Task UpdateFormAsync(Form form)
        {

            await using var context = _contextFactory.CreateDbContext();
            context.Forms.Update(form);
            await context.SaveChangesAsync();
        }

        public async Task OpenFormAsync(Form form)
        {
            await using var context = _contextFactory.CreateDbContext();
            form.OpenedDate = DateTime.Today;
            context.Forms.Update(form);
            await context.SaveChangesAsync();
        }

        public async Task DeleteFormsAsync(IEnumerable<Form> forms)
        {
            await using var context = _contextFactory.CreateDbContext();
            context.Forms.RemoveRange(forms);
            await context.SaveChangesAsync();
        }
    }
}

