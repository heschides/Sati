using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Sati.Api.Data;
using Sati.Api.Infrastructure;
using Sati.Contracts.V1;
using Sati.Models.Billing;
using Xunit;

namespace Sati.Api.Tests;

public sealed partial class ClearinghouseDispatchApiTests
{
    [Theory]
    [InlineData(false, "form")]
    [InlineData(true, "form")]
    [InlineData(false, "status")]
    [InlineData(true, "status")]
    public async Task ChangedComplianceHoldsTheRetainedOriginalAtQueueAndBeforeSending(bool alreadyQueued, string change)
    {
        await using var fixture = await Fixture.CreateAsync();
        var file = await fixture.GenerateAsync(fixture.AccountId);
        var queuePath = "/api/v1/billing/clearinghouse/dispatches";
        var request = new QueueClearinghouseDispatchRequest(fixture.GenerationId, fixture.AccountId);
        if (alreadyQueued) (await fixture.Biller.PostAsJsonAsync(queuePath, request)).EnsureSuccessStatusCode();
        await using var db = fixture.Factory.OpenDatabase();
        var line = await db.ClaimLines.FirstAsync(row => row.BillingPeriodId == fixture.PeriodId);
        var note = await db.Notes.SingleAsync(row => row.Id == line.NoteId);
        if (change == "form")
        {
            db.Forms.Add(new ServerForm
            {
                PersonId = note.PersonId, Type = "PCP", TargetEffectiveDate = line.DateOfService.AddDays(-1).Date,
                DueDate = line.DateOfService.AddDays(-1), CompletedDate = null
            });
            await db.SaveChangesAsync();
        }
        else await db.Notes.Where(row => row.Id == note.Id).ExecuteUpdateAsync(set => set.SetProperty(row => row.Status, (int?)2));
        var retained = await db.EdiGenerations.SingleAsync(row => row.Id == fixture.GenerationId);
        var counts = (await db.EdiGenerations.CountAsync(), await db.BillingSubmissionEvents.CountAsync(), await db.AuditEvents.CountAsync());
        if (alreadyQueued)
        {
            var connector = new CountedOutcomeConnector(ClearinghouseAttemptOutcome.Accepted);
            Assert.True(await fixture.Worker(connector).ProcessOneAsync(CancellationToken.None));
            Assert.Equal(0, connector.Calls);
            var dispatch = await db.ClearinghouseDispatches.AsNoTracking().SingleAsync();
            Assert.Equal(ClearinghouseDispatchState.CancelledBeforeSend, dispatch.State);
            Assert.Equal("billing_release_blocked", dispatch.SafeErrorCode);
            Assert.Empty(await db.ClearinghouseDispatchAttempts.ToListAsync());
        }
        else
        {
            var refused = await fixture.Biller.PostAsJsonAsync(queuePath, request);
            Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
            Assert.Equal("billing_release_blocked", (await refused.Content.ReadFromJsonAsync<ApiErrorDto>())!.Code);
            Assert.Empty(await db.ClearinghouseDispatches.ToListAsync());
        }
        Assert.Equal(file.Content, retained.Content);
        Assert.Equal(counts, (await db.EdiGenerations.CountAsync(), await db.BillingSubmissionEvents.CountAsync(), await db.AuditEvents.CountAsync()));
    }

