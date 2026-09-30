using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sati.Api.Data;
using Sati.Contracts.V1;
using Xunit;

namespace Sati.Api.Tests;

public sealed class AssessmentAvailabilityApiTests
{
    private static readonly DateTime OpenBy = new(2026, 9, 22);
    private static readonly DateTime CompleteBy = new(2026, 10, 22);
    private static readonly DateTime TargetEffectiveDate = new(2027, 1, 20);

    [Fact]
    public async Task LegacyZeroWindowAllowsOpeningAndCompletionOnAssessmentOpenByDate()
    {
        using var factory = CreateFactory();
        using var owner = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        var (personId, formId) = await CreateAssessmentAsync(factory);

        using var opened = await owner.PostAsJsonAsync(
            $"/api/v1/forms/{formId}/open", new OpenFormRequest(OpenBy));
        Assert.True(opened.IsSuccessStatusCode, await opened.Content.ReadAsStringAsync());

        using var completed = await owner.PostAsJsonAsync(
            $"/api/v1/people/{personId}/forms/ComprehensiveAssessment/attestation",
            new AttestFormRequest(formId, OpenBy));
        Assert.True(completed.IsSuccessStatusCode, await completed.Content.ReadAsStringAsync());

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var saved = await db.Forms.AsNoTracking().SingleAsync(form => form.Id == formId);
        Assert.Equal(OpenBy, saved.OpenedDate);
        Assert.Equal(OpenBy, saved.CompletedDate);
        Assert.Equal(CompleteBy, saved.DueDate);
        Assert.Equal(TargetEffectiveDate, saved.TargetEffectiveDate);
        var attestation = await db.FormAttestations.AsNoTracking()
            .SingleAsync(row => row.FormId == formId);
        Assert.Equal(OpenBy, attestation.CompletedOn);
        var evidenceNote = await db.Notes.AsNoTracking()
            .SingleAsync(note => note.FormId == formId);
        Assert.Equal(OpenBy, evidenceNote.EventDate);
    }

    [Theory]
    [InlineData(21, "Sep 22, 2026")]
    [InlineData(30, "future")]
    public async Task LegacyZeroWindowStillRejectsDatesBeforeAvailabilityAndInTheFuture(
        int septemberDay,
        string expectedError)
    {
        using var factory = CreateFactory();
        using var owner = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        var (personId, formId) = await CreateAssessmentAsync(factory);
        var actualDate = new DateTime(2026, 9, septemberDay);

        using var opened = await owner.PostAsJsonAsync(
            $"/api/v1/forms/{formId}/open", new OpenFormRequest(actualDate));
        Assert.Equal(HttpStatusCode.BadRequest, opened.StatusCode);
        Assert.Contains(expectedError, await opened.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);

        using var completed = await owner.PostAsJsonAsync(
            $"/api/v1/people/{personId}/forms/ComprehensiveAssessment/attestation",
            new AttestFormRequest(formId, actualDate));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, completed.StatusCode);
        Assert.Contains(expectedError, await completed.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var saved = await db.Forms.AsNoTracking().SingleAsync(form => form.Id == formId);
        Assert.Null(saved.OpenedDate);
        Assert.Null(saved.CompletedDate);
        Assert.False(await db.FormAttestations.AnyAsync(row => row.FormId == formId));
        Assert.False(await db.Notes.AnyAsync(note => note.FormId == formId));
    }

    private static SatiApiFactory CreateFactory() => new()
    {
        ClockOverride = new FrozenTimeProvider(
            new DateTimeOffset(2026, 9, 29, 16, 0, 0, TimeSpan.Zero))
    };

    private static async Task<(int PersonId, int FormId)> CreateAssessmentAsync(
        SatiApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var settings = await db.Settings.SingleAsync(row => row.AgencyId == 1);
        settings.CompAssessmentOpenDaysBefore = 0;
        var personId = await db.People.MaxAsync(person => person.Id) + 1;
        var form = new ServerForm
        {
            Type = "ComprehensiveAssessment",
            DueDate = CompleteBy,
            TargetEffectiveDate = TargetEffectiveDate
        };
        db.People.Add(new ServerPerson
        {
            Id = personId,
            UserId = 12,
            AgencyId = 1,
            FirstName = "Assessment",
            LastName = "Availability Fixture",
            BirthDate = new DateTime(1990, 1, 1),
            EffectiveDate = TargetEffectiveDate.AddYears(-1),
            IsTestData = true,
            CreatedAtUtc = DateTime.UtcNow,
            Forms = [form]
        });
        await db.SaveChangesAsync();
        return (personId, form.Id);
    }

    private sealed class FrozenTimeProvider(DateTimeOffset instant) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => instant;
    }
}
