using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Sati.ViewModels.Children;
using Xunit;

namespace Sati.Tests;

public sealed class ConsumerScheduleViewModelTests
{
    [Fact]
    public async Task DateOnlyDoctorVisitSavesWithoutInventingHours()
    {
        var service = new StubService();
        var model = new ConsumerScheduleViewModel(service, new SessionService());
        model.SetPerson(Person.Rehydrate(40, 1));
        model.Title = "Doctor visit";
        model.RideStatus = ModivcareRideStatus.NeedsBooking;

        await model.SaveCommand.ExecuteAsync(null);

        var saved = Assert.Single(service.Saves);
        Assert.Null(saved.StartMinute);
        Assert.Null(saved.EndMinute);
        Assert.Equal(ModivcareRideStatus.NeedsBooking, saved.RideStatus);
    }

    [Fact]
    public async Task ADateInTheTimeFieldIsRejectedBeforeWriting()
    {
        var service = new StubService();
        var model = new ConsumerScheduleViewModel(service, new SessionService());
        model.SetPerson(Person.Rehydrate(40, 1));
        model.Title = "Doctor visit";
        model.StartTime = "tomorrow";

        await model.SaveCommand.ExecuteAsync(null);

        Assert.Empty(service.Saves);
        Assert.Contains("Enter times", model.StatusMessage);
    }

    [Fact]
    public void SwitchingConsumerClearsCalendarBeforePublishingNewActivities()
    {
        var service = new StubService
        {
            OnGet = personId =>
            [
                new ConsumerScheduleEntryDto(personId, personId,
                    ConsumerScheduleKind.DoctorAppointment, $"Visit {personId}", null,
                    DateTime.Today, null, null, ScheduleWeekdays.None, null, null,
                    ModivcareRideStatus.NoRide, null, null, null, 1)
            ]
        };
        var model = new ConsumerScheduleViewModel(service, new SessionService());

        model.SetPerson(Person.Rehydrate(40, 1));
        Assert.Contains(model.CalendarDays.SelectMany(x => x.Activities),
            x => x.Entry.PersonId == 40);

        model.SetPerson(Person.Rehydrate(41, 1));

        Assert.DoesNotContain(model.CalendarDays.SelectMany(x => x.Activities),
            x => x.Entry.PersonId == 40);
        Assert.Contains(model.CalendarDays.SelectMany(x => x.Activities),
            x => x.Entry.PersonId == 41);

        var originalMonth = model.CalendarMonth;
        model.NextCalendarMonthCommand.Execute(null);
        Assert.Equal(originalMonth.AddMonths(1), model.CalendarMonth);
        Assert.Empty(model.SelectedDayActivities);

        model.PreviousCalendarMonthCommand.Execute(null);
        var today = Assert.Single(model.CalendarDays,
            x => x.Date == DateTime.Today);
        var stableDays = model.CalendarDays.ToArray();
        model.SelectCalendarDayCommand.Execute(today);
        Assert.Equal(stableDays, model.CalendarDays);
        Assert.True(today.IsSelected);
        Assert.Contains("selected", today.AutomationName);
        Assert.Equal(41, Assert.Single(model.SelectedDayActivities).Entry.PersonId);
    }

    private sealed class StubService : IConsumerScheduleService
    {
        public List<SaveConsumerScheduleEntryRequest> Saves { get; } = [];
        public Func<int, IReadOnlyList<ConsumerScheduleEntryDto>>? OnGet { get; init; }

        public Task<IReadOnlyList<ConsumerScheduleEntryDto>> GetAsync(int personId) =>
            Task.FromResult(OnGet?.Invoke(personId) ?? []);

        public Task<ConsumerScheduleEntryDto> SaveAsync(int personId, int? entryId,
            SaveConsumerScheduleEntryRequest request)
        {
            Saves.Add(request);
            return Task.FromResult(new ConsumerScheduleEntryDto(1, personId, request.Kind,
                request.Title, request.Location, request.Date, request.EffectiveStart,
                request.EffectiveEnd, request.Weekdays, request.StartMinute, request.EndMinute,
                request.RideStatus, request.OutboundPickupMinute, request.ReturnPickupMinute,
                request.RideReference, 1));
        }

        public Task DeleteAsync(int personId, int entryId, int expectedRevision) =>
            throw new NotSupportedException();
    }
}
