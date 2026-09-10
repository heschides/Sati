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

    public CheckRequestTests()
    {
        connection.Open();
        var options = new DbContextOptionsBuilder<SatiContext>().UseSqlite(connection).Options;
        using var db = new SatiContext(options);
        db.Database.EnsureCreated();
        var agency = db.Agencies.Single(x => x.Id == 1);
        agency.Name = "Woodfords Family Services";
        var supervisor = User.Create(0, "supervisor", "Christine Campbell", "", "", UserRole.Supervisor, null, agency.Id);
        supervisor.Agency = agency;
        db.Users.Add(supervisor);
        db.SaveChanges();
        var owner = User.Create(0, "case.manager", "Joshua White", "", "", UserRole.CaseManager, supervisor.Id, agency.Id);
        owner.Agency = agency;
        owner.Supervisor = supervisor;
        db.Users.Add(owner);
        db.SaveChanges();
        var person = Person.Rehydrate(0, owner.Id);
        person.FirstName = "Sample";
        person.LastName = "Consumer";
        person.AgencyId = agency.Id;
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

    private sealed class SingleConnectionContextFactory(DbContextOptions<SatiContext> options)
        : IDbContextFactory<SatiContext>
    {
        public SatiContext CreateDbContext() => new(options);
    }
}
