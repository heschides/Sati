using Microsoft.EntityFrameworkCore;
using Sati.Data;
using Sati.Helpers;
using Sati.Models.Billing;
using System.IO;

namespace Sati.Edi
{
    public class EdiService : IEdiService
    {
        private readonly IDbContextFactory<SatiContext> _contextFactory;

        // Submitter ID — Tax ID without hyphens. Replace with OA-assigned
        // submitter ID after enrollment if different.
        private const string SubmitterId = "010278395";

        // Output directory for generated 837P files.
        private const string OutputDirectory = @"C:\Published\Sati\Contained\EDI";

        public EdiService(IDbContextFactory<SatiContext> contextFactory)
        {
            _contextFactory = contextFactory;
        }

        public async Task<string> GenerateAndSaveAsync(int billingPeriodId, bool isTest = true)
        {
            await using var context = _contextFactory.CreateDbContext();

            var period = await context.BillingPeriods
                .Include(p => p.Lines)
                    .ThenInclude(l => l.Note)
                        .ThenInclude(n => n.Person)
                            .ThenInclude(p => p.Agency)
                .FirstOrDefaultAsync(p => p.Id == billingPeriodId)
                ?? throw new InvalidOperationException(
                    $"Billing period {billingPeriodId} not found.");

            if (!period.Lines.Any())
                throw new InvalidOperationException(
                    $"Billing period {billingPeriodId} has no claim lines.");

            var ediContent = EdiGenerator.Generate(period, SubmitterId, isTest);

            Directory.CreateDirectory(OutputDirectory);

            // File naming per OA companion guide:
            // - Must contain OATEST for test files
            // - Must contain 837P for SFTP submissions
            // The web portal upload doesn't require 837P in the name but
            // including it is harmless and makes the file self-documenting.
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var testMarker = isTest ? ".OATEST" : string.Empty;
            var fileName = $"837P{testMarker}_{period.Year}{period.Month:D2}_{timestamp}.txt";
            var filePath = Path.Combine(OutputDirectory, fileName);

            await File.WriteAllTextAsync(filePath, ediContent);

            return filePath;
        }
    }
}