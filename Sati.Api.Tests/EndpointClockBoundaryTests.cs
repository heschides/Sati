using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Sati.Contracts.V1;
using Xunit;

namespace Sati.Api.Tests;

public sealed class EndpointClockBoundaryTests
{
    private static readonly DateTimeOffset Instant =
        new(2026, 7, 1, 0, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task FormCompletionRejectsJulyFirstWhileMaineIsStillJuneThirtieth()
    {
        using var factory = CreateFactory();
        using var owner = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        var people = await owner.GetFromJsonAsync<List<PersonDto>>("/api/v1/caseload");
        var form = people!.SelectMany(person => person.Forms).First();

        using var response = await owner.PutAsJsonAsync($"/api/v1/forms/{form.Id}",
            new UpdateFormRequest(new DateTime(2026, 7, 1), form.OpenedDate));
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(FormCompletionRules.FutureDateMessage,
            problem.GetProperty("errors").GetProperty("completedDate")
                .EnumerateArray().Select(item => item.GetString()));
    }

    [Fact]
    public async Task PersonSaveRejectsJulyFirstBirthDateWhileMaineIsStillJuneThirtieth()
    {
        using var factory = CreateFactory();
        using var owner = await factory.CreateAuthenticatedClientAsync("case-manager-one");

        using var response = await owner.PostAsJsonAsync("/api/v1/people", ValidPerson() with
        {
            BirthDate = new DateTime(2026, 7, 1)
        });
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(problem.GetProperty("errors").TryGetProperty("birthDate", out _));
    }

    [Fact]
    public async Task GeneratedEdiUsesJuneThirtiethInIsaAndGs()
    {
        using var factory = CreateFactory();
        // This is an unsent export/time-zone positive, not the seeded legacy received claim.
        using var biller = await factory.CreateAuthenticatedClientAsync("admin-two");

        using var response = await biller.PostAsJsonAsync("/api/v1/billing/periods/1202/edi",
            new GenerateEdiRequest(true, Guid.NewGuid().ToString("N")));
        Assert.True(response.IsSuccessStatusCode,
            $"{response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var file = await response.Content.ReadFromJsonAsync<EdiFileDto>();
        Assert.NotNull(file);
        var segments = file.Content.Split('~', StringSplitOptions.RemoveEmptyEntries)
            .Select(segment => segment.Trim()).ToArray();
        var isa = segments.Single(segment => segment.StartsWith("ISA*", StringComparison.Ordinal)).Split('*');
        var gs = segments.Single(segment => segment.StartsWith("GS*", StringComparison.Ordinal)).Split('*');

        Assert.Equal("260630", isa[9]);
        Assert.Equal("20260630", gs[4]);
    }

    private static SatiApiFactory CreateFactory() => new()
    {
        ClockOverride = new FrozenTimeProvider(Instant), IncludeLegacyClaimEvidence = false
    };

    private sealed class FrozenTimeProvider(DateTimeOffset instant) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => instant;
    }

    private static SavePersonRequest ValidPerson() => new(
        "Riley", "Boundary", new DateTime(1990, 4, 3), "Unknown", null,
        "A synthetic clock-boundary request.", "None", null, null, null, null,
        false, false, null, null, null, null, null, null, null, null, null,
        false, false, false, false, false, false, 1,
        false, false, false, [], 0, true, false, null, null, false, false,
        "riley@example.test");
}
