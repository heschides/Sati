using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sati.Api.Data;
using Sati.Contracts.V1;
using Xunit;

namespace Sati.Api.Tests;

[Collection(SatiApiCollection.Name)]
public sealed class FormWizardProgressTests(SatiApiFactory factory)
{
    private static string Route(int personId, string key = "benefits-application") =>
        $"/api/v1/people/{personId}/form-wizards/{key}/progress";

    [Fact]
    public async Task Draft_is_encrypted_author_scoped_and_optimistically_versioned()
    {
        using var anonymous = factory.CreateAnonymousClient();
        using var owner = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        const string answers = "{\"Answers\":{\"person2.ssn\":\"123456789\"}}";
        var request = new SaveFormWizardProgressRequest(0, 4, answers);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.PutAsJsonAsync(Route(101), request)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await owner.PutAsJsonAsync(Route(201), request)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await owner.GetAsync(Route(201))).StatusCode);
        await DeleteTestRowAsync();

        try
        {
            var empty = await owner.GetFromJsonAsync<FormWizardProgressDto>(Route(101));
            Assert.Equal(0, empty?.Revision);
            var save = await owner.PutAsJsonAsync(Route(101), request);
            save.EnsureSuccessStatusCode();
            Assert.Contains("no-store", save.Headers.CacheControl?.ToString() ?? "");
            var first = await save.Content.ReadFromJsonAsync<FormWizardProgressDto>();
            Assert.NotNull(first);
            Assert.Equal(1, first.Revision);
            Assert.Equal(4, first.StepIndex);

            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                var stored = await db.FormWizardProgress.AsNoTracking().SingleAsync(x =>
                    x.PersonId == 101 && x.FormKey == "benefits-application");
                Assert.DoesNotContain("123456789", Encoding.UTF8.GetString(stored.Ciphertext));
            }

            var stale = await owner.PutAsJsonAsync(Route(101), request);
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            var read = await owner.GetAsync(Route(101));
            read.EnsureSuccessStatusCode();
            Assert.Contains("no-store", read.Headers.CacheControl?.ToString() ?? "");
            var resumed = await read.Content.ReadFromJsonAsync<FormWizardProgressDto>();
            Assert.Equal(answers, resumed?.AnswersJson);
            Assert.Equal(1, resumed?.Revision);

            var second = await owner.PutAsJsonAsync(Route(101),
                new SaveFormWizardProgressRequest(1, 5, "{\"Answers\":{}}"));
            second.EnsureSuccessStatusCode();
            Assert.Equal(2, (await second.Content.ReadFromJsonAsync<FormWizardProgressDto>())?.Revision);
        }
        finally { await DeleteTestRowAsync(); }
    }

    [Fact]
    public void Rejects_unrecognized_forms_and_invalid_progress()
    {
        Assert.False(FormWizardProgressRules.ValidKey("../note"));
        Assert.False(FormWizardProgressRules.Valid("cwic-packet",
            new SaveFormWizardProgressRequest(0, 0, "[]")));
        Assert.True(FormWizardProgressRules.ValidKey(
            $"medical-release.{Guid.NewGuid():D}"));
        Assert.True(FormWizardProgressRules.ValidKey("dhhs-authorization-to-release.20260929"));
        Assert.False(FormWizardProgressRules.ValidKey("dhhs-authorization-to-release.20261399"));
    }

    private async Task DeleteTestRowAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        await db.FormWizardProgress.Where(x =>
                x.PersonId == 101 && x.FormKey == "benefits-application")
            .ExecuteDeleteAsync();
    }
}
