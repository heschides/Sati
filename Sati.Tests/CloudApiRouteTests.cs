using System.Net;
using System.Net.Http;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Data.Cloud;
using Sati.Models;
using Xunit;

namespace Sati.Tests;

/// <summary>
/// The URL each cloud service actually requests.
///
/// Written after a live failure: the SSN, DHHS form, and agency-release routes were
/// built with bare paths like <c>people/1210/forms.pdf</c> while the API base address
/// is only the host, so every service must supply <c>/api/v1/</c> itself. The result
/// was not a clean error â€” App Service answered the GET with its own HTML page and
/// HTTP 200, which surfaced as a JSON parse failure, and the POST 404'd and was
/// reported to the case manager as "the record was not found or is outside your
/// caseload". A wrong URL and a genuinely missing consumer are indistinguishable at
/// the call site, which is why this is asserted here rather than left to a run-time
/// symptom.
///
/// New cloud services belong here. The mistake is invisible in review â€” a bare path
/// looks exactly like a correct one â€” and its symptom points at the wrong thing.
/// </summary>
public sealed class CloudApiRouteTests
{
    private const int PersonId = 1210;

    [Fact]
    public async Task BillingOverviewRequestsTheBoundedAggregateRoute()
    {
        var recorder = new UriRecorder(JsonBody(
            """{"draftRevenue":12.50,"months":[{"year":2026,"month":9,"billedAmount":8.25}]}"""));
        var service = new CloudBillingService(ClientFor(recorder));

        var overview = await service.GetBillingPeriodOverviewAsync(
            new AgencyActor(7, 1, UserPermissions.Billing, 1),
            new DateTime(2026, 9, 24));

        Assert.Equal(12.50m, overview.DraftRevenue);
        Assert.Equal(8.25m, Assert.Single(overview.Months).BilledAmount);
        Assert.Equal(
            "https://api.invalid/api/v1/billing/overview-periods/2026/9",
            recorder.LastUri?.ToString());
    }

    [Theory]
    [InlineData("null")]
    [InlineData("")]
    public async Task MissingSafetyPlanIsAnEmptyWorkspaceNotAFailedRequest(string body)
    {
        var recorder = new UriRecorder(JsonBody(body));
        var service = new CloudSafetyPlanService(ClientFor(recorder), new Sati.Data.SessionService());
        Assert.Null(await service.GetAsync(PersonId, new DateTime(2026, 9, 1)));
        Assert.Equal($"/api/v1/people/{PersonId}/safety-plans/latest", recorder.LastUri?.AbsolutePath);
    }

    [Fact]
    public async Task DownloadedSafetyPlanKeepsTheServersDraftFileName()
    {
        var content = new ByteArrayContent([1, 2, 3]);
        content.Headers.ContentDisposition = new("attachment") { FileNameStar = "Safety-Plan-DRAFT-1210.pdf" };
        var recorder = new UriRecorder(content);
        var service = new CloudSafetyPlanService(ClientFor(recorder), new Sati.Data.SessionService());
        var result = await service.GenerateAsync(PersonId, DateTime.Today);
        Assert.Equal("Safety-Plan-DRAFT-1210.pdf", result.FileName);
        Assert.Equal($"/api/v1/people/{PersonId}/documents/SafetyPlan", recorder.LastUri?.AbsolutePath);
    }

    [Fact]
    public async Task GeneratingAFormRequestsTheVersionedRoute()
    {
        var recorder = new UriRecorder(new ByteArrayContent([1, 2, 3]));
        var service = new CloudDhhsFormService(ClientFor(recorder));

        await service.GenerateAsync(
            DhhsFormDefinition.FormKey.AuthorizedRepresentative,
            PersonId,
            DhhsFormDefinition.Selections.None);

        Assert.Equal(
            $"https://api.invalid/api/v1/people/{PersonId}/forms.pdf",
            recorder.LastUri?.ToString());
    }

