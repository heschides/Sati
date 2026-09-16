using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Sati.Reporting;
using Xunit;

namespace Sati.Tests;

[Collection(PdfRenderingCollection.Name)]
public sealed class CheckRequestTests : IDisposable
{
    private readonly SqliteConnection connection = new("DataSource=:memory:");
    private readonly IDbContextFactory<SatiContext> factory;
    private readonly SessionService session = new();
    private readonly int personId;
    private readonly User owner;
    private readonly User supervisor;
    private readonly User finance;

    public CheckRequestTests()
    {
        connection.Open();
        var options = new DbContextOptionsBuilder<SatiContext>().UseSqlite(connection).Options;
        using var db = new SatiContext(options);
        db.Database.EnsureCreated();
        var agency = db.Agencies.Single(x => x.Id == 1);
        agency.Name = "Woodfords Family Services";
        supervisor = User.Create(0, "supervisor", "Christine Campbell", "", "", UserRole.Supervisor, null, agency.Id);
        supervisor.Agency = agency;
        db.Users.Add(supervisor);
        db.SaveChanges();
        owner = User.Create(0, "case.manager", "Joshua White", "", "", UserRole.CaseManager, supervisor.Id, agency.Id);
        owner.Agency = agency;
        owner.Supervisor = supervisor;
        db.Users.Add(owner);
        db.SaveChanges();
        finance = User.Create(0, "finance", "Finance User", "", "", UserRole.Finance, null, agency.Id);
        finance.Agency = agency;
        db.Users.Add(finance);
        db.SaveChanges();
        var person = Person.Rehydrate(0, owner.Id);
        person.FirstName = "Sample";
        person.LastName = "Consumer";
        person.AgencyId = agency.Id;
        person.CaseManagerIsRepPayee = true;
        db.People.Add(person);
        db.SaveChanges();
        personId = person.Id;
        session.SetUser(owner);
        factory = new SingleConnectionContextFactory(options);
    }

    public void Dispose() => connection.Dispose();

