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
                settings = new Settings
                {
                    // Reviews
                    ReviewOpenDaysBefore = 10,
                    ReviewDaysAfterDue = 10,
                    // PCP
                    PcpOpenDaysBefore = 30,
                    PcpDaysAfterDue = 30,
                    // Comprehensive Assessment
                    CompAssessmentOpenDaysBefore = 30,
                    CompAssessmentDaysAfterDue = 30,
                    // Reclassification
                    ReclassificationOpenDaysBefore = 15,
                    ReclassificationDaysAfterDue = 0,
                    // Safety Plan
                    SafetyPlanOpenDaysBefore = 60,
                    SafetyPlanDaysAfterDue = 30,
                    // Privacy Practices
                    PrivacyPracticesOpenDaysBefore = 30,
                    PrivacyPracticesDaysAfterDue = 30,
                    // Releases
                    ReleaseAgencyOpenDaysBefore = 30,
                    ReleaseAgencyDaysAfterDue = 30,
                    ReleaseDhhsOpenDaysBefore = 30,
                    ReleaseDhhsDaysAfterDue = 30,
                    ReleaseMedicalOpenDaysBefore = 30,
                    ReleaseMedicalDaysAfterDue = 30,
                };

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