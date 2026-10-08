using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Sati.Api.Data;
using Sati.Contracts.V1;

namespace Sati.Api.Infrastructure;

internal sealed record IncidentAggregation(
    int AgencyId,
    string Scope,
    string Source,
    string Severity,
    string Operation,
    string Release,
    string Fingerprint,
    DateTime OccurredAtUtc,
    string Reference,
    string ActorRole,
    CrashDiagnosticDto? CrashDiagnostic);

internal sealed class IncidentAggregator(IDbContextFactory<ApiDbContext> contextFactory)
{
    // Bounded lock striping prevents an attacker from growing an unbounded dictionary.
    // The database transaction remains the cross-process authority; these gates merely
    // avoid needless unique-key races within one API process.
    private static readonly SemaphoreSlim[] Gates = Enumerable.Range(0, 64)
        .Select(_ => new SemaphoreSlim(1, 1))
        .ToArray();

    public async Task<ServerIncidentGroup> UpsertAsync(
        IncidentAggregation report,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // EF keeps the outermost strategy: a nested zero-retry scope cannot
        // prevent an active retrying caller from replaying an ambiguous write.
        if (ExecutionStrategy.Current?.RetriesOnFailure == true)
            throw new InvalidOperationException("Incident aggregation cannot run inside a retrying execution scope.");

        var gate = Gates[(int)((uint)HashCode.Combine(
            report.AgencyId,
            report.Scope,
            report.Source,
            report.Operation,
            report.Fingerprint) % Gates.Length)];
        await gate.WaitAsync(cancellationToken);
        try
        {
            var diagnosticJson = report.CrashDiagnostic is null
                ? null
                : CrashDiagnosticRules.Serialize(report.CrashDiagnostic);
            await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
            return await new IncidentSingleAttempt(db).ExecuteAsync<IncidentAggregation, ServerIncidentGroup>(
                report, async (_, currentReport, token) =>
                {
                    await using var transaction = await db.Database.BeginTransactionAsync(
                        IsolationLevel.Serializable,
                        token);
                    var query = db.Database.IsSqlServer()
                        ? db.IncidentGroups.FromSqlInterpolated($"""
                            SELECT * FROM [IncidentGroups] WITH (UPDLOCK, HOLDLOCK)
                            WHERE [AgencyId] = {currentReport.AgencyId}
                              AND [Scope] = {currentReport.Scope}
                              AND [Source] = {currentReport.Source}
                              AND [Operation] = {currentReport.Operation}
                              AND [ExceptionFingerprint] = {currentReport.Fingerprint}
                            """)
                        : db.IncidentGroups.Where(candidate =>
                            candidate.AgencyId == currentReport.AgencyId &&
                            candidate.Scope == currentReport.Scope &&
                            candidate.Source == currentReport.Source &&
                            candidate.Operation == currentReport.Operation &&
                            candidate.ExceptionFingerprint == currentReport.Fingerprint);
                    var incident = await query.SingleOrDefaultAsync(token);
                    if (incident is null)
                    {
                        incident = new ServerIncidentGroup
                        {
                            AgencyId = currentReport.AgencyId,
                            Scope = currentReport.Scope,
                            Source = currentReport.Source,
                            Severity = currentReport.Severity,
                            Operation = currentReport.Operation,
                            FirstRelease = currentReport.Release,
                            LastRelease = currentReport.Release,
                            ExceptionFingerprint = currentReport.Fingerprint,
                            Status = "Open",
                            OccurrenceCount = 1,
                            FirstSeenUtc = currentReport.OccurredAtUtc,
                            LastSeenUtc = currentReport.OccurredAtUtc,
                            LastReference = currentReport.Reference,
                            LastActorRole = currentReport.ActorRole,
                            LastCrashDiagnosticJson = diagnosticJson
                        };
                        db.IncidentGroups.Add(incident);
                    }
                    else
                    {
                        // An immediate replay of the retained last reference may enrich
                        // diagnostics without another occurrence. Older references are
                        // not a durable deduplication history in this aggregate.
                        if (incident.LastReference == currentReport.Reference)
                        {
                            incident.LastCrashDiagnosticJson = PreferDiagnostic(
                                incident.LastCrashDiagnosticJson,
                                diagnosticJson);
                            await db.SaveChangesAsync(token);
                            await transaction.CommitAsync(token);
                            return incident;
                        }

                        incident.Severity = MoreSevere(incident.Severity, currentReport.Severity);
                        incident.FirstSeenUtc = currentReport.OccurredAtUtc < incident.FirstSeenUtc
                            ? currentReport.OccurredAtUtc
                            : incident.FirstSeenUtc;
                        incident.LastRelease = currentReport.Release;
                        incident.LastSeenUtc = currentReport.OccurredAtUtc > incident.LastSeenUtc
                            ? currentReport.OccurredAtUtc
                            : incident.LastSeenUtc;
                        incident.LastReference = currentReport.Reference;
                        incident.LastActorRole = currentReport.ActorRole;
                        if (diagnosticJson is not null)
                            incident.LastCrashDiagnosticJson = diagnosticJson;
                        incident.OccurrenceCount++;
                        if (incident.Status == "Resolved")
                            incident.Status = "Reopened";
                    }

                    await db.SaveChangesAsync(token);
                    await transaction.CommitAsync(token);
                    return incident;
                }, verifySucceeded: null, cancellationToken: cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private sealed class IncidentSingleAttempt(ApiDbContext context) : ExecutionStrategy(context, 0, TimeSpan.Zero)
    {
        protected override bool ShouldRetryOn(Exception exception) => false;
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
            "Critical" => 3,
            "Error" => 2,
            _ => 1
        };
        return Rank(reported) > Rank(current) ? reported : current;
    }
}
