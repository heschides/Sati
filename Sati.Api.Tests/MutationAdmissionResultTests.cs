using System.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Sati.Api.Data;
using Sati.Api.Infrastructure;
using Xunit;

namespace Sati.Api.Tests;

public sealed partial class SessionAdmissionResultTests
{
    [Theory]
    [InlineData("null")]
    [InlineData("DBNull")]
    [InlineData("string")]
    [InlineData("long")]
    [InlineData("positive")]
    [InlineData("negative")]
    public async Task MutationMalformedScalarCannotEnterNext(string kind)
    {
        using var fixture = new MutationFixture(Scalar(kind));
        await Assert.ThrowsAsync<InvalidOperationException>(fixture.RunAsync);
        fixture.AssertFinished(false, 0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(-1)]
    public async Task MutationExactResultsKeepAdmissionAndBusyResponse(int result)
    {
        using var fixture = new MutationFixture(result);
        await fixture.RunAsync();
        fixture.AssertFinished(result != -1, result == -1 ? 0 : 1);
        Assert.Equal(result == -1 ? 503 : 200, fixture.Context.Response.StatusCode);
        if (result == -1)
        {
            fixture.Context.Response.Body.Position = 0;
            var body = await new StreamReader(fixture.Context.Response.Body).ReadToEndAsync();
            Assert.Contains("demo_reset_in_progress", body);
            Assert.Contains("synthetic-correlation", body);
        }
    }

    [Theory]
    [InlineData("success")]
    [InlineData("busy")]
    [InlineData("null")]
    public async Task MutationCancellationAfterScalarDoesNotEnterNext(string kind)
    {
        using var fixture = new MutationFixture(kind == "success" ? 0 : kind == "busy" ? -1 : null);
        fixture.Connection.AfterTargetScalar = fixture.Cancellation.Cancel;
        var failure = await Assert.ThrowsAnyAsync<OperationCanceledException>(fixture.RunAsync);
        Assert.Equal(fixture.Cancellation.Token, failure.CancellationToken);
        fixture.AssertFinished(false, kind == "success" ? 1 : 0);
        Assert.Equal(200, fixture.Context.Response.StatusCode); // no normal busy response began
    }

    [Theory]
    [InlineData("next")]
    [InlineData("scalar")]
    [InlineData("release")]
    public async Task MutationFailuresCloseOnlyOwnedRawConnection(string kind)
    {
        using var fixture = new MutationFixture(0) { NextFailure = kind == "next" };
        fixture.Connection.ScalarFailure = kind == "scalar";
        fixture.Connection.ReleaseFailure = kind == "release";
        if (kind == "next") await Assert.ThrowsAsync<CallbackException>(fixture.RunAsync);
        else await Assert.ThrowsAsync<SyntheticCommandException>(fixture.RunAsync);
        fixture.AssertFinished(kind != "scalar", kind == "scalar" ? 0 : 1);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    [InlineData("reset")]
    [InlineData("non-demo")]
    [InlineData("non-sql")]
    public async Task MutationExemptionsNeverCreateRawConnection(string exemption)
    {
        using var fixture = new MutationFixture(null);
        await fixture.RunAsync(exemption);
        Assert.True(fixture.Entered);
        Assert.Equal(0, fixture.Created);
        Assert.Equal(0, fixture.Connection.Opens);
        Assert.Empty(fixture.Connection.AcquiredResources);
    }

    private sealed class MutationFixture(object? scalar) : IDisposable
    {
        public SyntheticConnection Connection { get; } = new(scalar, 1);
        public CancellationTokenSource Cancellation { get; } = new();
        public DefaultHttpContext Context { get; } = new();
        public bool Entered { get; private set; }
        public bool NextFailure { get; init; }
        public int Created { get; private set; }

        public Task RunAsync() => RunAsync("");
        public async Task RunAsync(string exemption)
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["ConnectionStrings:SatiDemo"] = "synthetic-raw-connection" }).Build();
            var options = Options.Create(new SatiApiOptions
            { ExpectedEnvironment = exemption == "non-demo" ? "Production" : "Demo", ExpectedDatabaseName = "SatiDemo" });
            var dbOptions = new DbContextOptionsBuilder<ApiDbContext>();
            if (exemption == "non-sql") dbOptions.UseSqlite("Data Source=:memory:");
            else dbOptions.UseSqlServer("Server=synthetic.invalid;Database=SatiDemo;Integrated Security=True");
            await using var db = new ApiDbContext(dbOptions.Options); // metadata only; never opened
            Context.Request.Method = exemption is "GET" or "HEAD" ? exemption : "POST";
            Context.Request.Path = exemption == "reset" ? "/api/v1/admin/demo/reset" : "/api/v1/synthetic";
            Context.RequestAborted = Cancellation.Token;
            Context.TraceIdentifier = "synthetic-correlation";
            Context.Response.Body = new MemoryStream();
            var middleware = new DemoMutationLeaseMiddleware(_ =>
            {
                Entered = true;
                Assert.Equal(ConnectionState.Open, Connection.State);
                Assert.False(Connection.WasDisposed);
                if (NextFailure) throw new CallbackException();
                return Task.CompletedTask;
            }, connectionString =>
            {
                Assert.Equal("synthetic-raw-connection", connectionString);
                Created++;
                return Connection;
            });
            // Exempt callbacks have no raw connection to assert; retain the same factory.
            if (exemption.Length > 0)
                middleware = new DemoMutationLeaseMiddleware(_ => { Entered = true; return Task.CompletedTask; },
                    _ => { Created++; return Connection; });
            await middleware.InvokeAsync(Context, configuration, db, options);
        }

        public void AssertFinished(bool entered, int releases)
        {
            Assert.Equal(entered, Entered);
            Assert.Equal(1, Created);
            Assert.Equal(1, Connection.Opens);
            Assert.Equal(ConnectionState.Closed, Connection.State);
            Assert.True(Connection.WasDisposed);
            Assert.Equal(releases, Connection.Releases);
            Assert.All(Connection.ReleaseTokens, token => Assert.False(token.CanBeCanceled));
            Assert.Null(Connection.LastCommandTransaction);
            Assert.Contains("@LockMode=N'Shared'", Connection.LastCommandText, StringComparison.Ordinal);
            Assert.Contains("@LockOwner=N'Session'", Connection.LastCommandText, StringComparison.Ordinal);
            Assert.Contains("@LockTimeout=0", Connection.LastCommandText, StringComparison.Ordinal);
            Assert.Equal(new[] { DemoResetLease.Resource }, Connection.AcquiredResources);
        }

        public void Dispose() { Cancellation.Dispose(); Context.Response.Body.Dispose(); }
    }
}