    [Fact]
    public async Task GeneratingATrackedDhhsReleaseSendsItsTargetAndObligationIdentity()
    {
        var recorder = new UriRecorder(new ByteArrayContent([1, 2, 3]));
        var service = new CloudDhhsFormService(ClientFor(recorder));
        var target = new DateTime(2027, 3, 7);
        var obligationId = Guid.NewGuid();

        await service.GenerateForAnnualTargetAsync(
            DhhsFormDefinition.FormKey.AuthorizationToRelease,
            PersonId,
            DhhsFormDefinition.Selections.None,
            target,
            obligationId);

        Assert.Contains($"\"targetEffectiveDate\":\"{target:yyyy-MM-dd}", recorder.LastBody);
        Assert.Contains($"\"releaseObligationId\":\"{obligationId:D}\"", recorder.LastBody);
    }

    [Fact]
    public async Task ReadingTheSsnStatusRequestsTheVersionedRoute()
    {
        var recorder = new UriRecorder(JsonBody("""{"masked":"***-**-6789","isOnFile":true}"""));
        var service = new CloudDhhsFormService(ClientFor(recorder));

        await service.GetSsnStatusAsync(PersonId);

        Assert.Equal(
            $"https://api.invalid/api/v1/people/{PersonId}/ssn",
            recorder.LastUri?.ToString());
    }

    [Fact]
    public async Task UpdatingTheSsnRequestsTheVersionedRoute()
    {
        var recorder = new UriRecorder(JsonBody("""{"masked":"***-**-6789","isOnFile":true}"""));
        var service = new CloudDhhsFormService(ClientFor(recorder));

        await service.UpdateSsnAsync(PersonId, "123-45-6789");

        Assert.Equal(
            $"https://api.invalid/api/v1/people/{PersonId}/ssn",
            recorder.LastUri?.ToString());
    }

    /// <summary>
    /// The plaintext goes out on the update and must not come back. The stub answers
    /// with a mask, as the real route does; this pins the shape the service reads so a
    /// future change to return the number cannot pass unnoticed.
    /// </summary>
    [Fact]
    public async Task TheUpdateResponseCarriesOnlyAMask()
    {
        var recorder = new UriRecorder(JsonBody("""{"masked":"***-**-6789","isOnFile":true}"""));
        var service = new CloudDhhsFormService(ClientFor(recorder));

        var status = await service.UpdateSsnAsync(PersonId, "123-45-6789");

        Assert.Equal("***-**-6789", status.Masked);
        Assert.DoesNotContain("12345", status.Masked);
    }

    [Fact]
    public async Task GeneratingAnAgencyReleaseRequestsTheVersionedRoute()
    {
        var recorder = new UriRecorder(new ByteArrayContent([1, 2, 3]));
        var service = new CloudAgencyReleaseService(ClientFor(recorder));

        await service.GenerateAsync(PersonId, ValidReleaseRequest());

        Assert.Equal(
            $"https://api.invalid/api/v1/people/{PersonId}/documents/{AnnualDocumentKind.ReleaseAgency}",
            recorder.LastUri?.ToString());
    }

    [Fact]
    public async Task GeneratingATrackedReleaseSendsTheExactObligationIdentifier()
    {
        var recorder = new UriRecorder(new ByteArrayContent([1, 2, 3]));
        var service = new CloudAgencyReleaseService(ClientFor(recorder));
        var obligationId = Guid.NewGuid();

        await service.GenerateMedicalForObligationAsync(
            PersonId, ValidReleaseRequest(), obligationId);

        Assert.Equal(
            $"https://api.invalid/api/v1/people/{PersonId}/documents/{AnnualDocumentKind.ReleaseMedical}",
            recorder.LastUri?.ToString());
        Assert.Contains($"\"releaseObligationId\":\"{obligationId:D}\"", recorder.LastBody);
    }

