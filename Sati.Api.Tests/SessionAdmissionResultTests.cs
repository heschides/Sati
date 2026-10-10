using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sati.Api.Data;
using Sati.Api.Infrastructure;
using Xunit;

namespace Sati.Api.Tests;

// Exercises the production coordinators with SQL-provider identity and synthetic ADO results.
// Open/commands never reach a server; this is not a SqlClient fault or capacity simulation.
public sealed class SessionAdmissionResultTests
{
    public static TheoryData<string, string> InvalidResults()
    {
        var data = new TheoryData<string, string>();
        foreach (var path in Paths)
            foreach (var result in new[] { "null", "DBNull", "string", "long", "positive", "negative" })
                data.Add(path, result);
        return data;
    }

    public static TheoryData<string, int> ValidResults()
    {
        var data = new TheoryData<string, int>();
        foreach (var path in Paths)
            foreach (var result in new[] { 0, 1 }) data.Add(path, result);
        return data;
    }

    public static TheoryData<string> AllPaths()
    {
        var data = new TheoryData<string>();
        foreach (var path in Paths) data.Add(path);
        return data;
    }

    private static readonly string[] Paths = ["reset", "dispatch", "account", "poll", "request", "note-reset", "note-sweep"];

    [Theory, MemberData(nameof(InvalidResults))]
    public async Task MalformedAdmissionNeverEntersCallbackAndDisposesConnection(string path, string result)
    {
        var fixture = new Fixture(path, Scalar(result));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.RunAsync());
        Assert.False(fixture.Entered);
        fixture.AssertClosed();
        Assert.Equal(path == "note-sweep" ? 1 : 0, fixture.Connection.Releases);
    }

    [Theory, MemberData(nameof(ValidResults))]
    public async Task ExactSuccessAdmitsAndReleasesExistingLockSequence(string path, int result)
    {
        var fixture = new Fixture(path, result);
        Assert.Equal(7, await fixture.RunAsync());
        Assert.True(fixture.Entered);
        fixture.AssertClosed();
        Assert.Equal(path.StartsWith("note-", StringComparison.Ordinal) ? 2 : 1, fixture.Connection.Releases);
        Assert.All(fixture.Connection.ReleaseTokens, token => Assert.False(token.CanBeCanceled));
        if (path.StartsWith("note-", StringComparison.Ordinal))
        {
            Assert.Equal(new[] { DemoResetLease.Resource, "Sati.NoteAbandonmentSweep" }, fixture.Connection.AcquiredResources);
            Assert.Equal(new[] { "Sati.NoteAbandonmentSweep", DemoResetLease.Resource }, fixture.Connection.ReleasedResources);
        }
    }

    [Theory, MemberData(nameof(AllPaths))]
    public async Task CancellationAfterScalarWinsBeforeCallback(string path)
    {
        using var cancellation = new CancellationTokenSource();
        var fixture = new Fixture(path, 0);
        fixture.Connection.AfterTargetScalar = cancellation.Cancel;
        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.RunAsync(cancellation.Token));
        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.False(fixture.Entered);
        fixture.AssertClosed();
        Assert.Equal(path == "note-sweep" ? 2 : 1, fixture.Connection.Releases);
    }

    [Theory, MemberData(nameof(AllPaths))]
    public async Task CancellationTakesPrecedenceOverMalformedScalar(string path)
    {
        using var cancellation = new CancellationTokenSource();
        var fixture = new Fixture(path, null);
        fixture.Connection.AfterTargetScalar = cancellation.Cancel;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.RunAsync(cancellation.Token));
        Assert.False(fixture.Entered);
        fixture.AssertClosed();
    }

    [Theory, MemberData(nameof(AllPaths))]
    public async Task KnownContentionKeepsExistingOutcomeAndUnwindsEarlierLocks(string path)
    {
        var fixture = new Fixture(path, -1);
        if (path == "request")
            await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.RunAsync());
        else Assert.Equal(0, await fixture.RunAsync());
        Assert.False(fixture.Entered);
        fixture.AssertClosed();
        Assert.Equal(path == "note-sweep" ? 1 : 0, fixture.Connection.Releases);
    }

    [Theory, MemberData(nameof(AllPaths))]
    public async Task CallbackFailureStillReleasesAndClosesOwnedConnection(string path)
    {
        var fixture = new Fixture(path, 0) { CallbackFailure = true };
        await Assert.ThrowsAsync<CallbackException>(() => fixture.RunAsync());
        Assert.True(fixture.Entered);
        fixture.AssertClosed();
        Assert.Equal(path.StartsWith("note-", StringComparison.Ordinal) ? 2 : 1, fixture.Connection.Releases);
    }

    [Theory, MemberData(nameof(AllPaths))]
    public async Task CommandFailureDoesNotEnterCallbackAndUnwindsEarlierLocks(string path)
    {
        var fixture = new Fixture(path, 0);
        fixture.Connection.ScalarFailure = true;
        await Assert.ThrowsAsync<SyntheticCommandException>(() => fixture.RunAsync());
        Assert.False(fixture.Entered);
        fixture.AssertClosed();
        Assert.Equal(path == "note-sweep" ? 1 : 0, fixture.Connection.Releases);
    }

    [Theory, MemberData(nameof(AllPaths))]
    public async Task ReleaseFailureStillDisposesOwnedConnection(string path)
    {
        var fixture = new Fixture(path, 0);
        fixture.Connection.ReleaseFailure = true;
        await Assert.ThrowsAsync<SyntheticCommandException>(() => fixture.RunAsync());
        Assert.True(fixture.Entered);
        fixture.AssertClosed();
        Assert.Equal(path.StartsWith("note-", StringComparison.Ordinal) ? 2 : 1, fixture.Connection.Releases);
    }

    private static object? Scalar(string kind) => kind switch
    {
        "null" => null, "DBNull" => DBNull.Value, "string" => "0", "long" => 0L,
        "positive" => 2, "negative" => -2, _ => throw new ArgumentException(kind)
    };

    private sealed class Fixture : IDbContextFactory<ApiDbContext>
    {
        private readonly string path;
        public SyntheticConnection Connection { get; }
        public bool Entered { get; private set; }
        public bool CallbackFailure { get; init; }
        public Fixture(string path, object? result)
        {
            this.path = path;
            Connection = new SyntheticConnection(result, path == "note-sweep" ? 2 : 1);
        }

        public ApiDbContext CreateDbContext() => new(new DbContextOptionsBuilder<ApiDbContext>()
            .UseSqlServer(Connection, contextOwnsConnection: true).Options);
        public Task<ApiDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());

        public async Task<int> RunAsync(CancellationToken token = default)
        {
            var options = Options.Create(new SatiApiOptions { ExpectedEnvironment = "Demo", ExpectedDatabaseName = "SatiDemo" });
            var demo = new SqlDemoWorkerResetCoordination(this, options);
            return path switch
            {
                "reset" => await demo.RunAsync(Callback, 0, token),
                "dispatch" => await demo.RunDispatchAsync(Guid.Parse("11111111-1111-1111-1111-111111111111"), Callback, 0, token),
                "account" => await demo.RunAccountPreflightAsync(1, Guid.Parse("22222222-2222-2222-2222-222222222222"), (_, ct) => Callback(ct), 0, token),
                "poll" => await new SqlClaimMdSandboxCoordination(this).PollOnceAsync(Callback, token),
                "request" => await new SqlClaimMdSandboxCoordination(this).RequestAsync(Callback, token),
                "note-reset" or "note-sweep" => await new SqlNoteAbandonmentCoordination(this, options)
                    .RunOnceAsync(async ct => { await Callback(ct); }, token) ? 7 : 0,
                _ => throw new ArgumentException(path)
            };
        }

        private Task<int> Callback(CancellationToken token)
        {
            Entered = true;
            if (CallbackFailure) throw new CallbackException();
            return Task.FromResult(7);
        }

        public void AssertClosed()
        {
            Assert.Equal(1, Connection.Opens);
            Assert.Equal(ConnectionState.Closed, Connection.State);
            Assert.True(Connection.WasDisposed);
        }
    }

    private sealed class CallbackException : Exception;
    private sealed class SyntheticCommandException : Exception;

    private sealed class SyntheticConnection(object? scalar, int targetScalar) : DbConnection
    {
        private readonly object? result = scalar;
        private readonly int target = targetScalar;
        private ConnectionState state;
        private int scalarCalls;
        [AllowNull] public override string ConnectionString { get; set; } = "Server=synthetic.invalid;Database=SatiDemo;Integrated Security=True;TrustServerCertificate=True";
        public override string Database => "SatiDemo";
        public override string DataSource => "synthetic.invalid";
        public override string ServerVersion => "16.0";
        public override ConnectionState State => state;
        public int Opens { get; private set; }
        public int Releases { get; private set; }
        public bool WasDisposed { get; private set; }
        public Action? AfterTargetScalar { get; set; }
        public bool ScalarFailure { get; set; }
        public bool ReleaseFailure { get; set; }
        public List<string> AcquiredResources { get; } = [];
        public List<string> ReleasedResources { get; } = [];
        public List<CancellationToken> ReleaseTokens { get; } = [];
        public override void Open() { Opens++; state = ConnectionState.Open; }
        public override Task OpenAsync(CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); Open(); return Task.CompletedTask; }
        public override void Close() => state = ConnectionState.Closed;
        public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotSupportedException();
        protected override DbCommand CreateDbCommand() => new SyntheticCommand(this);
        protected override void Dispose(bool disposing) { WasDisposed = true; Close(); base.Dispose(disposing); }

        private sealed class SyntheticCommand(SyntheticConnection connection) : DbCommand
        {
            private readonly Parameters parameters = new();
            [AllowNull] public override string CommandText { get; set; } = "";
            public override int CommandTimeout { get; set; }
            public override CommandType CommandType { get; set; }
            public override bool DesignTimeVisible { get; set; }
            public override UpdateRowSource UpdatedRowSource { get; set; }
            protected override DbConnection? DbConnection { get; set; } = connection;
            protected override DbTransaction? DbTransaction { get; set; }
            protected override DbParameterCollection DbParameterCollection => parameters;
            protected override DbParameter CreateDbParameter() => new SqlParameter();
            public override void Cancel() { }
            public override void Prepare() { }
            protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => throw new NotSupportedException();
            public override object? ExecuteScalar() => throw new NotSupportedException();
            public override int ExecuteNonQuery() => throw new NotSupportedException();
            public override Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken)
            {
                Assert.Contains("sp_getapplock", CommandText, StringComparison.Ordinal);
                connection.AcquiredResources.Add((string)parameters["@resource"].Value!);
                if (++connection.scalarCalls != connection.target) return Task.FromResult<object?>(0);
                if (connection.ScalarFailure) throw new SyntheticCommandException();
                connection.AfterTargetScalar?.Invoke();
                return Task.FromResult(connection.result);
            }
            public override Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken)
            {
                Assert.Contains("sp_releaseapplock", CommandText, StringComparison.Ordinal);
                connection.Releases++;
                connection.ReleasedResources.Add((string)parameters["@resource"].Value!);
                connection.ReleaseTokens.Add(cancellationToken);
                if (connection.ReleaseFailure) throw new SyntheticCommandException();
                return Task.FromResult(0);
            }
        }

        private sealed class Parameters : DbParameterCollection
        {
            private readonly List<DbParameter> items = [];
            public override int Count => items.Count;
            public override object SyncRoot => this;
            public override int Add(object value) { items.Add((DbParameter)value); return items.Count - 1; }
            public override void AddRange(Array values) { foreach (var value in values) Add(value!); }
            public override void Clear() => items.Clear();
            public override bool Contains(object value) => items.Contains((DbParameter)value);
            public override bool Contains(string value) => IndexOf(value) >= 0;
            public override void CopyTo(Array array, int index) => ((System.Collections.ICollection)items).CopyTo(array, index);
            public override System.Collections.IEnumerator GetEnumerator() => items.GetEnumerator();
            public override int IndexOf(object value) => items.IndexOf((DbParameter)value);
            public override int IndexOf(string parameterName) => items.FindIndex(item => item.ParameterName == parameterName);
            public override void Insert(int index, object value) => items.Insert(index, (DbParameter)value);
            public override void Remove(object value) => items.Remove((DbParameter)value);
            public override void RemoveAt(int index) => items.RemoveAt(index);
            public override void RemoveAt(string parameterName) => items.RemoveAt(IndexOf(parameterName));
            protected override DbParameter GetParameter(int index) => items[index];
            protected override DbParameter GetParameter(string parameterName) => items[IndexOf(parameterName)];
            protected override void SetParameter(int index, DbParameter value) => items[index] = value;
            protected override void SetParameter(string parameterName, DbParameter value) => SetParameter(IndexOf(parameterName), value);
        }
    }
}
