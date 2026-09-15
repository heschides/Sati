using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Sati.Models.Billing;
using Sati.Services;
using Xunit;

namespace Sati.Tests;

public sealed class BillingPolicySettingsTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<SatiContext> _options;
    private readonly IDbContextFactory<SatiContext> _factory;
    private readonly SessionService _session = new();

    public BillingPolicySettingsTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<SatiContext>()
            .UseSqlite(_connection)
            .Options;

        using var db = new SatiContext(_options);
        db.Database.EnsureCreated();
        var admin = User.Create(
            0, "policy-admin", "Policy Administrator", string.Empty, string.Empty,
            UserRole.Admin, null, 1);
        db.Users.Add(admin);
        db.SaveChanges();
        _session.SetUser(admin);
        _factory = new ContextFactory(_options);
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public void FreshDefaultsMatchTheConfirmedAnnualAndBillingPolicy()
    {
        var settings = new Settings();

        Assert.Equal(BillingComplianceGate.DefaultRequirements,
            settings.BillingComplianceRequirements);
        Assert.False(settings.AllowPastBillingPolicyEffectiveDates);
        Assert.Equal(10, settings.ReviewOpenDaysBefore);
        Assert.Equal(90, settings.PcpOpenDaysBefore);
        Assert.Equal(30, settings.CompAssessmentOpenDaysBefore);
        Assert.Equal(90, settings.CompAssessmentDaysBeforeAnniversary);
        Assert.Equal(60, settings.ReclassificationOpenDaysBefore);
        Assert.Equal(30, settings.ReclassificationDaysBeforeAnniversary);
        Assert.Equal(90, settings.SafetyPlanOpenDaysBefore);
        Assert.Equal(90, settings.PrivacyPracticesOpenDaysBefore);
        Assert.Equal(90, settings.ReleaseAgencyOpenDaysBefore);
        Assert.Equal(90, settings.ReleaseDhhsOpenDaysBefore);
        Assert.Equal(90, settings.ReleaseMedicalOpenDaysBefore);
    }

    [Fact]
    public async Task LocalSettingsRequireADatedAppendAndRetainImmutableHistoryAndAudit()
    {
        var service = new SettingsService(_factory, _session);
        var settings = await service.LoadAsync();
        settings.BillingComplianceRequirements = BillingComplianceRequirements.None;

        var ordinarySave = await Assert.ThrowsAsync<SettingsSaveException>(
            () => service.SaveAsync(settings));
        Assert.Contains("enforcement date", ordinarySave.Message,
            StringComparison.OrdinalIgnoreCase);

        // Restore the active display value before saving the emergency switch.
        settings.BillingComplianceRequirements = BillingComplianceGate.DefaultRequirements;
        settings.AllowPastBillingPolicyEffectiveDates = true;
        await service.SaveAsync(settings);

        var today = BillingRules.MaineBusinessDate(DateTimeOffset.UtcNow);
        var first = await service.AppendBillingCompliancePolicyAsync(
            new AppendBillingCompliancePolicyRequest(
                Guid.NewGuid(), today.AddDays(-1), BillingComplianceRequirements.Pcp,
                "Correcting an enforcement date entered in error."));
        var correction = await service.AppendBillingCompliancePolicyAsync(
            new AppendBillingCompliancePolicyRequest(
                Guid.NewGuid(), today.AddDays(-1),
                BillingComplianceRequirements.ComprehensiveAssessment,
                "Correcting the requirement selection on the same date."));

        Assert.True(correction.Id > first.Id);
        var history = await service.LoadBillingCompliancePolicyHistoryAsync();
        Assert.Equal([correction.Id, first.Id], history.Select(item => item.Id));

        await using var db = _factory.CreateDbContext();
        Assert.Equal(2, await db.BillingCompliancePolicyVersions.CountAsync());
        Assert.Equal(2, await db.AuditEvents.CountAsync(
            audit => audit.Action == "billing-compliance-policy.appended"));

        var retained = await db.BillingCompliancePolicyVersions.FirstAsync();
        db.Remove(retained);
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task LocalExactDateResolutionUsesThePolicyOnEachSideOfEnforcement()
    {
        var service = new SettingsService(_factory, _session);
        var currentSettings = await service.LoadAsync();
        var firstEnforcement = new DateTime(2188, 4, 1);
        var secondEnforcement = new DateTime(2188, 5, 1);

        await using (var db = _factory.CreateDbContext())
        {
            var actorId = _session.CurrentUser!.Id;
            db.BillingCompliancePolicyVersions.AddRange(
                BillingCompliancePolicyVersion.Create(
                    1,
                    BillingComplianceRequirements.Pcp,
                    firstEnforcement,
                    firstEnforcement,
                    actorId,
                    new DateTime(2188, 4, 1, 12, 0, 0, DateTimeKind.Utc)),
                BillingCompliancePolicyVersion.Create(
                    1,
                    BillingComplianceRequirements.ComprehensiveAssessment,
                    secondEnforcement,
                    secondEnforcement,
                    actorId,
                    new DateTime(2188, 5, 1, 12, 0, 0, DateTimeKind.Utc)));
            await db.SaveChangesAsync();
        }

        Assert.Equal(
            currentSettings.BillingComplianceRequirements,
            await service.ResolveBillingComplianceRequirementsAsync(
                firstEnforcement.AddDays(-1)));
        Assert.Equal(
            BillingComplianceRequirements.Pcp,
            await service.ResolveBillingComplianceRequirementsAsync(
                secondEnforcement.AddDays(-1)));
        Assert.Equal(
            BillingComplianceRequirements.ComprehensiveAssessment,
            await service.ResolveBillingComplianceRequirementsAsync(secondEnforcement));
    }

    [Fact]
    public async Task LocalImpactPreviewIsReadOnlyAndSeparatesUnsubmittedFromFinalizedBilling()
    {
        var service = new SettingsService(_factory, _session);
        await service.LoadAsync();

        await using (var db = _factory.CreateDbContext())
        {
            var admin = await db.Users.SingleAsync();
            var person = Person.CreatePerson(
                admin.Id,
                "Synthetic",
                "Preview",
                string.Empty,
                new DateTime(1990, 1, 1),
                effective: null,
                WaiverType.Section21,
                new Settings());
            person.AgencyId = 1;
            db.People.Add(person);
            await db.SaveChangesAsync();

            db.Forms.Add(new Form(
                FormType.PCP,
                new DateTime(2097, 3, 7),
                targetEffectiveDate: new DateTime(2097, 3, 7))
            {
                PersonId = person.Id
            });
            var unsubmitted = Note.Create(
                "Synthetic unsubmitted preview note",
                new DateTime(2097, 3, 8),
                NoteStatus.ComplianceBlocked,
                15,
                person.Id);
            unsubmitted.AgencyId = 1;
            var finalized = Note.Create(
                "Synthetic finalized preview note",
                new DateTime(2097, 3, 9),
                NoteStatus.Approved,
                15,
                person.Id);
            finalized.AgencyId = 1;
            db.Notes.AddRange(unsubmitted, finalized);
            await db.SaveChangesAsync();

            var period = new BillingPeriod
            {
                UserId = admin.Id,
                Month = 3,
                Year = 2097,
                Status = BillingStatus.Submitted,
                SubmittedAt = new DateTime(2097, 4, 1)
            };
            period.Lines.Add(new ClaimLine
            {
                NoteId = finalized.Id,
                DateOfService = finalized.EventDate!.Value,
                ProcedureCode = "T2023",
                Units = 1,
                ChargeAmount = 1,
                ClientMaineCareId = "synthetic",
                RenderingProviderNpi = "1999999984",
                DiagnosisCode = "F89",
                PlaceOfService = 11
            });
            db.BillingPeriods.Add(period);
            await db.SaveChangesAsync();
        }

        var preview = await service.PreviewBillingCompliancePolicyAsync(
                new PreviewBillingCompliancePolicyRequest(
                new DateTime(2097, 3, 1),
                BillingComplianceRequirements.None));

        Assert.Equal(1, preview.DraftOrUnsubmittedNotes.NewlyUnblocked);
        Assert.Equal(1, preview.SubmittedOrFinalizedNotes.NewlyUnblocked);
        Assert.Equal(1, preview.SubmittedOrFinalizedClaimRecords.NewlyUnblocked);
        await using (var verification = _factory.CreateDbContext())
        {
            Assert.Empty(await verification.BillingCompliancePolicyVersions.ToListAsync());
            Assert.Empty(await verification.BillingCompliancePolicyReviewFlags.ToListAsync());
            Assert.Empty(await verification.AuditEvents.ToListAsync());
            Assert.Equal(2, await verification.Notes.CountAsync());
            Assert.Single(await verification.ClaimLines.ToListAsync());
        }

        var appendRequest = new AppendBillingCompliancePolicyRequest(
            Guid.NewGuid(),
            new DateTime(2097, 3, 1),
            BillingComplianceRequirements.None);
        var saved = await service.AppendBillingCompliancePolicyAsync(appendRequest);
        var replay = await service.AppendBillingCompliancePolicyAsync(appendRequest);
        Assert.Equal(saved, replay);
        var flags = await service.LoadBillingCompliancePolicyReviewFlagsAsync();
        Assert.Equal(2, flags.Count(flag => flag.PolicyVersionId == saved.Id));
        Assert.All(flags.Where(flag => flag.PolicyVersionId == saved.Id), flag =>
            Assert.Equal("Unresolved", flag.Status));

        var administrator = _session.CurrentUser!;
        User billingOnly;
        await using (var addBiller = _factory.CreateDbContext())
        {
            billingOnly = User.Create(
                0, "policy-billing", "Policy Billing", string.Empty, string.Empty,
                UserRole.CaseManager, null, administrator.AgencyId);
            billingOnly.Permissions = UserPermissions.Billing;
            addBiller.Users.Add(billingOnly);
            await addBiller.SaveChangesAsync();
        }
        _session.SetUser(billingOnly);
        var billingQueue = await new Sati.Services.Billing.BillingService(_factory, _session)
            .GetBillingCompliancePolicyReviewFlagsAsync(billingOnly.ToAgencyActor());
        Assert.Equal(2, billingQueue.Count);
        Assert.False(_session.CurrentUser!.HasAdminPermissions);
        _session.SetUser(administrator);

        await using (var afterApply = _factory.CreateDbContext())
        {
            Assert.Single(await afterApply.BillingCompliancePolicyVersions.ToListAsync());
            Assert.Equal(2, await afterApply.BillingCompliancePolicyReviewFlags.CountAsync());
            Assert.Single(await afterApply.AuditEvents.ToListAsync());
            Assert.Equal(2, await afterApply.Notes.CountAsync());
            Assert.Single(await afterApply.ClaimLines.ToListAsync());

            // Force the child-row insert to fail. EF's single SaveChanges transaction
            // must roll back the policy and audit row as well as the review flags.
            await afterApply.Database.ExecuteSqlRawAsync("""
                CREATE TRIGGER fail_policy_review_flag_insert
                BEFORE INSERT ON BillingCompliancePolicyReviewFlags
                BEGIN
                    SELECT RAISE(ABORT, 'synthetic review flag failure');
                END;
                """);
        }

        var rejectedChangeId = Guid.NewGuid();
        await Assert.ThrowsAsync<DbUpdateException>(() =>
            service.AppendBillingCompliancePolicyAsync(
                new AppendBillingCompliancePolicyRequest(
                    rejectedChangeId,
                    new DateTime(2097, 3, 1),
                    BillingComplianceRequirements.Pcp)));
        await using var atomicityVerification = _factory.CreateDbContext();
        await atomicityVerification.Database.ExecuteSqlRawAsync(
            "DROP TRIGGER fail_policy_review_flag_insert");
        Assert.False(await atomicityVerification.BillingCompliancePolicyVersions.AnyAsync(
            version => version.VersionId == rejectedChangeId));
        Assert.Single(await atomicityVerification.BillingCompliancePolicyVersions.ToListAsync());
        Assert.Equal(2,
            await atomicityVerification.BillingCompliancePolicyReviewFlags.CountAsync());
        Assert.Single(await atomicityVerification.AuditEvents.ToListAsync());
    }

    [Fact]
    public void AdminUiSeparatesOrdinarySaveFromEffectiveDatedPolicyApply()
    {
        var root = RepositoryRoot();
        var view = File.ReadAllText(Path.Combine(root, "Views", "SettingsWindow.xaml"));
        var viewModel = File.ReadAllText(Path.Combine(root, "ViewModels", "SettingsViewModel.cs"));

        Assert.Contains("Billing policy enforcement date", view, StringComparison.Ordinal);
        Assert.Contains("ApplyBillingCompliancePolicyCommand", view, StringComparison.Ordinal);
        Assert.Contains("Allow past billing policy enforcement dates", view, StringComparison.Ordinal);
        Assert.Contains("Count PCP opening for billing compliance", view, StringComparison.Ordinal);
        Assert.Contains("BillingPolicyImpactPreview", view, StringComparison.Ordinal);
        Assert.Contains("PreviewBillingCompliancePolicyImpactCommand", view, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding BillingPolicyHistory}\"", view, StringComparison.Ordinal);
        var ordinarySaveStart = viewModel.IndexOf(
            "public async Task<bool> TrySaveSettingsAsync()", StringComparison.Ordinal);
        var policyApplyStart = viewModel.IndexOf(
            "private async Task ApplyBillingCompliancePolicyAsync()", ordinarySaveStart,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "_settings.BillingComplianceRequirements =",
            viewModel[ordinarySaveStart..policyApplyStart],
            StringComparison.Ordinal);
    }

    private static string RepositoryRoot(
        [System.Runtime.CompilerServices.CallerFilePath] string callerPath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(callerPath)!, ".."));

    private sealed class ContextFactory(DbContextOptions<SatiContext> options)
        : IDbContextFactory<SatiContext>
    {
        public SatiContext CreateDbContext() => new(options);
    }
}