    [Theory]
    [InlineData(ClaimCorrectionAction.Resubmit, true, false)]
    [InlineData(ClaimCorrectionAction.Resubmit, true, true)]
    [InlineData(ClaimCorrectionAction.Resubmit, false, false)]
    [InlineData(ClaimCorrectionAction.Replace, true, false)]
    [InlineData(ClaimCorrectionAction.Replace, true, true)]
    [InlineData(ClaimCorrectionAction.Replace, false, false)]
    [InlineData(ClaimCorrectionAction.Void, true, false)]
    [InlineData(ClaimCorrectionAction.Void, true, true)]
    public async Task CorrectionReleaseUsesTheExactSubsetAndAllowsStandingClaimWithdrawal(
        ClaimCorrectionAction action, bool blockSelectedClaim, bool alreadyQueued)
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.GenerateAsync(fixture.AccountId);
        var scenario = action switch
        {
            ClaimCorrectionAction.Resubmit => MockClearinghouseScenario.SyntaxRejected,
            ClaimCorrectionAction.Replace => MockClearinghouseScenario.Denied,
            _ => MockClearinghouseScenario.Accepted
        };
        (await fixture.Biller.PostAsJsonAsync($"/api/v1/billing/periods/{fixture.PeriodId}/mock-clearinghouse",
            new MockClearinghouseRequest(scenario))).EnsureSuccessStatusCode();
        await using var db = fixture.Factory.OpenDatabase();
        var lines = await db.ClaimLines.Where(row => row.BillingPeriodId == fixture.PeriodId).OrderBy(row => row.Id).ToListAsync();
        (await fixture.Biller.PostAsJsonAsync($"/api/v1/billing/periods/{fixture.PeriodId}/corrections",
            new CreateClaimCorrectionRequest(lines[0].Id, action, "Synthetic correction purpose."))).EnsureSuccessStatusCode();
        var generated = await fixture.Biller.PostAsJsonAsync($"/api/v1/billing/periods/{fixture.PeriodId}/corrections/edi",
            new GenerateEdiRequest(true, Guid.NewGuid().ToString("N")) { ClearinghouseAccountId = fixture.AccountId });
        generated.EnsureSuccessStatusCode();
        var file = (await generated.Content.ReadFromJsonAsync<EdiFileDto>())!;
        var generationId = await db.EdiGenerations.MaxAsync(row => row.Id);
        var queue = new QueueClearinghouseDispatchRequest(generationId, fixture.AccountId);
        if (alreadyQueued) (await fixture.Biller.PostAsJsonAsync("/api/v1/billing/clearinghouse/dispatches", queue)).EnsureSuccessStatusCode();
        var selectedLine = lines[blockSelectedClaim ? 0 : 1];
        var personId = await db.Notes.Where(row => row.Id == selectedLine.NoteId).Select(row => row.PersonId).SingleAsync();
        db.Forms.Add(new ServerForm
        {
            PersonId = personId, Type = "PCP", TargetEffectiveDate = selectedLine.DateOfService.AddDays(-1).Date,
            DueDate = selectedLine.DateOfService.AddDays(-1)
        });
        await db.SaveChangesAsync();
        var shouldHold = blockSelectedClaim && action != ClaimCorrectionAction.Void;
        var connector = new CountedOutcomeConnector(ClearinghouseAttemptOutcome.Accepted);
        if (!alreadyQueued)
        {
            var response = await fixture.Biller.PostAsJsonAsync("/api/v1/billing/clearinghouse/dispatches", queue);
            Assert.Equal(shouldHold ? HttpStatusCode.Conflict : HttpStatusCode.OK, response.StatusCode);
            if (shouldHold) Assert.Empty(await db.ClearinghouseDispatches.ToListAsync());
        }
        if (alreadyQueued || !shouldHold)
        {
            Assert.True(await fixture.Worker(connector).ProcessOneAsync(CancellationToken.None));
            Assert.Equal(shouldHold ? 0 : 1, connector.Calls);
            Assert.Equal(shouldHold ? ClearinghouseDispatchState.CancelledBeforeSend : ClearinghouseDispatchState.AcceptedByClearinghouse,
                (await db.ClearinghouseDispatches.AsNoTracking().SingleAsync()).State);
        }
        Assert.Equal(file.Content, (await db.EdiGenerations.AsNoTracking().SingleAsync(row => row.Id == generationId)).Content);
    }

    [Fact]
    public async Task LateAdjudicationRevokesAnUnsentResubmissionAction()
    {
        await using var fixture = await Fixture.CreateAsync();
        var original = await fixture.GenerateAsync(fixture.AccountId);
        (await fixture.Biller.PostAsJsonAsync($"/api/v1/billing/periods/{fixture.PeriodId}/mock-clearinghouse",
            new MockClearinghouseRequest(MockClearinghouseScenario.SyntaxRejected))).EnsureSuccessStatusCode();
        await using var db = fixture.Factory.OpenDatabase();
        var line = await db.ClaimLines.FirstAsync(row => row.BillingPeriodId == fixture.PeriodId);
        (await fixture.Biller.PostAsJsonAsync($"/api/v1/billing/periods/{fixture.PeriodId}/corrections",
            new CreateClaimCorrectionRequest(line.Id, ClaimCorrectionAction.Resubmit, "Synthetic resubmission."))).EnsureSuccessStatusCode();
        (await fixture.Biller.PostAsJsonAsync($"/api/v1/billing/periods/{fixture.PeriodId}/corrections/edi",
            new GenerateEdiRequest(true, Guid.NewGuid().ToString("N")) { ClearinghouseAccountId = fixture.AccountId })).EnsureSuccessStatusCode();
        var generationId = await db.EdiGenerations.MaxAsync(row => row.Id);
        var late = Sati.Api.Infrastructure.MockClearinghouse.Respond(original.Content, MockClearinghouseScenario.Accepted, DateTime.UtcNow);
        (await fixture.Biller.PostAsJsonAsync("/api/v1/billing/responses", new ClaimResponseIngestRequest(late.RemittanceAdvice!))).EnsureSuccessStatusCode();
        var refused = await fixture.Biller.PostAsJsonAsync("/api/v1/billing/clearinghouse/dispatches",
            new QueueClearinghouseDispatchRequest(generationId, fixture.AccountId));
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Empty(await db.ClearinghouseDispatches.ToListAsync());
    }

    [Fact]
    public async Task GeneratedOnlyRenderDoesNotSupersedeTheClaimThatNeedsCorrection()
    {
        await using var fixture = await Fixture.CreateAsync();
        var original = await fixture.GenerateAsync(fixture.AccountId);
        var deliveredId = fixture.GenerationId;
        await fixture.GenerateAsync(fixture.AccountId); // harmless alternative render, never queued
        (await fixture.Biller.PostAsJsonAsync("/api/v1/billing/clearinghouse/dispatches",
            new QueueClearinghouseDispatchRequest(deliveredId, fixture.AccountId))).EnsureSuccessStatusCode();
        Assert.True(await fixture.Worker(new SyntheticClearinghouseConnector()).ProcessOneAsync(CancellationToken.None));
        var rejected = MockClearinghouse.Respond(original.Content, MockClearinghouseScenario.SyntaxRejected, DateTime.UtcNow);
        (await fixture.Biller.PostAsJsonAsync("/api/v1/billing/responses", new ClaimResponseIngestRequest(rejected.FunctionalAcknowledgement!))).EnsureSuccessStatusCode();
        await using var db = fixture.Factory.OpenDatabase();
        var line = await db.ClaimLines.FirstAsync(row => row.BillingPeriodId == fixture.PeriodId);
        var corrected = await fixture.Biller.PostAsJsonAsync($"/api/v1/billing/periods/{fixture.PeriodId}/corrections",
            new CreateClaimCorrectionRequest(line.Id, ClaimCorrectionAction.Resubmit, "Synthetic valid resubmission."));
        Assert.Equal(HttpStatusCode.OK, corrected.StatusCode);
        Assert.Equal(deliveredId, (await db.ClaimCorrections.SingleAsync()).CorrectsEdiGenerationId);
    }
}
