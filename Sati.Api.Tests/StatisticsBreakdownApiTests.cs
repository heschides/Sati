using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sati.Api.Data;
using Sati.Contracts.V1;
using Sati.Models;
using Xunit;

namespace Sati.Api.Tests;

[Collection(SatiApiCollection.Name)]
public sealed class StatisticsBreakdownApiTests(SatiApiFactory factory)
{
    private const string JulyReport =
        "/api/v1/reports/statistics-breakdown?start=2026-07-01&end=2026-07-31&period=Month";

    [Fact]
    public async Task ReportRequiresAuthenticationAndCurrentCaseManagementPermission()
    {
        using var anonymous = factory.CreateAnonymousClient();
        using var billingOnly = await factory.CreateAuthenticatedClientAsync("billing-only-one");
        using var caseManager = await factory.CreateAuthenticatedClientAsync("case-manager-one");

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(JulyReport)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await billingOnly.GetAsync(JulyReport)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await caseManager.GetAsync(JulyReport)).StatusCode);

        await factory.ChangeUserPermissionsAsync(12, UserPermissions.None);
        try
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await caseManager.GetAsync(JulyReport)).StatusCode);
        }
        finally
        {
            await factory.ChangeUserPermissionsAsync(12, UserPermissions.CaseManagement);
        }
    }

    [Fact]
    public async Task ReportExcludesOtherPeopleAndConflictingTenantMarkers()
    {
        using var client = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        var date = new DateTime(2198, 11, 10);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var ghostPerson = new ServerPerson
        {
            UserId = 12,
            AgencyId = 1,
            FirstName = "Ghost",
            LastName = "Consumer",
            BirthDate = new DateTime(1990, 1, 1),
            EffectiveDate = new DateTime(2025, 1, 1),
            Status = (int)PersonStatus.Ghost
        };
        db.People.Add(ghostPerson);
        await db.SaveChangesAsync();
        var notes = new[]
        {
            ReportNote(101, 1, date, 16),
            ReportNote(201, 2, date, 60),
            ReportNote(101, 2, date, 60),
            ReportNote(201, 1, date, 60),
            ReportNote(ghostPerson.Id, 1, date, 60)
        };
        notes[0].IsUnbilled = true;
        db.Notes.AddRange(notes);
        await db.SaveChangesAsync();

        try
        {
            var url = "/api/v1/reports/statistics-breakdown?start=2198-11-01&end=2198-11-30&period=Month";
            var response = await client.GetAsync(url);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var json = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("Narrative must not leave the report", json);
            var report = await response.Content.ReadFromJsonAsync<StatisticsBreakdownReportDto>();

            Assert.NotNull(report);
            Assert.Equal(2, report.Totals.DocumentedUnits);
            Assert.Equal(2, report.Totals.NonBillableUnits);
            Assert.Equal(0, report.Totals.BillableMarkedUnits);
            Assert.Equal(1, report.Totals.NonBillableServiceDays);
            Assert.Equal(0, report.Totals.BillableMarkedServiceDays);
            Assert.DoesNotContain(report.Clients, row => row.PersonId == 201);
            Assert.DoesNotContain(report.Clients, row => row.PersonId == ghostPerson.Id);
            Assert.Equal(2, Assert.Single(report.Clients, row => row.PersonId == 101).Metrics.DocumentedUnits);

            var selected = await client.GetFromJsonAsync<StatisticsBreakdownReportDto>(url + "&personId=101");
            Assert.Equal(101, Assert.Single(selected!.Clients).PersonId);
            Assert.Equal(2, selected.Totals.DocumentedUnits);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await client.GetAsync(url + "&personId=201")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await client.GetAsync(url + $"&personId={ghostPerson.Id}")).StatusCode);
        }
        finally
        {
            db.Notes.RemoveRange(notes);
            db.People.Remove(ghostPerson);
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task TransmittedClaimBreakdownUsesFrozenUnitsAndExcludesDraftAndForeignLines()
    {
        using var client = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        var mixedActivities = (int)(NoteActivity.Form | NoteActivity.Visit);
        var submittedDate = new DateTime(2198, 11, 10);
        var draftDate = new DateTime(2198, 12, 10);
        var submittedNote = ReportNote(101, 1, submittedDate, 16);
        submittedNote.Activities = mixedActivities;
        submittedNote.NoteType = (int)NoteType.Form;
        var draftNote = ReportNote(101, 1, draftDate, 16);
        draftNote.Activities = mixedActivities;
        draftNote.NoteType = (int)NoteType.Form;
        var foreignNote = ReportNote(201, 2, submittedDate, 60);
        foreignNote.Activities = mixedActivities;
        foreignNote.NoteType = (int)NoteType.Form;
        var mismatchedNote = ReportNote(101, 2, submittedDate, 60);
        mismatchedNote.Activities = mixedActivities;
        mismatchedNote.NoteType = (int)NoteType.Form;
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        db.Notes.AddRange(submittedNote, draftNote, foreignNote, mismatchedNote);
        await db.SaveChangesAsync();

        var submittedPeriod = new ServerBillingPeriod
        {
            UserId = 12,
            Year = 2198,
            Month = 11,
            Status = (int)BillingStatus.Submitted,
            SubmittedAt = new DateTime(2198, 12, 1)
        };
        var draftPeriod = new ServerBillingPeriod
        {
            UserId = 12,
            Year = 2198,
            Month = 12,
            Status = (int)BillingStatus.Draft
        };
        var submittedLine = Claim(submittedNote.Id, submittedDate, 1.07m);
        var foreignLine = Claim(foreignNote.Id, submittedDate, 99.99m);
        var mismatchedLine = Claim(mismatchedNote.Id, submittedDate, 88.88m);
        var draftLine = Claim(draftNote.Id, draftDate, 9.99m);
        submittedPeriod.Lines.Add(submittedLine);
        submittedPeriod.Lines.Add(foreignLine);
        submittedPeriod.Lines.Add(mismatchedLine);
        draftPeriod.Lines.Add(draftLine);
        db.BillingPeriods.AddRange(submittedPeriod, draftPeriod);
        await db.SaveChangesAsync();

        try
        {
            const string reportUrl =
                "/api/v1/reports/statistics-breakdown?start=2198-11-01&end=2198-12-31&period=Month&personId=101";
            var internalOnly = await client.GetFromJsonAsync<StatisticsBreakdownReportDto>(reportUrl);
            Assert.NotNull(internalOnly);
            Assert.Equal(0m, internalOnly.Totals.SubmittedClaimUnits);
            Assert.Equal(0, internalOnly.Totals.SubmittedClaimCount);

            var originalGeneration = Generation(submittedPeriod.Id, 1);
            var testGeneration = Generation(submittedPeriod.Id, 1, isTest: true);
            var correctionGeneration = Generation(submittedPeriod.Id, 1, isCorrection: true);
            var mismatchedGeneration = Generation(draftPeriod.Id, 1);
            db.EdiGenerations.AddRange(originalGeneration, testGeneration,
                correctionGeneration, mismatchedGeneration);
            await db.SaveChangesAsync();

            db.BillingSubmissionEvents.Add(ExchangeEvent(submittedPeriod.Id, 1,
                BillingSubmissionStage.Generated, generationId: originalGeneration.Id));
            await db.SaveChangesAsync();
            var generatedOnly = await client.GetFromJsonAsync<StatisticsBreakdownReportDto>(reportUrl);
            Assert.Equal(0m, generatedOnly!.Totals.SubmittedClaimUnits);

            db.BillingSubmissionEvents.AddRange(
                ExchangeEvent(submittedPeriod.Id, 1, BillingSubmissionStage.Transmitted,
                    synthetic: true, generationId: originalGeneration.Id),
                ExchangeEvent(submittedPeriod.Id, 2, BillingSubmissionStage.Transmitted,
                    generationId: originalGeneration.Id));
            await db.SaveChangesAsync();
            var syntheticOrWrongAgency = await client.GetFromJsonAsync<StatisticsBreakdownReportDto>(reportUrl);
            Assert.Equal(0m, syntheticOrWrongAgency!.Totals.SubmittedClaimUnits);

            db.BillingSubmissionEvents.AddRange(
                ExchangeEvent(submittedPeriod.Id, 1, BillingSubmissionStage.Transmitted,
                    generationId: testGeneration.Id),
                ExchangeEvent(submittedPeriod.Id, 1, BillingSubmissionStage.Transmitted,
                    generationId: correctionGeneration.Id),
                ExchangeEvent(submittedPeriod.Id, 1, BillingSubmissionStage.Transmitted,
                    generationId: mismatchedGeneration.Id),
                ExchangeEvent(submittedPeriod.Id, 1, BillingSubmissionStage.Transmitted));
            await db.SaveChangesAsync();
            var invalidGenerations = await client.GetFromJsonAsync<StatisticsBreakdownReportDto>(reportUrl);
            Assert.Equal(0m, invalidGenerations!.Totals.SubmittedClaimUnits);

            db.BillingSubmissionEvents.AddRange(
                ExchangeEvent(submittedPeriod.Id, 1, BillingSubmissionStage.Transmitted,
                    generationId: originalGeneration.Id),
                // A draft must remain excluded even if inconsistent exchange evidence exists.
                ExchangeEvent(draftPeriod.Id, 1, BillingSubmissionStage.Transmitted,
                    generationId: mismatchedGeneration.Id));
            await db.SaveChangesAsync();
            var report = await client.GetFromJsonAsync<StatisticsBreakdownReportDto>(reportUrl);

            Assert.NotNull(report);
            Assert.Equal(4, report.Totals.DocumentedUnits); // Two 16-minute notes.
            Assert.Equal(4, report.Totals.FormDocumentedUnits);
            Assert.Equal(4, report.Totals.VisitDocumentedUnits);
            Assert.Equal(1.07m, report.Totals.SubmittedClaimUnits);
            Assert.Equal(1.07m, report.Totals.SubmittedFormUnits);
            Assert.Equal(1.07m, report.Totals.SubmittedVisitUnits);
            Assert.Equal(1, report.Totals.SubmittedClaimCount);
            Assert.Collection(report.Periods,
                november =>
                {
                    Assert.Equal(11, november.Start.Month);
                    Assert.Equal(1.07m, november.Metrics.SubmittedClaimUnits);
                },
                december =>
                {
                    Assert.Equal(12, december.Start.Month);
                    Assert.Equal(2, december.Metrics.DocumentedUnits);
                    Assert.Equal(0m, december.Metrics.SubmittedClaimUnits);
                });

            db.BillingSubmissionEvents.Add(ExchangeEvent(submittedPeriod.Id, 1,
                BillingSubmissionStage.Transmitted, generationId: originalGeneration.Id));
            await db.SaveChangesAsync();
            var duplicateTransmission = await client.GetFromJsonAsync<StatisticsBreakdownReportDto>(reportUrl);
            Assert.Equal(1.07m, duplicateTransmission!.Totals.SubmittedClaimUnits);
            Assert.Equal(1, duplicateTransmission.Totals.SubmittedClaimCount);
        }
        finally
        {
            // Exchange events are append-only through tracked EF changes. Delete
            // fixture rows directly in this isolated test database before parents.
            await db.BillingSubmissionEvents.Where(item =>
                item.BillingPeriodId == submittedPeriod.Id ||
                item.BillingPeriodId == draftPeriod.Id).ExecuteDeleteAsync();
            await db.EdiGenerations.Where(item =>
                item.BillingPeriodId == submittedPeriod.Id ||
                item.BillingPeriodId == draftPeriod.Id).ExecuteDeleteAsync();
            db.ChangeTracker.Clear();
            db.ClaimLines.RemoveRange(submittedLine, foreignLine, mismatchedLine, draftLine);
            db.BillingPeriods.RemoveRange(submittedPeriod, draftPeriod);
            db.Notes.RemoveRange(submittedNote, draftNote, foreignNote, mismatchedNote);
            await db.SaveChangesAsync();
        }
    }

    private static ServerBillingSubmissionEvent ExchangeEvent(
        int periodId, int agencyId, BillingSubmissionStage stage, bool synthetic = false,
        long? generationId = null) => new()
    {
        BillingPeriodId = periodId,
        AgencyId = agencyId,
        Stage = stage,
        IsSynthetic = synthetic,
        EdiGenerationId = generationId,
        OccurredAtUtc = new DateTime(2198, 12, 15, 12, 0, 0, DateTimeKind.Utc)
    };

    private static ServerEdiGeneration Generation(int periodId, int agencyId,
        bool isTest = false, bool isCorrection = false) => new()
    {
        BillingPeriodId = periodId,
        AgencyId = agencyId,
        ActorUserId = 12,
        IdempotencyKey = Guid.NewGuid().ToString("N"),
        IsTest = isTest,
        IsCorrection = isCorrection,
        FileName = "statistics-fixture.837",
        Content = "Synthetic test fixture",
        CreatedAtUtc = new DateTime(2198, 12, 15, 11, 0, 0, DateTimeKind.Utc)
    };

    private static ServerClaimLine Claim(int noteId, DateTime date, decimal units) => new()
    {
        NoteId = noteId,
        DateOfService = date,
        ProcedureCode = "T1016",
        Units = units,
        ChargeAmount = units,
        ClientMaineCareId = "SYNTHETIC",
        RenderingProviderNpi = "1999999984",
        DiagnosisCode = "F89",
        PlaceOfService = 11
    };

    private static ServerNote ReportNote(int personId, int agencyId, DateTime date, int minutes) => new()
    {
        PersonId = personId,
        AgencyId = agencyId,
        Narrative = "Narrative must not leave the report",
        EventDate = date,
        Minutes = minutes,
        Status = 2
    };
}
