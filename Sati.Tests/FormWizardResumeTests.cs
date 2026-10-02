using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Sati.ViewModels.ClientDocuments;
using Xunit;

namespace Sati.Tests;

public sealed class FormWizardResumeTests
{
    [Fact]
    public async Task Cwic_resumes_text_dates_and_multi_select_answers()
    {
        var drafts = new TestFormWizardProgressService();
        var person = PersonFor(311);
        var first = new CwicPacketViewModel(new UnusedCwicService(), drafts);
        first.SetPerson(person);
        first.WorkingHoursPerWeek = "24";
        first.WorkBeganOn = new DateTime(2026, 5, 1);
        first.MeetingMethods[0].IsSelected = true;
        await first.Progress.SaveCommand.ExecuteAsync(null);

        var resumed = new CwicPacketViewModel(new UnusedCwicService(), drafts);
        resumed.SetPerson(person);
        Assert.Equal("24", resumed.WorkingHoursPerWeek);
        Assert.Equal(new DateTime(2026, 5, 1), resumed.WorkBeganOn);
        Assert.True(resumed.MeetingMethods[0].IsSelected);
    }

    [Fact]
    public async Task Safety_device_resumes_appendix_rows_and_planning_date()
    {
        var drafts = new TestFormWizardProgressService();
        var person = PersonFor(312);
        var first = new SafetyDeviceViewModel(new UnusedSafetyDeviceService(), drafts);
        first.SetPerson(person);
        first.Devices[^1].NameAndType = "Alarm";
        first.Devices[^1].Level = "2";
        first.PlanningTeamMeetingDate = new DateTime(2026, 9, 1);
        await first.Progress.SaveCommand.ExecuteAsync(null);

        var resumed = new SafetyDeviceViewModel(new UnusedSafetyDeviceService(), drafts);
        resumed.SetPerson(person);
        Assert.Equal("Alarm", resumed.Devices[^1].NameAndType);
        Assert.Equal("2", resumed.Devices[^1].Level);
        Assert.Equal(new DateTime(2026, 9, 1), resumed.PlanningTeamMeetingDate);
    }

    [Fact]
    public async Task Safety_device_progress_restores_the_step_and_reveals_only_entered_device_rows()
    {
        var drafts = new TestFormWizardProgressService();
        var person = PersonFor(314);
        var first = new SafetyDeviceViewModel(new UnusedSafetyDeviceService(), drafts);
        first.SetPerson(person);

        Assert.True(first.IsMemberStep);
        Assert.Single(first.VisibleDevices);
        first.NextCommand.Execute(null);
        Assert.True(first.IsDeviceStep);
        first.Devices[0].NameAndType = "Door alarm";
        first.AddDeviceCommand.Execute(null);
        first.Devices[1].Purpose = "Alerts staff";
        Assert.Equal(2, first.CurrentDevice.Number);
        Assert.Equal(2, first.VisibleDevices.Count);
        first.NextCommand.Execute(null);
        first.NextCommand.Execute(null);
        Assert.True(first.IsReviewStep);
        await first.Progress.SaveCommand.ExecuteAsync(null);

        var resumed = new SafetyDeviceViewModel(new UnusedSafetyDeviceService(), drafts);
        resumed.SetPerson(person);
        Assert.True(resumed.IsReviewStep);
        Assert.Equal("Step 4 of 4", resumed.StepProgress);
        Assert.Equal(2, resumed.VisibleDevices.Count);
        Assert.Equal(2, resumed.CurrentDevice.Number);
        Assert.Equal("Door alarm", resumed.Devices[0].NameAndType);
        Assert.Equal("Alerts staff", resumed.Devices[1].Purpose);

        resumed.ShowDeviceStepCommand.Execute(null);
        Assert.True(resumed.IsDeviceStep);
        resumed.PreviousDeviceCommand.Execute(null);
        Assert.Equal(1, resumed.CurrentDevice.Number);
        resumed.NextDeviceCommand.Execute(null);
        Assert.Equal(2, resumed.CurrentDevice.Number);
    }

    [Fact]
    public async Task Safety_device_validation_returns_to_the_device_that_needs_correction()
    {
        var model = new SafetyDeviceViewModel(
            new UnusedSafetyDeviceService(), new TestFormWizardProgressService());
        model.SetPerson(PersonFor(315));
        model.Devices[6].NameAndType = new string('x', 100);
        model.ShowReviewStepCommand.Execute(null);

        await model.GenerateCommand.ExecuteAsync(null);

        Assert.True(model.IsDeviceStep);
        Assert.Equal(7, model.CurrentDevice.Number);
        Assert.Equal(7, model.VisibleDevices.Count);
        Assert.Contains("will not fit", model.StatusMessage);
    }

    [Fact]
    public async Task Housing_support_funds_resumes_amounts_and_landlord_answers()
    {
        var drafts = new TestFormWizardProgressService();
        var person = PersonFor(313);
        var service = new UnusedHousingService();
        var session = new EmptySession();
        var first = new HousingSupportFundsViewModel(service, session, drafts);
        first.SetPerson(person);
        first.HousingType = "Rental";
        first.LandlordName = "Example landlord";
        first.AmountRequested = 650m;
        await first.Progress.SaveCommand.ExecuteAsync(null);

        var resumed = new HousingSupportFundsViewModel(service, session, drafts);
        resumed.SetPerson(person);
        Assert.Equal("Rental", resumed.HousingType);
        Assert.Equal("Example landlord", resumed.LandlordName);
        Assert.Equal(650m, resumed.AmountRequested);
    }

    private static Person PersonFor(int id)
    {
        var person = Person.CreatePerson(1, "Test", "Consumer", string.Empty,
            new DateTime(1980, 1, 1), DateTime.Today.AddYears(-1),
            WaiverType.Section21, new Settings());
        typeof(Person).GetProperty(nameof(Person.Id))!.SetValue(person, id);
        person.AgencyId = 1;
        return person;
    }

    private sealed class UnusedCwicService : ICwicPacketService
    {
        public Task<CwicPacketResult> GenerateAsync(int personId, CwicPacketRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class UnusedSafetyDeviceService : ISafetyDeviceService
    {
        public Task<SafetyDeviceResult> GenerateAsync(int personId, SafetyDeviceRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class UnusedHousingService : IHousingSupportFundsService
    {
        public Task<HousingSupportFundsResult> GenerateAsync(int personId,
            HousingSupportFundsRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class EmptySession : ISessionService
    {
        public User? CurrentUser => null;
        public void SetUser(User user) => throw new NotSupportedException();
    }
}
