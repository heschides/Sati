using System.IO;
using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Forms;
using Sati.Models;
using Xunit;

namespace Sati.Tests;

[Collection(PdfRenderingCollection.Name)]
public sealed class AnnualPacketTests
{
    internal static AnnualPacketComposer Composer() => new(new AgencyReleasePdfGenerator(), new DhhsFormFiller(),
        new DocumentTemplatePdfComposer(), new SafetyPlanPdfGenerator());
    internal static PacketRenderInput Input() => new(new(1, "Jordan Example", new DateTime(1987, 4, 12), null,
        "Example Support Services", "12 Main Street, Augusta, ME 04330", "207-555-0100", "Case Manager", "CaseManager"),
        new DateTime(2026, 9, 1), new DateTime(2027, 8, 31), new DateTime(2026, 9, 3, 12, 0, 0, DateTimeKind.Utc),
        12, [], null, [], false, "Example Primary Care", "10 Example Street, Augusta, ME 04330", "207-555-0101",
        [
            new(101, Guid.Parse("11111111-1111-1111-1111-111111111111"), ReleaseObligationCategory.Agency,
                null, "Example Support Provider", "20 Service Street, Augusta, ME 04330", "207-555-0110"),
            new(102, Guid.Parse("22222222-2222-2222-2222-222222222222"), ReleaseObligationCategory.Medical,
                null, "Example Primary Care", "10 Example Street, Augusta, ME 04330", "207-555-0101"),
            new(103, Guid.Parse("33333333-3333-3333-3333-333333333333"), ReleaseObligationCategory.Dhhs,
                null, null, null, null)
        ]);

    [Fact]
    public void RecordsRequestRequiresAttestedMedicalReleaseAndLinkedRecipient()
    {
        var composer = Composer(); var input = Input();
        Assert.DoesNotContain(composer.Render(input).Documents, x => x.Kind == AnnualDocumentKind.MedicalRecordsRequest);
        Assert.DoesNotContain(composer.Render(input with { MedicalReleaseAttested = true, ProviderName = null }).Documents,
            x => x.Kind == AnnualDocumentKind.MedicalRecordsRequest);
        var documents = composer.Render(input with { MedicalReleaseAttested = true }).Documents;
        Assert.Contains(documents, x => x.Kind == AnnualDocumentKind.MedicalRecordsRequest);
        Assert.All(documents.Where(x => x.Kind is AnnualDocumentKind.ReleaseAgency or AnnualDocumentKind.ReleaseMedical or
            AnnualDocumentKind.ReleaseDhhs or AnnualDocumentKind.SafetyPlan), x => Assert.Equal(DocumentArtifactOrigin.Draft, x.Origin));
        Assert.Equal(3, documents.Count(x => x.ReleaseObligationRecordId is not null));
        Assert.Equal(3, documents.Where(x => x.ReleaseObligationRecordId is not null)
            .Select(x => x.ReleaseObligationRecordId).Distinct().Count());
        // Opt-in, synthetic-only preview for repeatable visual QA. Normal tests write no output files.
        if (Environment.GetEnvironmentVariable("SATI_DOCUMENT_QA_OUTPUT") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            foreach (var document in documents) File.WriteAllBytes(Path.Combine(directory, document.FileName), document.Pdf);
        }
    }

