using System.Net;
using System.Net.Http.Json;
using Sati.Contracts.V1;
using Xunit;

namespace Sati.Api.Tests;

[Collection(SatiApiCollection.Name)]
public sealed class PersonPhotoApiTests(SatiApiFactory factory)
{
    private const int OwnPerson = 101;
    private const int OtherAgencyPerson = 201;
    private static readonly byte[] OnePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    private static string Route(int personId) => $"/api/v1/people/{personId}/photo";

    [Fact]
    public async Task AnonymousAndCrossTenantCallersCannotReachAPhoto()
    {
        using var anonymous = factory.CreateAnonymousClient();
        using var caseManager = await factory.CreateAuthenticatedClientAsync("case-manager-one");

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Route(OwnPerson))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PutAsJsonAsync(
            Route(OwnPerson), new SavePersonPhotoRequest("image/png", OnePixelPng, null))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await caseManager.GetAsync(Route(OtherAgencyPerson))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await caseManager.PutAsJsonAsync(
            Route(OtherAgencyPerson), new SavePersonPhotoRequest("image/png", OnePixelPng, null))).StatusCode);
    }

    [Fact]
    public async Task APhotoRoundTripsOnlyOnItsDedicatedNoStoreRoute()
    {
        using var client = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        await RemoveExistingAsync(client);

        var save = await client.PutAsJsonAsync(
            Route(OwnPerson), new SavePersonPhotoRequest("image/png", OnePixelPng, null));
        save.EnsureSuccessStatusCode();
        var saved = await save.Content.ReadFromJsonAsync<PersonPhotoDto>();

        var read = await client.GetAsync(Route(OwnPerson));
        read.EnsureSuccessStatusCode();
        var state = await read.Content.ReadFromJsonAsync<PersonPhotoStateDto>();

        Assert.Equal("no-store, no-cache", read.Headers.CacheControl?.ToString());
        Assert.Equal(OnePixelPng, state!.Photo!.Content);
        Assert.Equal("image/png", state.Photo.ContentType);
        Assert.Equal(1, state.Photo.PixelWidth);
        Assert.Equal(saved!.Revision, state.Photo.Revision);
        Assert.Contains(await factory.GetAuditEventsAsync("person.photo-updated"), item =>
            item.ResourceId == OwnPerson.ToString());

        var delete = await client.DeleteAsync($"{Route(OwnPerson)}?expectedRevision={state.Photo.Revision}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
    }

    [Fact]
    public async Task StaleAndMislabeledWritesAreRejectedWithoutReplacingThePhoto()
    {
        using var client = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        await RemoveExistingAsync(client);
        var first = await client.PutAsJsonAsync(
            Route(OwnPerson), new SavePersonPhotoRequest("image/png", OnePixelPng, null));
        var stored = (await first.Content.ReadFromJsonAsync<PersonPhotoDto>())!;

        var stale = await client.PutAsJsonAsync(
            Route(OwnPerson), new SavePersonPhotoRequest("image/png", OnePixelPng, stored.Revision - 1));
        var mislabeled = await client.PutAsJsonAsync(
            Route(OwnPerson), new SavePersonPhotoRequest("image/jpeg", OnePixelPng, stored.Revision));

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, mislabeled.StatusCode);
        var state = await client.GetFromJsonAsync<PersonPhotoStateDto>(Route(OwnPerson));
        Assert.Equal(stored.Revision, state!.Photo!.Revision);
        Assert.Equal(OnePixelPng, state.Photo.Content);
        await client.DeleteAsync($"{Route(OwnPerson)}?expectedRevision={stored.Revision}");
    }

    private static async Task RemoveExistingAsync(HttpClient client)
    {
        var state = await client.GetFromJsonAsync<PersonPhotoStateDto>(Route(OwnPerson));
        if (state?.Photo is { } photo)
            (await client.DeleteAsync($"{Route(OwnPerson)}?expectedRevision={photo.Revision}"))
                .EnsureSuccessStatusCode();
    }
}
