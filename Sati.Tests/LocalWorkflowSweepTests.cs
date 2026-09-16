using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Xunit;
using Xunit.Abstractions;

namespace Sati.Tests;

/// <summary>
/// Everyday local-Production sequences on a client that carries real history: release rows,
/// attestations, provider links, contacts, and notes, loaded the way the Clients page loads
/// them. The append-only guards added in 1.3.11 refuse rewrites of that history, so any
/// sequence that rewrites it by accident fails here instead of on a workstation.
/// </summary>
public sealed class LocalWorkflowSweepTests(ITestOutputHelper output)
{
    [Fact]
    public async Task EverydaySequencesSucceedOnAClientWithHistory()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var session = new SessionService();
        session.SetUser(fixture.CaseManagerOne);
        var people = fixture.PeopleAs(fixture.CaseManagerOne);
        var providers = new ConsumerProviderService(fixture.Factory, session);
        var contacts = new PersonContactService(fixture.Factory, session);
        var forms = new FormService(fixture.Factory, session);
        var releases = new ReleaseObligationService(fixture.Factory, session);
        var (waiverId, clinicId) = await PrepareAsync(fixture);
        var failures = new List<string>();

        async Task Step(string name, Func<Task> action)
        {
            try
            {
                await action();
                output.WriteLine($"ok    {name}");
            }
            catch (Exception exception)
            {
                failures.Add($"{name}: {exception.GetType().Name}: {exception.Message}");
                output.WriteLine($"FAIL  {name}: {exception}");
            }
        }

        async Task<Person> Loaded() => (await people.GetAllPeopleAsync(fixture.CaseManagerOne.Id))
            .Single(item => item.Id == fixture.PersonOneId);

        var person = await Loaded();
        PersonProvider? waiverLink = null;
        PersonProvider? clinicLink = null;

        await Step("add waiver provider", async () => waiverLink = await providers.SaveAsync(new PersonProvider
        {
            PersonId = person.Id, ProviderId = waiverId, StartDate = DateTime.Today.AddDays(-150)
        }));
        await Step("add medical provider mid-year", async () => clinicLink = await providers.SaveAsync(new PersonProvider
        {
            PersonId = person.Id, ProviderId = clinicId, StartDate = DateTime.Today.AddDays(-5), Role = "Dentist"
        }));
        await Step("edit client after providers", async () =>
        {
            person = await Loaded();
            person.PhoneNumber = "207-555-0110";
            await people.EditPersonAsync(person);
        });
        await Step("edit provider role", async () =>
        {
            clinicLink!.Role = "Dental hygienist";
            clinicLink = await providers.SaveAsync(clinicLink);
        });
        await Step("change provider start date", async () =>
        {
            clinicLink!.StartDate = DateTime.Today.AddDays(-3);
            clinicLink = await providers.SaveAsync(clinicLink);
        });
        await Step("end provider", async () =>
            await providers.EndAsync(person.Id, clinicLink!.Id, DateTime.Today));
        await Step("edit ended provider's end date", async () =>
        {
            var stored = (await providers.GetByPersonAsync(person.Id)).Single(item => item.Id == clinicLink!.Id);
            stored.EndDate = DateTime.Today.AddDays(1);
            await providers.SaveAsync(stored);
        });
        await Step("remove ended provider", async () =>
            await providers.RemoveAsync(person.Id, clinicLink!.Id));
        await Step("reload caseload after provider changes", async () => person = await Loaded());

        await Step("attest and revoke a form", async () =>
        {
            person = await Loaded();
            var form = person.Forms.First(item => item.Type == FormType.Q1R);
            await forms.AttestAsync(form, form.DueDate);
            await forms.RevokeAttestationAsync(form, "Recorded against the wrong review.");
            await forms.AttestAsync(form, form.DueDate);
        });
        await Step("attest a release", async () =>
        {
            person = await Loaded();
            var row = person.ReleaseObligations.First(item =>
                item.CompletedOn is null && item.RetiredOn is null && item.AvailableOn <= DateTime.Today);
            await releases.AttestAsync(person.Id, row.ObligationId, DateTime.Today);
        });
        await Step("withdraw a release", async () =>
        {
            person = await Loaded();
            var row = person.ReleaseObligations.First(item => item.CompletedOn is not null);
            await releases.WithdrawAsync(person.Id, row.ObligationId, DateTime.Today, "Consumer withdrew consent.");
        });
        await Step("reconcile releases twice", async () =>
        {
            var target = ComplianceScheduleRules.CurrentTargetEffectiveDate(person.EffectiveDate!.Value, DateTime.Today);
            await releases.ReconcileAsync(person.Id, target);
            await releases.ReconcileAsync(person.Id, target);
        });
        await Step("add and edit a contact", async () =>
        {
            var contact = await contacts.SaveAsync(new PersonContact
            {
                PersonId = person.Id, FirstName = "Pat", LastName = "Guardian", Kind = PersonContactKind.Personal
            });
            contact.Phone = "207-555-0111";
            await contacts.SaveAsync(contact);
        });
        await Step("edit client after attestations", async () =>
        {
            person = await Loaded();
            person.Address = "12 Synthetic Road";
            await people.EditPersonAsync(person);
        });
        await Step("edit client twice without reloading", async () =>
        {
            person.Address = "14 Synthetic Road";
            await people.EditPersonAsync(person);
        });
        await Step("mark no longer served and restore", async () =>
        {
            person = await Loaded();
            var archived = await people.SetPersonStatusAsync(person.Id, "NoLongerServed", "Moved away.", person.Revision);
            await people.SetPersonStatusAsync(person.Id, "Active", "Returned.", archived.Revision);
        });
        await Step("remove waiver provider with history", async () =>
            await providers.RemoveAsync(fixture.PersonOneId, waiverLink!.Id));

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private static async Task<(int WaiverId, int ClinicId)> PrepareAsync(NoteEntryFixture fixture)
    {
        await using var db = fixture.Factory.CreateDbContext();
        var person = await db.People.SingleAsync(item => item.Id == fixture.PersonOneId);
        person.EffectiveDate = DateTime.Today.AddDays(-200);
        person.Bio = "Synthetic biography.";
        person.HasSharedLiving = true;
        var waiver = new Provider { AgencyId = fixture.CaseManagerOne.AgencyId, Type = ProviderType.Waiver, Name = "Shared Living Agency" };
        var clinic = new Provider { AgencyId = fixture.CaseManagerOne.AgencyId, Type = ProviderType.Healthcare, Name = "Synthetic Dental", MedicalKind = MedicalProviderKind.Practice };
        db.Providers.AddRange(waiver, clinic);
        var note = Note.Create("Visit.", DateTime.Today.AddDays(-3), NoteStatus.Logged, 30, person.Id, noteType: NoteType.Visit);
        note.AgencyId = fixture.CaseManagerOne.AgencyId;
        note.GoalProgress = GoalProgressLevel.None;
        db.Notes.Add(note);
        await db.SaveChangesAsync();
        return (waiver.Id, clinic.Id);
    }
}
