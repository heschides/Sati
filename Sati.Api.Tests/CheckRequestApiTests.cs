using System.Net;
using System.Net.Http.Json;
using Sati.Contracts.V1;
using Xunit;

namespace Sati.Api.Tests;

[Collection(SatiApiCollection.Name)]
public sealed class CheckRequestApiTests(SatiApiFactory factory)
{
    [Fact]
    public async Task AnotherAgencysCheckRequestIsHidden()
    {
        using var client = await factory.CreateAuthenticatedClientAsync("case-manager-one");

        var response = await client.GetAsync("/api/v1/check-requests/2002");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task SupervisorCanReadButCannotRewriteACaseManagersRequest()
    {
        using var supervisor = await factory.CreateAuthenticatedClientAsync("supervisor-one");
        var current = await supervisor.GetFromJsonAsync<CheckRequestDto>("/api/v1/check-requests/2001");

        var response = await supervisor.PutAsJsonAsync("/api/v1/check-requests/2001", Complete(current!, 90m));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PublishingUsesTheAuthenticatedOwnerAndLocksTheFinancialRecord()
    {
        using var owner = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        var current = await owner.GetFromJsonAsync<CheckRequestDto>("/api/v1/check-requests/2001");
        Assert.NotNull(current);

        var publishResponse = await owner.PostAsJsonAsync(
            "/api/v1/check-requests/2001/publish", Complete(current!, 125.40m));
        Assert.Equal(HttpStatusCode.OK, publishResponse.StatusCode);
        var published = await publishResponse.Content.ReadFromJsonAsync<CheckRequestDto>();
        Assert.Equal("case-manager-one", published!.PublishedByName);
        Assert.NotNull(published.PublishedAtUtc);

        var rewrite = await owner.PutAsJsonAsync(
            "/api/v1/check-requests/2001", Complete(published, 999m));
        Assert.Equal(HttpStatusCode.Conflict, rewrite.StatusCode);
        var stored = await owner.GetFromJsonAsync<CheckRequestDto>("/api/v1/check-requests/2001");
        Assert.Equal(125.40m, stored!.Amount);
    }

    [Fact]
    public async Task SubmittedRequestRequiresSupervisorApprovalBeforeFinanceCanReleaseIt()
    {
        using var owner = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        using var supervisor = await factory.CreateAuthenticatedClientAsync("supervisor-one");
        using var finance = await factory.CreateAuthenticatedClientAsync("finance-one");

        var createdResponse = await owner.PostAsJsonAsync(
            "/api/v1/check-requests", new CreateCheckRequestRequest(101));
        createdResponse.EnsureSuccessStatusCode();
        var created = await createdResponse.Content.ReadFromJsonAsync<CheckRequestDto>();
        var publishResponse = await owner.PostAsJsonAsync(
            $"/api/v1/check-requests/{created!.Id}/publish", Complete(created, 143.25m));
        publishResponse.EnsureSuccessStatusCode();
        var prepared = await publishResponse.Content.ReadFromJsonAsync<CheckRequestDto>();
        Assert.Equal(CheckRequestWorkflowStatus.Prepared, prepared!.WorkflowStatus);

        var prematureFinanceQueue = await finance.GetFromJsonAsync<List<CheckRequestWorkflowQueueItemDto>>(
            "/api/v1/representative-payee/check-requests");
        Assert.DoesNotContain(prematureFinanceQueue!, item => item.CheckRequestId == created.Id);

        var submit = await owner.PostAsJsonAsync($"/api/v1/check-requests/{created.Id}/workflow",
            new ApplyCheckRequestWorkflowActionRequest(CheckRequestWorkflowAction.Submitted, "Ready for review"));
        submit.EnsureSuccessStatusCode();
        var supervisorQueue = await supervisor.GetFromJsonAsync<List<CheckRequestWorkflowQueueItemDto>>(
            "/api/v1/check-requests/supervisor-queue");
        Assert.Contains(supervisorQueue!, item => item.CheckRequestId == created.Id);

        var approve = await supervisor.PostAsJsonAsync($"/api/v1/check-requests/{created.Id}/workflow",
            new ApplyCheckRequestWorkflowActionRequest(CheckRequestWorkflowAction.Approved, "Approved for Finance"));
        approve.EnsureSuccessStatusCode();
        var financeQueue = await finance.GetFromJsonAsync<List<CheckRequestWorkflowQueueItemDto>>(
            "/api/v1/representative-payee/check-requests");
        Assert.Contains(financeQueue!, item => item.CheckRequestId == created.Id &&
            item.Status == CheckRequestWorkflowStatus.Approved);

        var release = await finance.PostAsJsonAsync($"/api/v1/check-requests/{created.Id}/workflow",
            new ApplyCheckRequestWorkflowActionRequest(CheckRequestWorkflowAction.Released, "Released"));
        release.EnsureSuccessStatusCode();
        var workspace = await finance.GetFromJsonAsync<RepresentativePayeeWorkspaceDto>(
            "/api/v1/representative-payee/consumers/101/ledger");
        var debit = Assert.Single(workspace!.Entries, entry => entry.CheckRequestId == created.Id);
        Assert.Equal(-143.25m, debit.Amount);
        Assert.Equal(RepresentativePayeeLedgerEntryKind.CheckRelease, debit.Kind);

        var duplicateRelease = await finance.PostAsJsonAsync($"/api/v1/check-requests/{created.Id}/workflow",
            new ApplyCheckRequestWorkflowActionRequest(CheckRequestWorkflowAction.Released, "Again"));
        Assert.Equal(HttpStatusCode.Conflict, duplicateRelease.StatusCode);

        var receipt = await finance.PostAsJsonAsync($"/api/v1/check-requests/{created.Id}/workflow",
            new ApplyCheckRequestWorkflowActionRequest(CheckRequestWorkflowAction.ReceiptAcknowledged, "Receipt confirmed"));
        receipt.EnsureSuccessStatusCode();
        var completed = await receipt.Content.ReadFromJsonAsync<CheckRequestWorkflowQueueItemDto>();
        Assert.Equal(CheckRequestWorkflowStatus.ReceiptAcknowledged, completed!.Status);
    }

    [Fact]
    public async Task BillingPermissionAloneCannotOpenRepresentativePayeeRecords()
    {
        using var billing = await factory.CreateAuthenticatedClientAsync("billing-only-one");

        var consumers = await billing.GetAsync("/api/v1/representative-payee/consumers");
        var queue = await billing.GetAsync("/api/v1/representative-payee/check-requests");
        var timeOff = await billing.GetAsync(
            $"/api/v1/check-requests/time-off-collisions?date={DateTime.Today.AddDays(1):yyyy-MM-dd}");
        var prepareTimeOff = await billing.PostAsJsonAsync(
            "/api/v1/check-requests/time-off-drafts/ensure",
            new EnsureTimeOffCheckRequestDraftsRequest(DateTime.Today.AddDays(1)));

        Assert.Equal(HttpStatusCode.Forbidden, consumers.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, queue.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, timeOff.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, prepareTimeOff.StatusCode);
    }

    [Fact]
    public async Task WeeklyDefaultGeneratesOneReviewDraftAndStopsPromptingAfterSubmission()
    {
        using var owner = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        var today = DateTime.Today;
        var save = await owner.PutAsJsonAsync(
            "/api/v1/people/101/check-request-template",
            new SaveCheckRequestTemplateRequest(
                0, true, today.DayOfWeek, 4,
                "Weekly Vendor", "20 Sample Street", 51.25m, "Weekly support"));
        save.EnsureSuccessStatusCode();
        var template = await save.Content.ReadFromJsonAsync<CheckRequestTemplateDto>();
        Assert.Equal(1, template!.Revision);

        var firstResponse = await owner.PostAsJsonAsync(
            "/api/v1/check-requests/weekly-drafts/ensure", new { });
        firstResponse.EnsureSuccessStatusCode();
        var first = await firstResponse.Content.ReadFromJsonAsync<WeeklyCheckRequestDraftResultDto>();
        var generated = Assert.Single(first!.PendingDrafts);
        Assert.Equal(1, first.CreatedCount);
        Assert.Equal(101, generated.PersonId);
        Assert.Equal(51.25m, generated.Amount);

        var secondResponse = await owner.PostAsJsonAsync(
            "/api/v1/check-requests/weekly-drafts/ensure", new { });
        var second = await secondResponse.Content.ReadFromJsonAsync<WeeklyCheckRequestDraftResultDto>();
        Assert.Equal(0, second!.CreatedCount);
        Assert.Single(second.PendingDrafts);

        var request = await owner.GetFromJsonAsync<CheckRequestDto>(
            $"/api/v1/check-requests/{generated.CheckRequestId}");
        Assert.Equal(template.Id, request!.TemplateId);
        Assert.Equal(generated.ScheduledForDate, request.ScheduledForDate);
        var publish = await owner.PostAsJsonAsync(
            $"/api/v1/check-requests/{request.Id}/publish",
            new SaveCheckRequestRequest(
                request.RequestDate, request.PayableTo, request.MailingAddress,
                request.Amount, request.NeededByDate, request.Reason, request.Revision));
        publish.EnsureSuccessStatusCode();
        var submit = await owner.PostAsJsonAsync(
            $"/api/v1/check-requests/{request.Id}/workflow",
            new ApplyCheckRequestWorkflowActionRequest(
                CheckRequestWorkflowAction.Submitted, "Reviewed and submitted"));
        submit.EnsureSuccessStatusCode();

        var pending = await owner.GetFromJsonAsync<List<GeneratedCheckRequestDraftDto>>(
            "/api/v1/check-requests/generated-drafts/pending");
        Assert.DoesNotContain(pending!, item => item.CheckRequestId == request.Id);

        var timeOffDate = today.AddDays(3);
        var updateTemplate = await owner.PutAsJsonAsync(
            "/api/v1/people/101/check-request-template",
            new SaveCheckRequestTemplateRequest(
                template.Revision, true, timeOffDate.DayOfWeek, 2,
                "Time-off Vendor", "30 Sample Street", 72m, "Prepare before leave"));
        updateTemplate.EnsureSuccessStatusCode();
        using var addTimeOff = await owner.PostAsJsonAsync(
            "/api/v1/exempt-dates",
            new AddExemptDateRequest(timeOffDate, "Scheduled time off"));
        addTimeOff.EnsureSuccessStatusCode();

        var collisions = await owner.GetFromJsonAsync<List<TimeOffCheckRequestCollisionDto>>(
            $"/api/v1/check-requests/time-off-collisions?date={timeOffDate:yyyy-MM-dd}");
        var collision = Assert.Single(collisions!);
        Assert.Equal(101, collision.PersonId);
        Assert.Null(collision.PendingCheckRequestId);

        var timeOffEnsure = await owner.PostAsJsonAsync(
            "/api/v1/check-requests/time-off-drafts/ensure",
            new EnsureTimeOffCheckRequestDraftsRequest(timeOffDate));
        timeOffEnsure.EnsureSuccessStatusCode();
        var timeOffResult = await timeOffEnsure.Content
            .ReadFromJsonAsync<WeeklyCheckRequestDraftResultDto>();
        var timeOffDraft = Assert.Single(timeOffResult!.PendingDrafts);
        Assert.Equal(1, timeOffResult.CreatedCount);
        Assert.Equal(timeOffDate.Date, timeOffDraft.ScheduledForDate);

        var duplicateTimeOffEnsure = await owner.PostAsJsonAsync(
            "/api/v1/check-requests/time-off-drafts/ensure",
            new EnsureTimeOffCheckRequestDraftsRequest(timeOffDate));
        var duplicateTimeOff = await duplicateTimeOffEnsure.Content
            .ReadFromJsonAsync<WeeklyCheckRequestDraftResultDto>();
        Assert.Equal(0, duplicateTimeOff!.CreatedCount);
        Assert.Single(duplicateTimeOff.PendingDrafts);
    }

    [Fact]
    public async Task WeeklyDefaultIsLimitedToTheAssignedCaseManager()
    {
        using var otherAgency = await factory.CreateAuthenticatedClientAsync("case-manager-two");

        var read = await otherAgency.GetAsync("/api/v1/people/101/check-request-template");
        var write = await otherAgency.PutAsJsonAsync(
            "/api/v1/people/101/check-request-template",
            new SaveCheckRequestTemplateRequest(
                0, true, DayOfWeek.Monday, 2,
                "Hidden Vendor", "Hidden address", 10m, "Hidden reason"));

        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, write.StatusCode);
    }

    private static SaveCheckRequestRequest Complete(CheckRequestDto request, decimal amount) => new(
        request.RequestDate,
        "Example Vendor",
        "10 Main Street, Augusta, ME 04330",
        amount,
        new DateTime(2026, 9, 20),
        "Synthetic accessibility supplies",
        request.Revision);
}
