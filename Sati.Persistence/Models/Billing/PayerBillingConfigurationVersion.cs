using System.Text.Json;
using Sati.Contracts.V1;

namespace Sati.Models.Billing;

public sealed class PayerBillingConfigurationVersion
{
    public Guid VersionId { get; private set; }
    public int AgencyId { get; private set; }
    public string ProfileKey { get; private set; } = "";
    public long Revision { get; private set; }
    public DateTime EffectiveOn { get; private set; }
    public string ConfigurationJson { get; private set; } = "";
    public int CreatedByUserId { get; private set; }
    public DateTime RecordedAtUtc { get; private set; }
    private PayerBillingConfigurationVersion() { }

    public static PayerBillingConfigurationVersion Create(AgencyActor actor, PublishPayerBillingRequest request,
        long revision, DateTime recordedAtUtc)
    {
        if (!PayerBillingRules.CanPublish(actor)) throw new UnauthorizedAccessException("Only an administrator can publish payer configuration.");
        var errors = PayerBillingRules.Validate(request.Configuration);
        if (errors.Count > 0 || request.ChangeId == Guid.Empty || revision <= 0 || recordedAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("A validated configuration, change identity, revision and UTC timestamp are required.");
        return new() { VersionId = request.ChangeId, AgencyId = actor.AgencyId, ProfileKey = request.Configuration.ProfileKey,
            Revision = revision, EffectiveOn = request.Configuration.EffectiveOn,
            ConfigurationJson = JsonSerializer.Serialize(request.Configuration), CreatedByUserId = actor.UserId, RecordedAtUtc = recordedAtUtc };
    }

    public PayerBillingVersionDto ToDto() => new(VersionId, AgencyId, Revision,
        JsonSerializer.Deserialize<PayerBillingConfiguration>(ConfigurationJson)
            ?? throw new InvalidOperationException("Unreadable payer configuration."), CreatedByUserId, DateTime.SpecifyKind(RecordedAtUtc, DateTimeKind.Utc));
}
