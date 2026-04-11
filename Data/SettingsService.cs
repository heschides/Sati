using Microsoft.EntityFrameworkCore;
using Sati.Models;

namespace Sati.Data
{
    public class SettingsService : ISettingsService
    {
        private readonly IDbContextFactory<SatiContext> _contextFactory;

        public SettingsService(IDbContextFactory<SatiContext> contextFactory)
        {
            _contextFactory = contextFactory;
        }

        public async Task<Settings> LoadAsync()
        {
            await using var context = _contextFactory.CreateDbContext();
            var settings = await context.Settings.FirstOrDefaultAsync();

            if (settings is null)
            {
                settings = new Settings
                {
                    ReviewOpenDaysBefore = 10,
                    ReviewDaysAfterDue = 10,
                    PcpOpenDaysBefore = 30,
                    PcpDaysAfterDue = 30,
                    CompAssessmentOpenDaysBefore = 30,
                    CompAssessmentDaysAfterDue = 30,
                    ReclassificationOpenDaysBefore = 15,
                    ReclassificationDaysAfterDue = 0,
                    SafetyPlanOpenDaysBefore = 60,
                    SafetyPlanDaysAfterDue = 30,
                    PrivacyPracticesOpenDaysBefore = 30,
                    PrivacyPracticesDaysAfterDue = 30,
                    ReleaseAgencyOpenDaysBefore = 30,
                    ReleaseAgencyDaysAfterDue = 30,
                    ReleaseDhhsOpenDaysBefore = 30,
                    ReleaseDhhsDaysAfterDue = 30,
                    ReleaseMedicalOpenDaysBefore = 30,
                    ReleaseMedicalDaysAfterDue = 30,
                };

                context.Settings.Add(settings);
                await context.SaveChangesAsync();
            }

            return settings;
        }

        public async Task SaveAsync(Settings settings)
        {
            await using var context = _contextFactory.CreateDbContext();
            context.Settings.Update(settings);
            await context.SaveChangesAsync();
        }
    }
}