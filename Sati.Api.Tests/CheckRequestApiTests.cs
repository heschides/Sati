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

    private static SaveCheckRequestRequest Complete(CheckRequestDto request, decimal amount) => new(
        request.RequestDate,
        "Example Vendor",
        "10 Main Street, Augusta, ME 04330",
        amount,
        new DateTime(2026, 9, 20),
        "Synthetic accessibility supplies",
        request.Revision);
}
