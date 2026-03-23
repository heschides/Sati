using Microsoft.EntityFrameworkCore;
using Sati.Models;

namespace Sati.Data
{
    public class SettingsService : ISettingsService
    {
        private readonly SatiContext _context;

        public SettingsService(SatiContext context)
        {
            _context = context;
        }

        public async Task<Settings> LoadAsync()
        {
            var settings = await _context.Settings.FirstOrDefaultAsync();

            if (settings is null)
            {
                settings = new Settings();
                _context.Settings.Add(settings);
                await _context.SaveChangesAsync();
            }

            return settings;
        }

        public async Task SaveAsync(Settings settings)
        {
            _context.Settings.Update(settings);
            await _context.SaveChangesAsync();
        }
    }
}