    [Fact]
    public void PublicationRequiresEveryPrintedEntry()
    {
        var blockers = CheckRequestPublication.FindPublicationBlockers(
            DateTime.Today, "", "", 0m, null, "", alreadyPublished: false);

        Assert.Contains(blockers, x => x.Contains("payable", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(blockers, x => x.Contains("amount", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(blockers, x => x.Contains("needed", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(blockers, x => x.Contains("reason", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PreparedPdfStatusDoesNotClaimSupervisorApproval()
    {
        var item = new CheckRequestListItem(
            42, 2, DateTime.Today, "Example Vendor", 125.40m, DateTime.Today.AddDays(7), DateTime.UtcNow);

        Assert.Equal("PDF prepared", item.Status);
        Assert.DoesNotContain("approved", item.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WeeklyScheduleFindsTheLatestConfiguredDayWithoutCrossingItsStart()
    {
        var monday = new DateTime(2026, 9, 14);

        Assert.Null(WeeklyCheckRequestSchedule.MostRecentOccurrence(
            DayOfWeek.Monday, monday, monday.AddDays(-1)));
        Assert.Equal(monday, WeeklyCheckRequestSchedule.MostRecentOccurrence(
            DayOfWeek.Monday, monday, monday));
        Assert.Equal(monday, WeeklyCheckRequestSchedule.MostRecentOccurrence(
            DayOfWeek.Monday, monday, monday.AddDays(6)));
        Assert.Equal(monday.AddDays(7), WeeklyCheckRequestSchedule.MostRecentOccurrence(
            DayOfWeek.Monday, monday, monday.AddDays(7)));
    }

    [Fact]
    public async Task WeeklyDefaultCreatesOneFrozenDraftAndKeepsItPendingUntilSubmission()
    {
        var automation = new CheckRequestAutomationService(factory, session);
        var saved = await automation.SaveTemplateAsync(personId,
            new SaveCheckRequestTemplateRequest(
                0, true, DateTime.Today.DayOfWeek, 5,
                "Weekly Vendor", "20 Sample Street", 42.50m, "Weekly allowance"));

        Assert.Equal(1, saved.Revision);
        var first = await automation.EnsureWeeklyDraftsAsync();
        var generated = Assert.Single(first.PendingDrafts);
        Assert.Equal(1, first.CreatedCount);
        Assert.Equal("Weekly Vendor", generated.PayableTo);
        Assert.Equal(42.50m, generated.Amount);
        Assert.Equal(DateTime.Today.AddDays(5), generated.NeededByDate);

        var second = await automation.EnsureWeeklyDraftsAsync();
        Assert.Equal(0, second.CreatedCount);
        Assert.Single(second.PendingDrafts);

        var documents = new CheckRequestService(factory, session);
        var request = await documents.GetByIdAsync(generated.CheckRequestId);
        Assert.True(request!.IsAutomaticallyGenerated);
        await documents.PublishAsync(request);
        var workflow = new RepresentativePayeeService(factory, session);
        await workflow.ApplyActionAsync(request.Id, CheckRequestWorkflowAction.Submitted);

        Assert.Empty(await automation.GetPendingDraftsAsync());
        var afterSubmission = await automation.EnsureWeeklyDraftsAsync();
        Assert.Equal(0, afterSubmission.CreatedCount);
        Assert.Empty(afterSubmission.PendingDrafts);
    }

    [Fact]
    public async Task ScheduledTimeOffCanPrepareTheMatchingWeeklyDraftEarly()
    {
        var timeOffDate = DateTime.Today.AddDays(4);
        var automation = new CheckRequestAutomationService(factory, session);
        await automation.SaveTemplateAsync(personId,
            new SaveCheckRequestTemplateRequest(
                0, true, timeOffDate.DayOfWeek, 3,
                "Time-off Vendor", "30 Sample Street", 63m, "Weekly expense"));
        var exemptDates = new ExemptDateService(factory, session);
        await exemptDates.AddAsync(owner.Id, timeOffDate, "Scheduled time off");

        var collision = Assert.Single(await automation.GetTimeOffCollisionsAsync(timeOffDate));
        Assert.Equal(personId, collision.PersonId);
        Assert.Null(collision.PendingCheckRequestId);

        var first = await automation.EnsureTimeOffDraftsAsync(timeOffDate);
        var generated = Assert.Single(first.PendingDrafts);
        Assert.Equal(1, first.CreatedCount);
        var second = await automation.EnsureTimeOffDraftsAsync(timeOffDate);
        Assert.Equal(0, second.CreatedCount);
        Assert.Single(second.PendingDrafts);

        var documents = new CheckRequestService(factory, session);
        var request = await documents.GetByIdAsync(generated.CheckRequestId);
        Assert.Equal(DateTime.Today, request!.RequestDate);
        Assert.Equal(timeOffDate.Date, request.ScheduledForDate);
        Assert.Equal(timeOffDate.AddDays(3).Date, request.NeededByDate);
        await documents.PublishAsync(request);
        var workflow = new RepresentativePayeeService(factory, session);
        await workflow.ApplyActionAsync(request.Id, CheckRequestWorkflowAction.Submitted);

        Assert.Empty(await automation.GetTimeOffCollisionsAsync(timeOffDate));
    }

    [Fact]
    public async Task DraftSnapshotsProfileFieldsAndPublishedRecordIsLocked()
    {
        var service = new CheckRequestService(factory, session);
        var request = await service.CreateDraftAsync(personId);

        Assert.Equal("Sample Consumer", request.ConsumerName);
        Assert.Equal("Woodfords Family Services", request.AgencyName);
        Assert.Equal("Joshua White", request.CaseManagerName);
        Assert.Equal("Christine Campbell", request.SupervisorName);

        request.PayableTo = "Example Vendor";
        request.MailingAddress = "10 Main Street\nAugusta, ME 04330";
        request.Amount = 125.40m;
        request.NeededByDate = DateTime.Today.AddDays(7);
        request.Reason = "Synthetic accessibility supplies";
        await service.PublishAsync(request);

        Assert.True(request.IsPublished);
        request.Amount = 999m;
        await Assert.ThrowsAsync<CheckRequestLockedException>(() => service.UpdateAsync(request));
        var stored = await service.GetByIdAsync(request.Id);
        Assert.Equal(125.40m, stored!.Amount);
    }

    [Fact]
    public async Task PdfHasTheOriginalFormFieldsAndASafeName()
    {
        var service = new CheckRequestService(factory, session);
        var request = await service.CreateDraftAsync(personId);
        request.PayableTo = "Example Vendor";
        request.MailingAddress = "10 Main Street";
        request.Amount = 125.40m;
        request.NeededByDate = DateTime.Today.AddDays(7);
        request.Reason = "Synthetic accessibility supplies";
        await service.PublishAsync(request);

        var pdf = new CheckRequestPdfExporter().Generate(request);
        var fileName = CheckRequestPdfExporter.SuggestedFileName(request);

        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
        Assert.EndsWith(".pdf", fileName, StringComparison.Ordinal);
        Assert.DoesNotContain('/', fileName);
        Assert.DoesNotContain('\\', fileName);
    }

    [Fact]
    public async Task PublishedRowCannotBeChangedThroughTheContext()
    {
        var service = new CheckRequestService(factory, session);
        var request = await service.CreateDraftAsync(personId);
        request.PayableTo = "Example Vendor";
        request.MailingAddress = "10 Main Street";
        request.Amount = 25m;
        request.NeededByDate = DateTime.Today.AddDays(5);
        request.Reason = "Synthetic supplies";
        await service.PublishAsync(request);

        await using var db = factory.CreateDbContext();
        var stored = await db.CheckRequests.SingleAsync(x => x.Id == request.Id);
        stored.Amount = 999m;

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        Assert.Contains("immutable", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ApprovalReleaseAndReceiptAreSeparateAuditedSteps()
    {
        var documents = new CheckRequestService(factory, session);
        var workflow = new RepresentativePayeeService(factory, session);
        var request = await documents.CreateDraftAsync(personId);
        request.PayableTo = "Example Vendor";
        request.MailingAddress = "10 Main Street";
        request.Amount = 88.50m;
        request.NeededByDate = DateTime.Today.AddDays(5);
        request.Reason = "Synthetic supplies";
        await documents.PublishAsync(request);

        await workflow.ApplyActionAsync(request.Id, CheckRequestWorkflowAction.Submitted);
        session.SetUser(supervisor);
        var pending = await workflow.GetSupervisorQueueAsync();
        Assert.Contains(pending, item => item.CheckRequestId == request.Id);
        await workflow.ApplyActionAsync(request.Id, CheckRequestWorkflowAction.Approved);

        session.SetUser(finance);
        var approved = await workflow.GetFinanceQueueAsync();
        Assert.Contains(approved, item => item.CheckRequestId == request.Id &&
            item.Status == CheckRequestWorkflowStatus.Approved);
        await workflow.ApplyActionAsync(request.Id, CheckRequestWorkflowAction.Released);
        var ledger = await workflow.GetWorkspaceAsync(personId);
        var debit = Assert.Single(ledger.Entries, entry => entry.CheckRequestId == request.Id);
        Assert.Equal(-88.50m, debit.Amount);
        await workflow.ApplyActionAsync(request.Id, CheckRequestWorkflowAction.ReceiptAcknowledged);

        await using var db = factory.CreateDbContext();
        Assert.Equal(4, await db.CheckRequestWorkflowEvents.CountAsync(x => x.CheckRequestId == request.Id));
        Assert.Equal(1, await db.RepresentativePayeeLedgerEntries.CountAsync(x => x.CheckRequestId == request.Id));
        var storedEntry = await db.RepresentativePayeeLedgerEntries.SingleAsync(x => x.CheckRequestId == request.Id);
        storedEntry.Amount = -999m;
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    private sealed class SingleConnectionContextFactory(DbContextOptions<SatiContext> options)
        : IDbContextFactory<SatiContext>
    {
        public SatiContext CreateDbContext() => new(options);
    }
}
