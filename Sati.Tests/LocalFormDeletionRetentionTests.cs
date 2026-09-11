using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Xunit;

namespace Sati.Tests;

public sealed class LocalFormDeletionRetentionTests
{
    private static readonly DateTime ServiceDate = new(2026, 9, 10);

    [Fact]
    public async Task AnUnattestedOverdueFormCannotBeDeletedToEraseItsBillingWindow()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var form = await AddFormAsync(fixture, FormType.PCP, ServiceDate.AddDays(-2));
        await AssertBlockedAsync(fixture, form.Id);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Service(fixture).DeleteFormsAsync([form]));
        Assert.Equal(FormRetentionRules.Message, error.Message);

        await AssertBlockedAsync(fixture, form.Id);
    }

    [Theory]
    [InlineData(FormType.PCP, 30, false)]
    [InlineData(FormType.PrivacyPractices, -30, false)]
    [InlineData(FormType.PCP, -30, true)]
    public async Task FutureOptionalAndCompletedRecordsAreAlsoRetained(FormType type, int dueOffset, bool completed)
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var due = ServiceDate.AddDays(dueOffset);
        var completedOn = completed ? (DateTime?)ServiceDate.AddDays(-1) : null;
        var form = await AddFormAsync(fixture, type, due, completedOn);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(fixture).DeleteFormsAsync([form]));

        await using var verification = fixture.Factory.CreateDbContext();
        var retained = await verification.Forms.AsNoTracking().SingleAsync(item => item.Id == form.Id);
        Assert.Equal(type, retained.Type);
        Assert.Equal(due, retained.DueDate);
        Assert.Equal(completedOn, retained.CompletedDate);
        Assert.Equal(fixture.PersonOneId, retained.PersonId);
    }

    [Fact]
    public async Task CallerMetadataCannotDisguiseTheStoredObligation()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var stored = await AddFormAsync(fixture, FormType.PCP, ServiceDate.AddDays(-2));
        var supplied = new Form(FormType.PrivacyPractices, ServiceDate.AddYears(1), ServiceDate)
        { Id = stored.Id, PersonId = fixture.PersonTwoId };

        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(fixture).DeleteFormsAsync([supplied]));
        await AssertBlockedAsync(fixture, stored.Id);
    }

    [Fact]
    public async Task AMixedOwnedAndForeignBatchCannotPartiallyDelete()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var owned = await AddFormAsync(fixture, FormType.PCP, ServiceDate.AddDays(-2));
        Form foreign;
        await using (var db = fixture.Factory.CreateDbContext())
        {
            var other = Person.CreatePerson(fixture.CaseManagerTwo.Id, "Foreign", "Synthetic", string.Empty,
                new DateTime(1990, 1, 1), null, WaiverType.Section21, new Settings());
            other.AgencyId = fixture.CaseManagerTwo.AgencyId;
            db.People.Add(other);
            await db.SaveChangesAsync();
            foreign = new Form(FormType.PCP, ServiceDate.AddDays(-3)) { PersonId = other.Id };
            db.Forms.Add(foreign);
            await db.SaveChangesAsync();
        }

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service(fixture).DeleteFormsAsync([owned, foreign]));
        await AssertBlockedAsync(fixture, owned.Id);
        await using var verification = fixture.Factory.CreateDbContext();
        Assert.True(await verification.Forms.AnyAsync(item => item.Id == foreign.Id));
    }

    [Theory]
    [InlineData("permissions")]
    [InlineData("agency")]
    [InlineData("role")]
    public async Task StaleSessionCannotReachTheOperationEvenWithAnEmptyBatch(string changed)
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var service = Service(fixture);
        await using (var db = fixture.Factory.CreateDbContext())
        {
            var persisted = await db.Users.SingleAsync(item => item.Id == fixture.CaseManagerOne.Id);
            if (changed == "permissions") persisted.Permissions = UserPermissions.Billing;
            if (changed == "agency") persisted.AgencyId = fixture.CaseManagerTwo.AgencyId;
            if (changed == "role") persisted.Role = UserRole.Admin;
            await db.SaveChangesAsync();
        }

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.DeleteFormsAsync([]));
    }

    [Fact]
    public async Task EmptySelectionIsANoOpForACurrentAuthorizedUser()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        await Service(fixture).DeleteFormsAsync([]);
        await using var db = fixture.Factory.CreateDbContext();
        Assert.Empty(await db.Forms.ToListAsync());
    }

    [Fact]
    public async Task AnOversizedBatchIsRejectedWithoutReadingOrDeletingIndividualForms()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var forms = Enumerable.Range(1, 101).Select(id => new Form(FormType.PCP, ServiceDate) { Id = id });
        await Assert.ThrowsAsync<ArgumentException>(() => Service(fixture).DeleteFormsAsync(forms));
    }

    private static FormService Service(NoteEntryFixture fixture)
    {
        var session = new SessionService();
        session.SetUser(fixture.CaseManagerOne);
        return new FormService(fixture.Factory, session);
    }

    private static async Task<Form> AddFormAsync(NoteEntryFixture fixture, FormType type, DateTime due, DateTime? completed = null)
    {
        await using var db = fixture.Factory.CreateDbContext();
        var form = new Form(type, due, completed) { PersonId = fixture.PersonOneId };
        db.Forms.Add(form);
        await db.SaveChangesAsync();
        return form;
    }

    private static async Task AssertBlockedAsync(NoteEntryFixture fixture, int formId)
    {
        await using var db = fixture.Factory.CreateDbContext();
        var person = await db.People.Include(item => item.Forms).SingleAsync(item => item.Id == fixture.PersonOneId);
        var retained = Assert.Single(person.Forms, item => item.Id == formId);
        Assert.Equal(FormType.PCP, retained.Type);
        Assert.Equal(ServiceDate.AddDays(-2), retained.DueDate);
        Assert.Null(retained.CompletedDate);
        Assert.False(person.EvaluateComplianceGate(ServiceDate).Passed);
        Assert.Contains(person.EvaluateBillingWindow(ServiceDate), reason => reason.Contains("PCP", StringComparison.Ordinal));
    }
}
