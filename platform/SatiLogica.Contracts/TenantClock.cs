namespace SatiLogica.Contracts;

/// <summary>The shared owner of tenant-local dates and wall times.</summary>
public sealed class TenantClock
{
    public const string MaineTimeZoneId = "America/New_York";
    private static readonly TimeZoneInfo MaineZone =
        TimeZoneInfo.FindSystemTimeZoneById(MaineTimeZoneId);
    private readonly TimeZoneInfo _zone;
    private readonly TimeProvider _timeProvider;

    public TenantClock(string timeZoneId, TimeProvider timeProvider)
    {
        _zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public DateTimeOffset UtcNow => _timeProvider.GetUtcNow();

    public DateTime Today => DateAt(UtcNow);

    public DateTime Now => TimeZoneInfo.ConvertTime(UtcNow, _zone).DateTime;

    public DateTime DateAt(DateTimeOffset utcInstant) =>
        TimeZoneInfo.ConvertTime(utcInstant, _zone).Date;

    public DateTime ToLocalDate(DateTime utcInstant) =>
        DateAt(new DateTimeOffset(DateTime.SpecifyKind(utcInstant, DateTimeKind.Utc)));

    public static DateTime MaineDate(DateTimeOffset utcInstant) =>
        TimeZoneInfo.ConvertTime(utcInstant, MaineZone).Date;

    public static DateTime MaineDate(DateTime utcInstant) =>
        MaineDate(new DateTimeOffset(DateTime.SpecifyKind(utcInstant, DateTimeKind.Utc)));
}
