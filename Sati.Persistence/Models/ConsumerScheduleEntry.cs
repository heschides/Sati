using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;

namespace Sati.Models;

public sealed class ConsumerScheduleEntry
{
    public int Id { get; set; }
    public int PersonId { get; set; }
    public ConsumerScheduleKind Kind { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Location { get; set; }
    public DateTime? Date { get; set; }
    public DateTime? EffectiveStart { get; set; }
    public DateTime? EffectiveEnd { get; set; }
    public ScheduleWeekdays Weekdays { get; set; }
    public int? StartMinute { get; set; }
    public int? EndMinute { get; set; }
    public ModivcareRideStatus RideStatus { get; set; }
    public int? OutboundPickupMinute { get; set; }
    public int? ReturnPickupMinute { get; set; }
    public string? RideReference { get; set; }
    public int Revision { get; set; }

    public void Apply(SaveConsumerScheduleEntryRequest request)
    {
        Kind = request.Kind;
        Title = request.Title.Trim();
        Location = Normalize(request.Location);
        Date = request.Date?.Date;
        EffectiveStart = request.EffectiveStart?.Date;
        EffectiveEnd = request.EffectiveEnd?.Date;
        Weekdays = request.Weekdays;
        StartMinute = request.StartMinute;
        EndMinute = request.EndMinute;
        RideStatus = request.RideStatus;
        OutboundPickupMinute = request.OutboundPickupMinute;
        ReturnPickupMinute = request.ReturnPickupMinute;
        RideReference = Normalize(request.RideReference);
        Revision++;
    }

    public ConsumerScheduleEntryDto ToDto() => new(Id, PersonId, Kind, Title, Location,
        Date, EffectiveStart, EffectiveEnd, Weekdays, StartMinute, EndMinute,
        RideStatus, OutboundPickupMinute, ReturnPickupMinute, RideReference, Revision);

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public static class ConsumerSchedulePersistenceModel
{
    public static void Configure<TPerson>(ModelBuilder modelBuilder) where TPerson : class
    {
        modelBuilder.Entity<ConsumerScheduleEntry>(entity =>
        {
            entity.ToTable("ConsumerScheduleEntries");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Title).HasMaxLength(ConsumerScheduleRules.MaxTitleLength).IsRequired();
            entity.Property(x => x.Location).HasMaxLength(ConsumerScheduleRules.MaxLocationLength);
            entity.Property(x => x.RideReference).HasMaxLength(ConsumerScheduleRules.MaxRideReferenceLength);
            entity.Property(x => x.Date).HasColumnType("date");
            entity.Property(x => x.EffectiveStart).HasColumnType("date");
            entity.Property(x => x.EffectiveEnd).HasColumnType("date");
            entity.Property(x => x.Revision).IsConcurrencyToken();
            entity.HasIndex(x => new { x.PersonId, x.Kind, x.Date });
            entity.HasOne<TPerson>().WithMany().HasForeignKey(x => x.PersonId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