    /// <summary>
    /// Validation runs before the request goes out, so an invalid release never
    /// reaches the network at all.
    /// </summary>
    [Fact]
    public async Task AnInvalidAgencyReleaseIsNotSent()
    {
        var recorder = new UriRecorder(new ByteArrayContent([1, 2, 3]));
        var service = new CloudAgencyReleaseService(ClientFor(recorder));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GenerateAsync(PersonId, ValidReleaseRequest() with { ContactName = null }));

        Assert.Null(recorder.LastUri);
    }

    [Fact]
    public async Task OpeningAFormSendsTheSelectedActualDateToTheDedicatedRoute()
    {
        var today = DateTime.Today;
        var openedOn = today.AddDays(-2);
        var responseJson = $$"""
            {"id":44,"type":"Q1R","dueDate":"{{today.AddDays(10):yyyy-MM-dd}}","isCompliant":false,"personId":{{PersonId}},"completedDate":null,"openedDate":"{{openedOn:yyyy-MM-dd}}"}
            """;
        var recorder = new UriRecorder(JsonBody(responseJson));
        var service = new CloudFormService(ClientFor(recorder));
        var form = new Form(FormType.Q1R, today.AddDays(10))
        {
            Id = 44,
            PersonId = PersonId
        };

        await service.OpenFormAsync(form, openedOn);

        Assert.Equal($"https://api.invalid/api/v1/forms/{form.Id}/open", recorder.LastUri?.ToString());
        Assert.Equal(HttpMethod.Post, recorder.LastMethod);
        Assert.Contains($"\"openedOn\":\"{openedOn:yyyy-MM-dd}", recorder.LastBody);
        Assert.Equal(openedOn, form.OpenedDate);
    }

    [Fact]
    public async Task ConfirmingScheduledFormDraftSendsThePreviewToken()
    {
        var completedOn = DateTime.Today.AddDays(-1);
        var responseJson = $$"""
            {"id":44,"type":"SafetyPlan","dueDate":"{{completedOn:yyyy-MM-dd}}","isCompliant":true,"personId":{{PersonId}},"completedDate":"{{completedOn:yyyy-MM-dd}}","openedDate":null}
            """;
        var recorder = new UriRecorder(JsonBody(responseJson));
        var service = new CloudFormService(ClientFor(recorder));
        var form = new Form(FormType.SafetyPlan, completedOn) { Id = 44, PersonId = PersonId };

        await service.AttestAsync(form, completedOn, null, true, "44:2:123456");

        Assert.Equal($"/api/v1/people/{PersonId}/forms/SafetyPlan/attestation",
            recorder.LastUri?.AbsolutePath);
        Assert.Contains("\"confirmScheduledNoteConversion\":true", recorder.LastBody);
        Assert.Contains("\"scheduledNoteConversionToken\":\"44:2:123456\"",
            recorder.LastBody);
        Assert.Equal(completedOn, form.CompletedDate);
    }

    [Fact]
    public async Task BillingComplianceResolutionSendsTheExactServiceDate()
    {
        var serviceDate = new DateTime(2026, 8, 10);
        var recorder = new UriRecorder(JsonBody($$"""
            {"serviceDate":"{{serviceDate:yyyy-MM-dd}}","requirements":{{(int)BillingComplianceRequirements.Pcp}}}
            """));
        var service = new CloudSettingsService(ClientFor(recorder));

        var requirements = await service.ResolveBillingComplianceRequirementsAsync(serviceDate);

        Assert.Equal(BillingComplianceRequirements.Pcp, requirements);
        Assert.Equal(
            "/api/v1/settings/billing-compliance-requirements",
            recorder.LastUri?.AbsolutePath);
        Assert.Equal("?serviceDate=2026-08-10", recorder.LastUri?.Query);
        Assert.Equal(HttpMethod.Get, recorder.LastMethod);
    }

    [Fact]
    public async Task SuccessfulFullDemoResetEndsTheInitiatingDesktopSession()
    {
        var requestId = Guid.NewGuid();
        var recorder = new UriRecorder(JsonBody($$"""
            {"requestId":"{{requestId}}","completedAtUtc":"2026-09-06T12:00:00Z","status":"Reset started"}
            """));
        var api = ClientFor(recorder);
        var service = new CloudAdminService(api);
        var endedNotifications = 0;
        api.SessionEnded += (_, _) => endedNotifications++;

        var result = await service.RequestFullDemoResetAsync("RESET DEMO");

        Assert.Equal(requestId, result.RequestId);
        Assert.Equal("/api/v1/admin/demo/reset", recorder.LastUri?.AbsolutePath);
        Assert.Equal(HttpMethod.Post, recorder.LastMethod);
        Assert.Contains("RESET DEMO", recorder.LastBody);
        Assert.True(api.HasSessionEnded);
        Assert.Equal(1, endedNotifications);
    }

    private static AgencyReleaseRequest ValidReleaseRequest() => new(
        true,
        "Community support",
        "Community Provider",
        "Service provider",
        "1 Center Street",
        "Augusta",
        "ME",
        "207-555-0101",
        "207-555-0100",
        "records@example.test",
        [AgencyReleaseInformation.IntakeAssessment, AgencyReleaseInformation.TreatmentPlan],
        null,
        new DateOnly(2026, 8, 19),
        new DateOnly(2026, 11, 17),
        nameof(AgencyReleaseScope.OneTime),
        false,
        false,
        false,
        false);

    private static HttpContent JsonBody(string json) =>
        new StringContent(json, System.Text.Encoding.UTF8, "application/json");

    private static CloudApiClient ClientFor(UriRecorder recorder)
    {
        var client = new HttpClient(recorder) { BaseAddress = new Uri("https://api.invalid") };
        var api = new CloudApiClient(client);
        api.SetAccessToken("test-token");
        return api;
    }

    [Fact]
    public async Task SignInDuringAFullResetSaysTheDemoIsBeingResetNotWakingUp()
    {
        var service = new CloudAuthService(ClientFor(new UriRecorder(JsonBody("""
            {"code":"demo_reset_in_progress","message":"The Demo is being restored. Sign in again in a few minutes.","correlationId":"c1"}
            """), HttpStatusCode.ServiceUnavailable)));

        var failure = await Assert.ThrowsAsync<AuthenticationServiceException>(() =>
            service.AuthenticateAsync("admin", SecurePassword("pw")));

        Assert.Equal(AuthenticationServiceIssue.ServiceUnavailable, failure.Issue);
        Assert.Contains("being reset", failure.Message);
        Assert.DoesNotContain("waking up", failure.Message);
    }

    [Fact]
    public async Task SignInAgainstAnUnavailableServiceStillSaysItMayBeWakingUp()
    {
        var service = new CloudAuthService(ClientFor(new UriRecorder(
            new StringContent(string.Empty), HttpStatusCode.ServiceUnavailable)));

        var failure = await Assert.ThrowsAsync<AuthenticationServiceException>(() =>
            service.AuthenticateAsync("admin", SecurePassword("pw")));

        Assert.Contains("waking up", failure.Message);
    }

    private static System.Security.SecureString SecurePassword(string value)
    {
        var secure = new System.Security.SecureString();
        foreach (var character in value)
            secure.AppendChar(character);
        secure.MakeReadOnly();
        return secure;
    }

    /// <summary>Records the request URI and answers with a canned body.</summary>
    private sealed class UriRecorder(HttpContent content, HttpStatusCode status = HttpStatusCode.OK)
        : HttpMessageHandler
    {
        public Uri? LastUri { get; private set; }
        public HttpMethod? LastMethod { get; private set; }
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastUri = request.RequestUri;
            LastMethod = request.Method;
            LastBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = content };
        }
    }
}
