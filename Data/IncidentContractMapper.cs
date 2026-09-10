using Sati.Contracts.V1;
using Sati.Models;

namespace Sati.Data;

internal static class IncidentContractMapper
{
    public static IncidentGroupDto ToDto(IncidentGroup incident) => new(
        incident.Id,
        incident.AgencyId,
        incident.Scope,
        incident.Source,
        incident.Severity,
        incident.Operation,
        incident.FirstRelease,
        incident.LastRelease,
        incident.ExceptionFingerprint,
        incident.Status,
        incident.OccurrenceCount,
        incident.FirstSeenUtc,
        incident.LastSeenUtc,
        incident.LastReference,
        incident.LastActorRole,
        CrashDiagnosticRules.Deserialize(incident.LastCrashDiagnosticJson));
}
