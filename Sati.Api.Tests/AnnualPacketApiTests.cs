using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sati.Api.Data;
using Sati.Contracts.V1;
using Xunit;

namespace Sati.Api.Tests;
[Collection(SatiApiCollection.Name)]
public sealed class AnnualPacketApiTests(SatiApiFactory factory)
{
    [Fact]
    public async Task ReminderUsesExactAnnualEffectiveDateInsteadOfBorrowingAnotherPcp()
    {
        using var owner = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var target = DateTime.Today.AddDays(100);
        var person = new ServerPerson
        {
            UserId = 12,
            AgencyId = 1,
            FirstName = "Synthetic",
            LastName = "Cycle identity",
            EffectiveDate = target
        };
        db.People.Add(person);
        await db.SaveChangesAsync();
        db.Forms.Add(new ServerForm
        {
            PersonId = person.Id,
            Type = "PCP",
            TargetEffectiveDate = target.AddYears(1),
            DueDate = target.AddYears(1),
            CompletedDate = DateTime.Today
        });
        await db.SaveChangesAsync();

        try
        {
            var route = $"/api/v1/people/{person.Id}/annual-documents?cycleStart={target:yyyy-MM-dd}";
            var wrongCycleOnly = (await owner.GetFromJsonAsync<AnnualDocumentsStatusDto>(route))!;
            Assert.False(wrongCycleOnly.Window.IsOpen);
            Assert.Equal(string.Empty, wrongCycleOnly.Reminder);

            db.Forms.Add(new ServerForm
            {
                PersonId = person.Id,
                Type = "PCP",
                TargetEffectiveDate = target,
                DueDate = target,
                CompletedDate = DateTime.Today
            });
            await db.SaveChangesAsync();

            var exactCycle = (await owner.GetFromJsonAsync<AnnualDocumentsStatusDto>(route))!;
            Assert.Contains("Preparation still needed", exactCycle.Reminder);
        }
        finally
        {
            await db.Forms.Where(x => x.PersonId == person.Id).ExecuteDeleteAsync();
            await db.People.Where(x => x.Id == person.Id).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task PacketHashesAndArtifactBoundReceiptAreEnforced()
    {
        using var owner = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        using var outsider = await factory.CreateAuthenticatedClientAsync("case-manager-two");
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var cycle = DateTime.Today.AddMonths(-2);
        var person = new ServerPerson { UserId = 12, AgencyId = 1, FirstName = "Synthetic", LastName = "Packet", EffectiveDate = cycle };
        db.People.Add(person); await db.SaveChangesAsync();
        var form = new ServerForm
        {
            PersonId = person.Id,
            Type = "PrivacyPractices",
            TargetEffectiveDate = cycle,
            DueDate = cycle
        };
        db.Forms.Add(form); await db.SaveChangesAsync();
        try
        {
            Assert.Equal(HttpStatusCode.NotFound, (await outsider.PostAsJsonAsync($"/api/v1/people/{person.Id}/annual-packet", new SaveAnnualPacketRequest(cycle))).StatusCode);
            var response = await owner.PostAsJsonAsync($"/api/v1/people/{person.Id}/annual-packet", new SaveAnnualPacketRequest(cycle));
            response.EnsureSuccessStatusCode();
            Assert.True(response.Headers.CacheControl?.NoStore);
            Assert.True(response.Headers.CacheControl?.NoCache);
            using var zip = new ZipArchive(new MemoryStream(await response.Content.ReadAsByteArrayAsync()));
            Assert.NotNull(zip.GetEntry("MANIFEST.txt"));
            var status = (await owner.GetFromJsonAsync<AnnualDocumentsStatusDto>($"/api/v1/people/{person.Id}/annual-documents?cycleStart={cycle:yyyy-MM-dd}"))!;
            Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync($"/api/v1/people/{person.Id}/annual-documents?cycleStart={cycle:yyyy-MM-dd}")).StatusCode);
            var releaseArtifacts = status.Artifacts.Where(x => x.Kind is
                nameof(AnnualDocumentKind.ReleaseAgency) or
                nameof(AnnualDocumentKind.ReleaseMedical) or
                nameof(AnnualDocumentKind.ReleaseDhhs)).ToArray();
            Assert.NotEmpty(releaseArtifacts);
            Assert.All(releaseArtifacts, artifact =>
                Assert.True(artifact.ReleaseObligationRecordId is > 0));
            foreach (var artifact in status.Artifacts)
            {
                using var bytes = new MemoryStream();
                await zip.GetEntry(artifact.SuggestedFileName!)!.Open().CopyToAsync(bytes);
                var request = DocumentVerification.FromBytes(artifact.Id, bytes.ToArray());
                Assert.Equal(HttpStatusCode.NotFound, (await outsider.PostAsJsonAsync($"/api/v1/people/{person.Id}/documents/verify", request)).StatusCode);
                var verified = await owner.PostAsJsonAsync($"/api/v1/people/{person.Id}/documents/verify", request);
                Assert.True((await verified.Content.ReadFromJsonAsync<VerifyDocumentResult>())!.Matches);
                var tampered = await owner.PostAsJsonAsync($"/api/v1/people/{person.Id}/documents/verify", request with { ByteCount = request.ByteCount + 1 });
                Assert.False((await tampered.Content.ReadFromJsonAsync<VerifyDocumentResult>())!.Matches);
            }
            var notice = status.Artifacts.Single(x => x.Kind == "PrivacyPractices");
            var receiptRoute = $"/api/v1/people/{person.Id}/documents/privacy-practices/acknowledgment";
            var receipt = new AcknowledgeDocumentRequest(notice.Id, DateTime.Today, null);
            Assert.Equal(HttpStatusCode.NotFound, (await outsider.PostAsJsonAsync(receiptRoute, receipt)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync(receiptRoute, receipt with { ReceivedOn = null })).StatusCode);
            (await owner.PostAsJsonAsync(receiptRoute, receipt)).EnsureSuccessStatusCode();
            var acknowledgedStatus = (await owner.GetFromJsonAsync<AnnualDocumentsStatusDto>(
                $"/api/v1/people/{person.Id}/annual-documents?cycleStart={cycle:yyyy-MM-dd}"))!;
            Assert.Contains(notice.Id, acknowledgedStatus.AcknowledgedArtifactIds);
            (await owner.PostAsJsonAsync($"/api/v1/people/{person.Id}/documents/PrivacyPractices", new RenderAnnualDocumentRequest(cycle))).EnsureSuccessStatusCode();
            var replacedStatus = (await owner.GetFromJsonAsync<AnnualDocumentsStatusDto>(
                $"/api/v1/people/{person.Id}/annual-documents?cycleStart={cycle:yyyy-MM-dd}"))!;
            Assert.DoesNotContain(notice.Id, replacedStatus.AcknowledgedArtifactIds);
            Assert.NotEqual(notice.Id, replacedStatus.Artifacts.Single(x => x.Kind == "PrivacyPractices").Id);
            var savedReceipt = await db.DocumentAcknowledgments.SingleAsync(x => x.DocumentArtifactId == notice.Id);
            savedReceipt.GoodFaithEffortReason = "Attempt to rewrite historical receipt";
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        }
        finally
        {
            await db.DocumentAcknowledgments.Where(x => db.DocumentArtifacts.Any(a => a.Id == x.DocumentArtifactId && a.PersonId == person.Id)).ExecuteDeleteAsync();
            await db.DocumentArtifacts.Where(x => x.PersonId == person.Id).ExecuteDeleteAsync();
            await db.Forms.Where(x => x.PersonId == person.Id).ExecuteDeleteAsync();
            await db.People.Where(x => x.Id == person.Id).ExecuteDeleteAsync();
        }
    }
}