    [Fact]
    public void PacketKeepsMultipleSameCategoryReleasesSeparate()
    {
        var input = Input();
        var secondMedical = new PacketReleaseInput(
            104,
            Guid.Parse("44444444-4444-4444-4444-444444444444"),
            ReleaseObligationCategory.Medical,
            null,
            "Second Medical Provider",
            "30 Health Street, Augusta, ME 04330",
            "207-555-0120");
        input = input with
        {
            ReleaseObligations = input.ReleaseObligations!.Append(secondMedical).ToList()
        };

        var releases = Composer().Render(input).Documents
            .Where(x => x.Kind == AnnualDocumentKind.ReleaseMedical)
            .ToList();

        Assert.Equal(2, releases.Count);
        Assert.Equal(2, releases.Select(x => x.ReleaseObligationRecordId).Distinct().Count());
        Assert.Equal(2, releases.Select(x => x.FileName).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void OneRecipientsLiveArtifactDoesNotHideAnotherRecipientsRelease()
    {
        var input = Input();
        var medical = input.ReleaseObligations!.Single(x =>
            x.Category == ReleaseObligationCategory.Medical);
        var secondMedical = medical with
        {
            RecordId = 104,
            ObligationId = Guid.Parse("44444444-4444-4444-4444-444444444444"),
            RecipientName = "Second Medical Provider"
        };
        var live = new DocumentArtifactDto(
            91,
            input.Subject.PersonId,
            1,
            AnnualDocumentKind.ReleaseMedical.ToString(),
            input.CycleStart,
            DocumentArtifactOrigin.RecordedAsExternal.ToString(),
            input.GeneratedAtUtc,
            input.ActorId,
            null,
            null,
            null,
            [],
            "Synthetic external evidence",
            ReleaseObligationRecordId: medical.RecordId);
        input = input with
        {
            LiveArtifacts = [live],
            ReleaseObligations = input.ReleaseObligations!.Append(secondMedical).ToList()
        };

        var rendered = Composer().Render(input);

        var remaining = Assert.Single(rendered.Documents, x =>
            x.Kind == AnnualDocumentKind.ReleaseMedical);
        Assert.Equal(secondMedical.RecordId, remaining.ReleaseObligationRecordId);
        Assert.Contains(rendered.Omitted, text =>
            text.Contains("Example Primary Care", StringComparison.Ordinal));
    }

    [Fact]
    public void LeapYearCycleUsesOriginalEnrollmentAnniversaryAndInclusiveWindow()
    {
        var effective = new DateTime(2024, 2, 29);
        Assert.Equal(new DateTime(2024, 2, 29), AnnualDocumentCycle.CurrentStart(effective, new DateTime(2024, 2, 28)));
        var cycle = new DateTime(2027, 2, 28);
        var opens = cycle.AddDays(-30);
        Assert.False(AnnualPacketWindow.ForCycle(effective, cycle, opens.AddDays(-1), 30).IsOpen);
        Assert.True(AnnualPacketWindow.ForCycle(effective, cycle, opens, 30).IsOpen);
        Assert.Equal(new DateTime(2028, 2, 28), AnnualPacketWindow.ForCycle(effective, cycle, opens, 30).EndsOn);
        Assert.Equal(new DateTime(2028, 2, 28), AnnualDocumentCycle.EndInclusive(effective, cycle));
        Assert.Equal(cycle, AnnualPacketWindow.SuggestedCycle(effective, opens, 30));
        Assert.Throws<ArgumentException>(() => AnnualPacketWindow.ForCycle(effective, cycle.AddDays(1), opens, 30));
    }

    [Fact]
    public void RecipientInheritsOrganizationContactButPreservesPractitionerName()
    {
        var recipient = RecordsRecipient.Resolve(2, [new(1, null, "Practice", "Address", "Phone"), new(2, 1, "Practitioner", null, "")]);
        Assert.Equal("Practitioner", recipient!.Name); Assert.Equal("Address", recipient.Address); Assert.Equal("Phone", recipient.Phone);
        Assert.Null(RecordsRecipient.Resolve(3, [new(1, null, "Practice", "Address", "Phone")]));
        Assert.Throws<InvalidOperationException>(() => RecordsRecipient.Resolve(1, [new(1, 2, "A", null, null), new(2, 1, "B", null, null)]));
    }

    [Fact]
    public void ReceiptRequiresActualDateOrGoodFaithEffortAndHashChecksBytes()
    {
        var today = DateTime.Today;
        Assert.NotNull(DocumentAcknowledgmentRules.Validate(new(1, null, " "), today, today));
        Assert.NotNull(DocumentAcknowledgmentRules.Validate(new(1, today.AddDays(-1), null), today, today));
        Assert.NotNull(DocumentAcknowledgmentRules.Validate(new(1, today.AddDays(1), null), today, today));
        Assert.Null(DocumentAcknowledgmentRules.Validate(new(1, null, "Delivery attempted; follow up arranged."), today, today));
        var original = DocumentVerification.FromBytes(1, [1, 2, 3]);
        Assert.True(DocumentVerification.Matches(original.Sha256, 3, original));
        Assert.False(DocumentVerification.Matches(original.Sha256, 3, DocumentVerification.FromBytes(1, [1, 2, 4])));
        Assert.False(DocumentVerification.Matches(null, null, original));
    }

    [Fact]
    public async Task LocalPacketAndReceiptUsePersistedArtifactsAndAudit()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var cycle = DateTime.Today.AddMonths(-2);
        await using (var setup = fixture.Factory.CreateDbContext())
        {
            (await setup.People.SingleAsync(x => x.Id == fixture.PersonOneId)).EffectiveDate = cycle;
            await setup.SaveChangesAsync();
        }
        var session = new SessionService(); session.SetUser(fixture.CaseManagerOne);
        var service = new AnnualDocumentService(fixture.Factory, session, Composer());
        var packet = await service.SavePacketAsync(fixture.PersonOneId, cycle);
        Assert.NotEmpty(packet.Pdf);
        var status = await service.GetStatusAsync(fixture.PersonOneId, cycle);
        var releaseArtifacts = status.Artifacts.Where(x => x.Kind is
            nameof(AnnualDocumentKind.ReleaseAgency) or
            nameof(AnnualDocumentKind.ReleaseMedical) or
            nameof(AnnualDocumentKind.ReleaseDhhs)).ToArray();
        Assert.NotEmpty(releaseArtifacts);
        Assert.All(releaseArtifacts, artifact =>
            Assert.True(artifact.ReleaseObligationRecordId is > 0));
        var notice = status.Artifacts.Single(x => x.Kind == "PrivacyPractices");
        await service.AcknowledgeAsync(fixture.PersonOneId, new(notice.Id, DateTime.Today, null));
        Assert.Contains(notice.Id, (await service.GetStatusAsync(fixture.PersonOneId, cycle)).AcknowledgedArtifactIds);
        await using var db = fixture.Factory.CreateDbContext();
        Assert.Contains(await db.AuditEvents.ToListAsync(), x => x.Action == "annual-packet.saved");
        var receipt = await db.DocumentAcknowledgments.SingleAsync();
        receipt.GoodFaithEffortReason = "Illegal edit";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task ReminderUsesExactAnnualEffectiveDateInsteadOfBorrowingNextYearsPcp()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var target = DateTime.Today.AddDays(100);
        await using (var setup = fixture.Factory.CreateDbContext())
        {
            var person = await setup.People.SingleAsync(x => x.Id == fixture.PersonOneId);
            person.EffectiveDate = target;
            setup.Forms.Add(new Form(
                FormType.PCP,
                target.AddYears(1),
                DateTime.Today,
                target.AddYears(1))
            {
                PersonId = person.Id
            });
            await setup.SaveChangesAsync();
        }

        var session = new SessionService();
        session.SetUser(fixture.CaseManagerOne);
        var service = new AnnualDocumentService(fixture.Factory, session, Composer());

        var wrongCycleOnly = await service.GetStatusAsync(fixture.PersonOneId, target);
        Assert.False(wrongCycleOnly.Window.IsOpen);
        Assert.Equal(string.Empty, wrongCycleOnly.Reminder);

        await using (var setup = fixture.Factory.CreateDbContext())
        {
            setup.Forms.Add(new Form(FormType.PCP, target, DateTime.Today, target)
            {
                PersonId = fixture.PersonOneId
            });
            await setup.SaveChangesAsync();
        }

        var exactCycle = await service.GetStatusAsync(fixture.PersonOneId, target);
        Assert.Contains("Preparation still needed", exactCycle.Reminder);
    }
}
