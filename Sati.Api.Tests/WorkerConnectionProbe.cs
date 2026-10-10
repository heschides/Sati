using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Sati.Api.Tests;

// Logical ADO connection evidence only: successful disposal is not physical pool eviction.
// Failed/cancelled open or unconfirmed close remains charged until a completion event.
internal sealed class WorkerConnectionProbe : DbConnectionInterceptor
{
    private readonly object sync = new();
    private readonly Dictionary<DbConnection, int> identities = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<DbConnection> held = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<DbConnection> attempted = new(ReferenceEqualityComparer.Instance);
    private readonly List<string> events = [];
    private int peak;
    private int attempts;

    internal int Held { get { lock (sync) return held.Count; } }
    internal int Peak { get { lock (sync) return peak; } }
    internal int Attempts { get { lock (sync) return attempts; } }
    internal int SeenConnections { get { lock (sync) return identities.Count; } }
    internal int AttemptedConnections { get { lock (sync) return attempted.Count; } }
    internal string[] Events { get { lock (sync) return events.ToArray(); } }

    internal void ResetEvidence()
    {
        lock (sync)
        {
            if (held.Count != 0) throw new InvalidOperationException("Cannot discard held connection evidence.");
            identities.Clear(); attempted.Clear(); events.Clear(); peak = 0; attempts = 0;
        }
    }

    internal void Observe(DbConnection connection, string kind)
    {
        lock (sync)
        {
            if (!identities.TryGetValue(connection, out var id)) identities.Add(connection, id = identities.Count + 1);
            if (kind == "opening") { attempts++; attempted.Add(connection); held.Add(connection); }
            else if (kind is "opened" or "failed" or "cancelled") held.Add(connection);
            else if (kind is "closed" or "disposed") held.Remove(connection);
            peak = Math.Max(peak, held.Count);
            events.Add($"{kind}:{id}:held={held.Count}");
        }
    }

    public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
    { Observe(connection, "opening"); return result; }
    public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData,
        InterceptionResult result, CancellationToken cancellationToken = default)
    { Observe(connection, "opening"); return ValueTask.FromResult(result); }
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData) => Observe(connection, "opened");
    public override Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    { Observe(connection, "opened"); return Task.CompletedTask; }
    public override InterceptionResult ConnectionClosing(DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
    { Observe(connection, "closing"); return result; }
    public override ValueTask<InterceptionResult> ConnectionClosingAsync(DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
    { Observe(connection, "closing"); return ValueTask.FromResult(result); }
    public override void ConnectionClosed(DbConnection connection, ConnectionEndEventData eventData) => Observe(connection, "closed");
    public override Task ConnectionClosedAsync(DbConnection connection, ConnectionEndEventData eventData)
    { Observe(connection, "closed"); return Task.CompletedTask; }
    public override InterceptionResult ConnectionDisposing(DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
    { Observe(connection, "disposing"); return result; }
    public override ValueTask<InterceptionResult> ConnectionDisposingAsync(DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
    { Observe(connection, "disposing"); return ValueTask.FromResult(result); }
    public override void ConnectionDisposed(DbConnection connection, ConnectionEndEventData eventData) => Observe(connection, "disposed");
    public override Task ConnectionDisposedAsync(DbConnection connection, ConnectionEndEventData eventData)
    { Observe(connection, "disposed"); return Task.CompletedTask; }
    public override void ConnectionFailed(DbConnection connection, ConnectionErrorEventData eventData) => Observe(connection, "failed");
    public override Task ConnectionFailedAsync(DbConnection connection, ConnectionErrorEventData eventData, CancellationToken cancellationToken = default)
    { Observe(connection, "failed"); return Task.CompletedTask; }
    public override void ConnectionCanceled(DbConnection connection, ConnectionEndEventData eventData) => Observe(connection, "cancelled");
    public override Task ConnectionCanceledAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    { Observe(connection, "cancelled"); return Task.CompletedTask; }
}
