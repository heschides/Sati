namespace Sati.Contracts.V1;

public enum ConsumerScheduleKind { DoctorAppointment = 1, DayProgram = 2, Work = 3 }

[Flags]
public enum ScheduleWeekdays
{
    None = 0, Monday = 1, Tuesday = 2, Wednesday = 4, Thursday = 8,
    Friday = 16, Saturday = 32, Sunday = 64
}

// These are staff-entered tracking states. Sati does not request or book a ride.
public enum ModivcareRideStatus { NoRide = 0, NeedsBooking = 1, Requested = 2, Confirmed = 3 }

public sealed record ConsumerScheduleEntryDto(
    int Id, int PersonId, ConsumerScheduleKind Kind, string Title, string? Location,
    DateTime? Date, DateTime? EffectiveStart, DateTime? EffectiveEnd,
    ScheduleWeekdays Weekdays, int? StartMinute, int? EndMinute,
    ModivcareRideStatus RideStatus, int? OutboundPickupMinute,
    int? ReturnPickupMinute, string? RideReference, int Revision);

public sealed record SaveConsumerScheduleEntryRequest(
    ConsumerScheduleKind Kind, string Title, string? Location,
    DateTime? Date, DateTime? EffectiveStart, DateTime? EffectiveEnd,
    ScheduleWeekdays Weekdays, int? StartMinute, int? EndMinute,
    ModivcareRideStatus RideStatus, int? OutboundPickupMinute,
    int? ReturnPickupMinute, string? RideReference, int ExpectedRevision);

public static class ConsumerScheduleRules
{
    public const int MaxTitleLength = 120;
    public const int MaxLocationLength = 160;
    public const int MaxRideReferenceLength = 80;
    private const ScheduleWeekdays AllWeekdays = (ScheduleWeekdays)127;

    public static Dictionary<string, string[]> Validate(SaveConsumerScheduleEntryRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (!Enum.IsDefined(request.Kind))
            errors["kind"] = ["Choose a schedule type."];
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length > MaxTitleLength)
            errors["title"] = [$"Enter a title of at most {MaxTitleLength} characters."];
        if (request.Location?.Trim().Length > MaxLocationLength)
            errors["location"] = [$"Location must not exceed {MaxLocationLength} characters."];
        if (request.StartMinute is < 0 or > 1439 || request.EndMinute is < 0 or > 1440 ||
            request.EndMinute is not null && request.StartMinute is null ||
            request.StartMinute is int startMinute && request.EndMinute is int endMinute &&
            endMinute <= startMinute ||
            (request.Kind is ConsumerScheduleKind.DayProgram or ConsumerScheduleKind.Work) &&
            (request.StartMinute is null || request.EndMinute is null))
            errors["time"] = ["Enter valid same-day hours; weekly schedules require start and end times."];
        if (request.Kind == ConsumerScheduleKind.DoctorAppointment)
        {
            if (request.Date is null || request.EffectiveStart is not null ||
                request.EffectiveEnd is not null || request.Weekdays != ScheduleWeekdays.None)
                errors["date"] = ["A doctor appointment needs one date, not weekly dates."];
        }
        else if (request.Kind is ConsumerScheduleKind.DayProgram or ConsumerScheduleKind.Work)
        {
            if (request.Date is not null || request.EffectiveStart is null ||
                request.Weekdays == ScheduleWeekdays.None ||
                (request.Weekdays & ~AllWeekdays) != ScheduleWeekdays.None ||
                request.EffectiveEnd < request.EffectiveStart)
                errors["weekdays"] = ["Enter a start date and at least one weekday; end date cannot precede start date."];
        }
        if (!Enum.IsDefined(request.RideStatus))
            errors["rideStatus"] = ["Choose a ModivCare ride status."];
        if (request.OutboundPickupMinute is < 0 or > 1439 ||
            request.ReturnPickupMinute is < 0 or > 1439)
            errors["rideTime"] = ["Ride pickup times must be within the day."];
        if (request.RideStatus == ModivcareRideStatus.NoRide &&
            (request.OutboundPickupMinute is not null || request.ReturnPickupMinute is not null ||
             !string.IsNullOrWhiteSpace(request.RideReference)))
            errors["rideStatus"] = ["Choose a ride status before adding pickup times or a reference."];
        if (request.RideReference?.Trim().Length > MaxRideReferenceLength)
            errors["rideReference"] = [$"Ride reference must not exceed {MaxRideReferenceLength} characters."];
        if (request.ExpectedRevision < 0)
            errors["expectedRevision"] = ["Reload this schedule entry before saving."];
        return errors;
    }
}
