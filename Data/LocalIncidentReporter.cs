using System.Reflection;
using System.Data;
using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Models;
using Sati.Services;

namespace Sati.Data;

public sealed class LocalIncidentReporter(
    IDbContextFactory<SatiContext> contextFactory,
    ISessionService sessionService) : IIncidentReporter
{
    private static readonly SemaphoreSlim[] Gates = Enumerable.Range(0, 32)
        .Select(_ => new SemaphoreSlim(1, 1))
        .ToArray();

    public async Task ReportAsync(
        Exception exception,
        string operation,
        string reference,
        string severity = IncidentSeverities.Error,
        CancellationToken cancellationToken = default) =>
        await ReportCoreAsync(exception, operation, reference, severity, null, cancellationToken);

    public async Task ReportCrashAsync(
        Exception exception,
        string operation,
        string reference,
        CrashDiagnosticDto diagnostic,
        string severity = IncidentSeverities.Critical,
        CancellationToken cancellationToken = default) =>
        await ReportCoreAsync(exception, operation, reference, severity, diagnostic, cancellationToken);

    private async Task ReportCoreAsync(
        Exception exception,
        string operation,
        string reference,
        string severity,
        CrashDiagnosticDto? diagnostic,
        CancellationToken cancellationToken)
    {
        var actor = sessionService.CurrentUser;
        if (actor is null)
            return;

        try
        {
            var fingerprint = AppErrorLog.CreateFingerprint(exception);
            var safeOperation = AppErrorLog.SafeArea(operation);
            var release = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "unknown";
            var occurredAt = DateTime.UtcNow;
            var diagnosticJson = diagnostic is null ? null : CrashDiagnosticRules.Serialize(diagnostic);
            var gate = Gates[(int)((uint)HashCode.Combine(
                actor.AgencyId,
                actor.Role == UserRole.PlatformOperator ? IncidentScopes.Platform : IncidentScopes.Agency,
                safeOperation,
                fingerprint) % Gates.Length)];
            await gate.WaitAsync(cancellationToken);
            try
            {
                await using var context = contextFactory.CreateDbContext();
                await using var transaction = await context.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    cancellationToken);
                var incident = await context.IncidentGroups.SingleOrDefaultAsync(candidate =>
                    candidate.AgencyId == actor.AgencyId &&
                    candidate.Scope == (actor.Role == UserRole.PlatformOperator ? IncidentScopes.Platform : IncidentScopes.Agency) &&
                    candidate.Source == "Desktop" &&
                    candidate.Operation == safeOperation &&
                    candidate.ExceptionFingerprint == fingerprint,
                    cancellationToken);
                if (incident is null)
                {
                    context.IncidentGroups.Add(new IncidentGroup
                    {
                        AgencyId = actor.AgencyId,
                        Scope = actor.Role == UserRole.PlatformOperator ? IncidentScopes.Platform : IncidentScopes.Agency,
                        Source = "Desktop",
                        Severity = severity,
                        Operation = safeOperation,
                        FirstRelease = release,
                        LastRelease = release,
                        ExceptionFingerprint = fingerprint,
                        FirstSeenUtc = occurredAt,
                        LastSeenUtc = occurredAt,
                        LastReference = reference,
                        LastActorRole = actor.Role.ToString(),
                        LastCrashDiagnosticJson = diagnosticJson
                    });
                }
                else
                {
                    if (incident.LastReference == reference)
                    {
                        incident.LastCrashDiagnosticJson = PreferDiagnostic(
                            incident.LastCrashDiagnosticJson,
                            diagnosticJson);
                        await context.SaveChangesAsync(cancellationToken);
                        await transaction.CommitAsync(cancellationToken);
                        return;
                    }

                    incident.Severity = MoreSevere(incident.Severity, severity);
                    incident.FirstSeenUtc = occurredAt < incident.FirstSeenUtc ? occurredAt : incident.FirstSeenUtc;
                    incident.LastRelease = release;
                    incident.LastSeenUtc = occurredAt > incident.LastSeenUtc ? occurredAt : incident.LastSeenUtc;
                    incident.LastReference = reference;
                    incident.LastActorRole = actor.Role.ToString();
                    if (diagnosticJson is not null)
                        incident.LastCrashDiagnosticJson = diagnosticJson;
                    incident.OccurrenceCount++;
                    if (incident.Status == "Resolved")
                        incident.Status = "Reopened";
                }
                await context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            finally
            {
                gate.Release();
            }
        }
        catch
        {
            // Error reporting must never replace or amplify the original failure.
        }
    }

    private static string? PreferDiagnostic(string? currentJson, string? reportedJson)
    {
        if (reportedJson is null)
            return currentJson;
        var current = CrashDiagnosticRules.Deserialize(currentJson);
        var reported = CrashDiagnosticRules.Deserialize(reportedJson);
        return reported is not null &&
               (current is null || CrashDiagnosticRules.Quality(reported.Status) > CrashDiagnosticRules.Quality(current.Status))
            ? reportedJson
            : currentJson;
    }

    private static string MoreSevere(string current, string reported)
    {
        static int Rank(string value) => value switch
        {
            IncidentSeverities.Critical => 3,
            IncidentSeverities.Error => 2,
            _ => 1
        };
        return Rank(reported) > Rank(current) ? reported : current;
    }
}
