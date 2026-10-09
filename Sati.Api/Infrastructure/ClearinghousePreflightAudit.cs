using System.Text.Json;
using Sati.Api.Data;
using Sati.Contracts.V1;
using Sati.Models.Billing;

namespace Sati.Api.Infrastructure;

internal static class ClearinghousePreflightAudit
{
    public static void RecordSystem(ApiDbContext db, ClearinghouseDispatchReadiness readiness,
        string action, DateTime nowUtc) => db.AuditEvents.Add(new ServerAuditEvent
        {
            AgencyId = readiness.AgencyId, ActorUserId = SystemActor.UserId,
            Action = action, ResourceType = "ClearinghouseAccount", ResourceId = readiness.AccountId.ToString("N"),
            OccurredAtUtc = nowUtc,
            MetadataJson = JsonSerializer.Serialize(new { actorKind = "system", job = "clearinghouse-preflight",
                readiness.AccountId, readiness.FailureCount, readiness.RecoveryCycleId, readiness.NextEligibleAtUtc,
                readiness.SafeFailureCode, readiness.Revision })
        });
}
