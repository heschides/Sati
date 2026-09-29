using Microsoft.Extensions.Options;
using SatiLogica.Contracts;

namespace Sati.Api.Infrastructure;

internal sealed class ApiClock(IOptions<SatiApiOptions> options, TimeProvider timeProvider)
{
    private readonly TenantClock _clock = new(options.Value.TimeZoneId, timeProvider);

    public DateTime Today => _clock.Today;

    // Agency-local wall clock, including time of day. Anything a person READS as
    // a time — a journal stamp, for one — has to come from here: the host's own
    // local time is UTC in Azure and would present hours off the clock the case
    // manager just looked at. Stored instants stay UTC.
    public DateTime Now => _clock.Now;

    public DateTimeOffset UtcNow => _clock.UtcNow;

    public DateTime ToAgencyDate(DateTime utcInstant) => _clock.ToLocalDate(utcInstant);
}
