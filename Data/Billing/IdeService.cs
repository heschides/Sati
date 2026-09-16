using Microsoft.EntityFrameworkCore;
using Sati.Data;
using Sati.Helpers;
using Sati.Models.Billing;
using Sati.Services.Billing;
using System.IO;
using System.Data;
using Sati.Contracts.V1;

namespace Sati.Edi
{
    public class EdiService : IEdiService
    {
        private readonly IDbContextFactory<SatiContext> _contextFactory;
        private readonly ISessionService _sessionService;

        // User-scoped output avoids requiring administrator rights or a machine-global folder.
        private static readonly string OutputDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Sati", "EDI");

        public EdiService(IDbContextFactory<SatiContext> contextFactory, ISessionService sessionService)
        {
            _contextFactory = contextFactory;
            _sessionService = sessionService;
        }

        public async Task<string> GenerateAndSaveAsync(int billingPeriodId, bool isTest, string idempotencyKey)
        {
            if (!Guid.TryParse(idempotencyKey, out var parsedKey))
                throw new ArgumentException("A valid EDI idempotency key is required.", nameof(idempotencyKey));
            var normalizedKey = parsedKey.ToString("N");
            await using var context = _contextFactory.CreateDbContext();
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var actor = await LocalTenantAccess.EnsureSessionAsync(context, _sessionService);
            if (!actor.HasBillingPermissions)
                throw new UnauthorizedAccessException("Billing permission is required to generate EDI.");

            var previous = await context.EdiGenerations.AsNoTracking().SingleOrDefaultAsync(generation =>
                generation.AgencyId == actor.AgencyId && generation.ActorUserId == actor.Id &&
                generation.IdempotencyKey == normalizedKey);
            if (previous is not null)
            {
                if (previous.BillingPeriodId != billingPeriodId || previous.IsTest != isTest)
                    throw new InvalidOperationException("This EDI retry key was already used for a different request.");
            }

            var period = await LoadExportablePeriodAsync(context, billingPeriodId, actor);
            if (previous is not null)
                return await SaveFileAsync(previous.FileName, previous.Content);

            var generatedAt = DateTime.Now;
            var controlNumber = CreateEdiControlNumber(normalizedKey);
            var ediContent = EdiGenerator.Generate(
                period, isTest, generatedAt, controlNumber);

            // File naming per OA companion guide:
            // - Must contain OATEST for test files
            // - Must contain 837P for SFTP submissions
            // The web portal upload doesn't require 837P in the name but
            // including it is harmless and makes the file self-documenting.
            var timestamp = generatedAt.ToString("yyyyMMdd_HHmmss");
            var testMarker = isTest ? ".OATEST" : string.Empty;
            var fileName = $"837P{testMarker}_{period.Year}{period.Month:D2}_{timestamp}_{normalizedKey[..8]}.txt";
            context.EdiGenerations.Add(new EdiGeneration
            {
                AgencyId = actor.AgencyId,
                ActorUserId = actor.Id,
                BillingPeriodId = billingPeriodId,
                IdempotencyKey = normalizedKey,
                IsTest = isTest,
                FileName = fileName,
                Content = ediContent,
                CreatedAtUtc = DateTime.UtcNow
            });
            context.BillingSubmissionEvents.Add(new BillingSubmissionEvent
            {
                AgencyId = actor.AgencyId,
                BillingPeriodId = billingPeriodId,
                OccurredAtUtc = DateTime.UtcNow,
                Stage = Sati.Contracts.V1.BillingSubmissionStage.Generated,
                Reference = fileName,
                Explanation = isTest
                    ? "Test 837P generated; no external transmission is implied."
                    : "Production 837P generated; transmission status has not been recorded.",
                IsSynthetic = false
            });
            LocalAuditTrail.Record(context, actor, LocalAuditActions.BillingEdiGenerated,
                "BillingPeriod", billingPeriodId);
            try
            {
                await context.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch (DbUpdateException)
            {
                await transaction.RollbackAsync();
                await transaction.DisposeAsync();
                context.ChangeTracker.Clear();
                await using var replayTransaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
                await LoadExportablePeriodAsync(context, billingPeriodId, actor);
                var completed = await context.EdiGenerations.AsNoTracking().SingleOrDefaultAsync(generation =>
                    generation.AgencyId == actor.AgencyId && generation.ActorUserId == actor.Id &&
                    generation.IdempotencyKey == normalizedKey);
                if (completed is null)
                    throw;
                if (completed.BillingPeriodId != billingPeriodId || completed.IsTest != isTest)
                {
                    throw new InvalidOperationException(
                        "This EDI retry key was already used for a different request.");
                }
                return await SaveFileAsync(completed.FileName, completed.Content);
            }
            return await SaveFileAsync(fileName, ediContent);
        }

        private async Task<BillingPeriod> LoadExportablePeriodAsync(
            SatiContext context, int billingPeriodId, Sati.Models.User capturedActor)
        {
            var actor = await LocalTenantAccess.EnsureSessionAsync(context, _sessionService);
            if (!ReferenceEquals(actor, capturedActor))
                throw new UnauthorizedAccessException("The signed-in account changed while the 837P was being prepared. Start again.");
            if (!actor.HasBillingPermissions)
                throw new UnauthorizedAccessException("Billing permission is required to generate EDI.");
            var period = await context.BillingPeriods.AsNoTracking().Include(p => p.User).Include(p => p.Lines)
                .SingleOrDefaultAsync(p => p.Id == billingPeriodId && p.User.AgencyId == actor.AgencyId)
                ?? throw new InvalidOperationException($"Billing period {billingPeriodId} not found.");
            if (period.Lines.Count == 0)
                throw new InvalidOperationException($"Billing period {billingPeriodId} has no claim lines.");
            if (period.Status != BillingStatus.Submitted)
                throw new InvalidOperationException("Submit and lock the billing period before generating its 837P file.");
            EdiGenerator.ValidatePeriod(period);

            var noteIds = period.Lines.Select(line => line.NoteId).Distinct().ToList();
            var notes = await context.Notes.AsNoTracking()
                .Include(note => note.Person).ThenInclude(person => person.Forms)
                    .ThenInclude(form => form.Attestations)
                .Include(note => note.Person).ThenInclude(person => person.ReleaseObligations)
                    .ThenInclude(obligation => obligation.Attestations)
                .Where(note => noteIds.Contains(note.Id) && note.AgencyId == actor.AgencyId &&
                    note.Person.AgencyId == actor.AgencyId && note.Person.User != null &&
                    note.Person.User.AgencyId == actor.AgencyId)
                .AsSplitQuery()
                .ToListAsync();
            if (notes.Count != noteIds.Count)
                throw new InvalidOperationException("The billing period contains a note outside the agency boundary or a missing source record.");
            await BillingComplianceProjectionLoader.PopulateAsync(
                context, notes.Select(note => note.Person), actor.AgencyId);
            var compliancePolicy = await BillingCompliancePolicyContextLoader.LoadAsync(
                context, actor.AgencyId);
            var approverIds = notes.Where(note => note.OverrideApprovedById.HasValue)
                .Select(note => note.OverrideApprovedById!.Value).Distinct().ToList();
            var agencyApprovers = await context.Users.AsNoTracking()
                .Where(user => user.AgencyId == actor.AgencyId && approverIds.Contains(user.Id))
                .Select(user => user.Id).ToListAsync();
            var today = BillingRules.MaineBusinessDate(DateTimeOffset.UtcNow);
            var errors = new List<string>();
            foreach (var line in period.Lines)
            {
                var note = notes.Single(candidate => candidate.Id == line.NoteId);
                await NoteService.EnsureServiceTimeAvailableAsync(context, note.Person.UserId, note, note.Id);
                var facts = new BillingExportSource(note.PersonId, (int?)note.Status, note.EventDate,
                    note.ComplianceOverride, note.OverrideReason, note.ApprovedById, note.ApprovedAt,
                    note.OverrideApprovedById, note.OverrideApprovedAt,
                    note.OverrideApprovedById is int approverId && agencyApprovers.Contains(approverId));
                // Release re-checks the same service-date decision claim creation
                // made, including any exact-obligation exception or Admin recovery.
                var recoveryDecisions = await BillingService.LoadRecoveryDecisionsForNoteAsync(
                    context, actor.AgencyId, note.PersonId, note.Id);
                var complianceErrors = BillingService.EvaluateBillingComplianceRelease(
                    note, compliancePolicy, recoveryDecisions);
                errors.AddRange(BillingExportGate.Evaluate(
                    ProfessionalClaimSnapshotCodec.Deserialize(line.ClaimSnapshotJson), actor.AgencyId,
                    line.DateOfService, line.IsComplianceException, line.ComplianceExceptionReason, facts,
                    complianceErrors, compliancePolicy.Resolve(line.DateOfService))
                    .Select(error => $"Note {line.NoteId}: {error}"));
            }
            if (errors.Count > 0)
                throw new InvalidOperationException("The 837P cannot be released. " + string.Join(" ", errors));
            return period;
        }

        private static string CreateEdiControlNumber(string normalizedKey) =>
            (Convert.ToUInt32(normalizedKey[..8], 16) % 1_000_000_000)
            .ToString("D9", System.Globalization.CultureInfo.InvariantCulture);

        private static async Task<string> SaveFileAsync(string fileName, string content)
        {
            Directory.CreateDirectory(OutputDirectory);
            var filePath = Path.Combine(OutputDirectory, fileName);
            await File.WriteAllTextAsync(filePath, content);
            return filePath;
        }
    }
}
