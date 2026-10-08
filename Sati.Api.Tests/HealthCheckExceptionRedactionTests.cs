using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sati.Api.Data;
using Sati.Api.Infrastructure;
using Xunit;

namespace Sati.Api.Tests;

// The faulting factory fails before EF/provider logging. These tests cover the two
// application checks, their HealthCheckResult/HealthReportEntry objects, and the
// framework HealthCheckService ILogger sink. They do not establish global health,
// provider, startup, registration-construction, or response-callback redaction.
public sealed class HealthCheckExceptionRedactionTests
{
    [Theory]
    [InlineData("schema_drift")]
    [InlineData("database_identity")]
    public async Task DirectProbeFailureReturnsContentFreeUnhealthyWithoutTheOriginalException(string checkName)
    {
        var fault = new ProbeFault();
        var factory = new ThrowingFactory(fault.Exception);
        using var logs = new HealthLogCapture();
        using var provider = BuildHealthServices(factory, logs);

        var result = await Check(provider, checkName).CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(1, factory.Attempts);
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal(FailureDescription(checkName), result.Description);
        Assert.Empty(result.Data);
        Assert.Null(result.Exception);
        Assert.DoesNotContain(fault.Sentinel, result.Description!);
        if (checkName == "schema_drift")
        {
            Assert.Contains(logs.Entries, entry => entry.Level == LogLevel.Error &&
                entry.Category == typeof(SchemaDriftHealthCheck).FullName);
            AssertSafeLogs(logs, fault);
        }
    }

    [Theory]
    [InlineData("schema_drift")]
    [InlineData("database_identity")]
    public async Task FrameworkHealthServiceLogsVisibleSafeFailureWithoutExceptionPayload(string checkName)
    {
        var fault = new ProbeFault();
        var factory = new ThrowingFactory(fault.Exception);
        using var logs = new HealthLogCapture();
        using var provider = BuildHealthServices(factory, logs);

        var report = await provider.GetRequiredService<HealthCheckService>()
            .CheckHealthAsync(registration => registration.Name == checkName);

        Assert.Equal(1, factory.Attempts);
        Assert.Equal(HealthStatus.Unhealthy, report.Status);
        var entry = Assert.Single(report.Entries);
        Assert.Equal(checkName, entry.Key);
        Assert.Equal(HealthStatus.Unhealthy, entry.Value.Status);
        Assert.Equal(FailureDescription(checkName), entry.Value.Description);
        Assert.Empty(entry.Value.Data);
        AssertVisibleFrameworkFailure(logs, checkName);
        // Logging and result retention are independent sinks: inspect the logger
        // before checking the report's exception field, so this regression cannot
        // pass merely because the anonymous response writer omits descriptions.
        AssertSafeLogs(logs, fault);
        Assert.Null(entry.Value.Exception);
    }

    [Fact]
    public async Task ProgramReadinessRouteKeepsAnonymous503AndBothChecksLogSafeFailure()
    {
        await using var parent = new SatiApiFactory();
        using (var seed = await parent.CreateAuthenticatedClientAsync("admin-one")) { }
        var fault = new ProbeFault();
        var factory = new ThrowingFactory(fault.Exception);
        using var logs = new HealthLogCapture();
        // Keep Program's actual check registrations and ordinary request context
        // factory. DemoMutationLeaseMiddleware resolves ApiDbContext even on GET;
        // replacing that factory would fail before the health checks ever ran.
        await using var child = parent.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.AddSingleton<ILoggerProvider>(logs);
            services.AddSingleton(sp => new DatabaseIdentityValidator(factory,
                sp.GetRequiredService<IOptions<SatiApiOptions>>()));
            services.AddSingleton(sp => new SchemaDriftHealthCheck(factory,
                sp.GetRequiredService<ILogger<SchemaDriftHealthCheck>>()));
        }));
        using var client = child.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        using var response = await client.GetAsync("/health/ready");

        Assert.Null(client.DefaultRequestHeaders.Authorization);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Unhealthy", await response.Content.ReadAsStringAsync());
        Assert.Equal(2, factory.Attempts);
        AssertVisibleFrameworkFailure(logs, "schema_drift");
        AssertVisibleFrameworkFailure(logs, "database_identity");
        AssertSafeLogs(logs, fault);
        Assert.DoesNotContain(fault.Sentinel, response.ToString());
    }

    [Theory]
    [InlineData("schema_drift", false)]
    [InlineData("schema_drift", true)]
    [InlineData("database_identity", false)]
    [InlineData("database_identity", true)]
    public async Task DirectOperationCancellationRetainsCurrentSafeUnhealthyClassification(string checkName, bool cancelled)
    {
        using var cancellation = new CancellationTokenSource();
        if (cancelled) cancellation.Cancel();
        var fault = new ProbeFault(cancellation.Token, operationCancelled: true);
        var factory = new ThrowingFactory(fault.Exception);
        using var logs = new HealthLogCapture();
        using var provider = BuildHealthServices(factory, logs);

        // Both current checks catch cancellation as Unhealthy. This bounded
        // redaction slice preserves that classification rather than silently
        // introducing a new direct-check cancellation contract.
        var result = await Check(provider, checkName)
            .CheckHealthAsync(new HealthCheckContext(), cancellation.Token);

        Assert.Equal(1, factory.Attempts);
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal(FailureDescription(checkName), result.Description);
        Assert.Empty(result.Data);
        Assert.Null(result.Exception);
        AssertSafeLogs(logs, fault);
    }

    [Fact]
    public async Task FrameworkPreCancellationInvokesNoProbeAndEmitsNoFailure()
    {
        var fault = new ProbeFault();
        var factory = new ThrowingFactory(fault.Exception);
        using var logs = new HealthLogCapture();
        using var provider = BuildHealthServices(factory, logs);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            provider.GetRequiredService<HealthCheckService>().CheckHealthAsync(cancellationToken: cancellation.Token));

        Assert.Equal(0, factory.Attempts);
        Assert.DoesNotContain(logs.Entries, entry => entry.Level >= LogLevel.Error);
        AssertSafeLogs(logs, fault);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SchemaHealthyAndMissingModelColumnRemainDistinct(bool matching)
    {
        await using var database = new SyntheticPipelineDatabase();
        await database.InitializeAsync();
        if (!matching)
        {
            await using var db = new ApiDbContext(database.Options());
            await db.Database.ExecuteSqlRawAsync("DROP INDEX IX_Providers_AgencyId_Npi");
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE Providers DROP COLUMN Npi");
        }
        using var logs = new HealthLogCapture();
        using var provider = BuildHealthServices(new OptionsFactory(database.Options()), logs);

        var report = await provider.GetRequiredService<HealthCheckService>()
            .CheckHealthAsync(registration => registration.Name == "schema_drift");

        var entry = Assert.Single(report.Entries).Value;
        Assert.Equal(matching ? HealthStatus.Healthy : HealthStatus.Unhealthy, entry.Status);
        Assert.Null(entry.Exception);
        if (matching)
        {
            Assert.Equal("Database schema matches the API model.", entry.Description);
            Assert.DoesNotContain(logs.Entries, record => record.Level >= LogLevel.Error);
        }
        else
        {
            Assert.Contains("Providers.Npi", entry.Description!);
            AssertVisibleFrameworkFailure(logs, "schema_drift");
            Assert.Contains(logs.Entries, record => record.Level == LogLevel.Error &&
                record.Category == typeof(SchemaDriftHealthCheck).FullName && record.Text.Contains("Providers.Npi"));
        }
    }

    [Theory]
    [InlineData("matching")]
    [InlineData("mismatch")]
    [InlineData("missing")]
    public async Task RealIdentityValidatorKeepsHealthyMismatchAndMissingMarkerResults(string mode)
    {
        var sentinel = $"SYNTHETIC_IDENTITY_METADATA_{Guid.NewGuid():N}";
        var connection = new IdentityConnection(mode == "matching" ? "sAtIdEmO" : sentinel,
            mode == "matching" ? "dEmO" : sentinel, mode != "missing");
        var factory = new OptionsFactory(new DbContextOptionsBuilder<ApiDbContext>().UseSqlServer(connection).Options);
        using var logs = new HealthLogCapture();
        using var provider = BuildHealthServices(factory, logs);

        var report = await provider.GetRequiredService<HealthCheckService>()
            .CheckHealthAsync(registration => registration.Name == "database_identity");

        var entry = Assert.Single(report.Entries).Value;
        Assert.Equal(mode == "matching" ? HealthStatus.Healthy : HealthStatus.Unhealthy, entry.Status);
        Assert.Null(entry.Exception);
        Assert.Empty(entry.Data);
        Assert.Equal(1, connection.OpenAttempts);
        Assert.Equal(1, connection.ReaderAttempts);
        Assert.Equal("SELECT DB_NAME(), EnvironmentName FROM dbo.SatiDatabaseIdentity WHERE Id = 1;", connection.LastCommand);
        if (mode == "matching")
        {
            Assert.Equal("SatiDemo identity validated.", entry.Description);
            Assert.DoesNotContain(logs.Entries, record => record.Level >= LogLevel.Error);
        }
        else
        {
            Assert.Equal("SatiDemo identity validation failed.", entry.Description);
            AssertVisibleFrameworkFailure(logs, "database_identity");
            Assert.DoesNotContain(sentinel, entry.Description!);
            Assert.DoesNotContain(logs.Entries, record => record.Text.Contains(sentinel));
            Assert.DoesNotContain(logs.Entries, record => record.ExceptionReference is not null);
        }
    }

    private static ServiceProvider BuildHealthServices(IDbContextFactory<ApiDbContext> factory, HealthLogCapture logs)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.ClearProviders().SetMinimumLevel(LogLevel.Debug).AddProvider(logs));
        services.AddSingleton(factory);
        services.AddSingleton<IOptions<SatiApiOptions>>(Options.Create(new SatiApiOptions()));
        services.AddSingleton<DatabaseIdentityValidator>();
        services.AddHealthChecks()
            .AddCheck<DatabaseIdentityHealthCheck>("database_identity", tags: ["ready"])
            .AddCheck<SchemaDriftHealthCheck>("schema_drift", tags: ["ready"]);
        return services.BuildServiceProvider();
    }

    private static IHealthCheck Check(IServiceProvider provider, string name) => name switch
    {
        "schema_drift" => ActivatorUtilities.GetServiceOrCreateInstance<SchemaDriftHealthCheck>(provider),
        "database_identity" => ActivatorUtilities.GetServiceOrCreateInstance<DatabaseIdentityHealthCheck>(provider),
        _ => throw new ArgumentOutOfRangeException(nameof(name))
    };

    private static string FailureDescription(string name) => name == "schema_drift"
        ? "Could not read database metadata." : "SatiDemo identity validation failed.";

    private static void AssertVisibleFrameworkFailure(HealthLogCapture logs, string checkName) =>
        Assert.Contains(logs.Entries, entry => entry.Level == LogLevel.Error && entry.EventId.Id == 103 &&
            entry.Category == "Microsoft.Extensions.Diagnostics.HealthChecks.DefaultHealthCheckService" &&
            entry.Values.Any(value => value.Key == "HealthCheckName" && Equals(value.Value, checkName)) &&
            entry.Values.Any(value => value.Key == "HealthStatus" && Equals(value.Value, HealthStatus.Unhealthy)));

    private static void AssertSafeLogs(HealthLogCapture logs, ProbeFault fault)
    {
        Assert.DoesNotContain(logs.Entries, entry => entry.Text.Contains(fault.Sentinel, StringComparison.Ordinal));
        Assert.DoesNotContain(logs.ScopeText, text => text.Contains(fault.Sentinel, StringComparison.Ordinal));
        Assert.DoesNotContain(logs.Entries, entry => entry.Values.Any(value => value.Value is Exception));
        Assert.DoesNotContain(logs.Entries, entry => entry.ExceptionReference is not null);
        Assert.DoesNotContain(logs.Entries, entry => ReferenceEquals(entry.ExceptionReference, fault.Exception));
    }

    private sealed class ProbeFault
    {
        public string Sentinel { get; } = $"SYNTHETIC_HEALTH_PRIVATE_CONTENT_{Guid.NewGuid():N}";
        public Exception Exception { get; }
        public ProbeFault(CancellationToken token = default, bool operationCancelled = false)
        {
            var inner = new IOException(Sentinel + "_INNER");
            inner.Data["SyntheticPrivateKey"] = Sentinel + "_INNER_DATA";
            Exception = operationCancelled
                ? new OperationCanceledException(Sentinel, inner, token)
                : new InvalidOperationException(Sentinel, inner);
            Exception.Data["SyntheticPrivateKey"] = Sentinel + "_DATA";
        }
    }

    private sealed class ThrowingFactory(Exception failure) : IDbContextFactory<ApiDbContext>
    {
        private int _attempts;
        public int Attempts => Volatile.Read(ref _attempts);
        public ApiDbContext CreateDbContext() { Interlocked.Increment(ref _attempts); throw failure; }
        public ValueTask<ApiDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(CreateDbContext());
    }

    private sealed class OptionsFactory(DbContextOptions<ApiDbContext> options) : IDbContextFactory<ApiDbContext>
    {
        public ApiDbContext CreateDbContext() => new(options);
        public ValueTask<ApiDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(CreateDbContext());
    }

    private sealed record CapturedLog(LogLevel Level, string Category, EventId EventId,
        string Text, Exception? ExceptionReference, IReadOnlyList<KeyValuePair<string, object?>> Values);

    private sealed class HealthLogCapture : ILoggerProvider
    {
        private readonly ConcurrentQueue<CapturedLog> _entries = new();
        private readonly ConcurrentQueue<string> _scopes = new();
        public IReadOnlyList<CapturedLog> Entries => _entries.ToArray();
        public IReadOnlyList<string> ScopeText => _scopes.ToArray();
        public ILogger CreateLogger(string categoryName) => new CaptureLogger(categoryName, this);
        public void Dispose() { }
        private sealed class CaptureLogger(string category, HealthLogCapture owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull
            {
                owner._scopes.Enqueue(state.ToString() ?? "");
                return null;
            }
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                var values = state is IEnumerable<KeyValuePair<string, object?>> pairs
                    ? pairs.ToArray() : [];
                // Keep actual references and Data values as well as formatted text:
                // Exception.ToString ordinarily omits Exception.Data.
                var detail = exception is null ? "" : exception.ToString() + "\n" +
                    string.Join("\n", exception.Data.Keys.Cast<object>().Select(key => exception.Data[key]?.ToString()));
                owner._entries.Enqueue(new CapturedLog(level, category, eventId,
                    formatter(state, exception) + "\n" + detail + "\n" +
                    string.Join("\n", values.Select(pair => pair.Value?.ToString())), exception, values));
            }
        }
    }

    // The real validator executes its fixed ADO command against these synthetic
    // values. No SQL Server connection, environment data, or EF query is used.
    private sealed class IdentityConnection(string databaseName, string environmentName, bool markerExists) : DbConnection
    {
        private ConnectionState _state;
        private readonly string _databaseName = databaseName;
        private readonly string _environmentName = environmentName;
        private readonly bool _markerExists = markerExists;
        public int OpenAttempts { get; private set; }
        public int ReaderAttempts { get; private set; }
        public string? LastCommand { get; private set; }
        [AllowNull]
        public override string ConnectionString { get; set; } = "";
        public override string Database => "SyntheticIdentityDatabase";
        public override string DataSource => "SyntheticIdentitySource";
        public override string ServerVersion => "1.0";
        public override ConnectionState State => _state;
        public override void Open() { OpenAttempts++; _state = ConnectionState.Open; }
        public override Task OpenAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Open();
            return Task.CompletedTask;
        }
        public override void Close() => _state = ConnectionState.Closed;
        public override void ChangeDatabase(string name) => throw new NotSupportedException();
        protected override DbTransaction BeginDbTransaction(IsolationLevel level) => throw new NotSupportedException();
        protected override DbCommand CreateDbCommand() => new IdentityCommand(this);

        private sealed class IdentityCommand(IdentityConnection owner) : DbCommand
        {
            private readonly SqlCommand _parameterOwner = new();
            [AllowNull]
            public override string CommandText { get; set; } = "";
            public override int CommandTimeout { get; set; }
            public override CommandType CommandType { get; set; } = CommandType.Text;
            public override bool DesignTimeVisible { get; set; }
            public override UpdateRowSource UpdatedRowSource { get; set; }
            protected override DbConnection? DbConnection { get; set; } = owner;
            protected override DbTransaction? DbTransaction { get; set; }
            protected override DbParameterCollection DbParameterCollection => _parameterOwner.Parameters;
            protected override DbParameter CreateDbParameter() => new SqlParameter();
            public override void Cancel() { }
            public override void Prepare() { }
            public override int ExecuteNonQuery() => throw new NotSupportedException();
            public override object ExecuteScalar() => throw new NotSupportedException();
            protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
            {
                owner.ReaderAttempts++;
                owner.LastCommand = CommandText;
                var table = new DataTable();
                table.Columns.Add("DatabaseName", typeof(string));
                table.Columns.Add("EnvironmentName", typeof(string));
                if (owner._markerExists) table.Rows.Add(owner._databaseName, owner._environmentName);
                return table.CreateDataReader();
            }
            protected override Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                return Task.FromResult(ExecuteDbDataReader(behavior));
            }
            protected override void Dispose(bool disposing)
            {
                if (disposing) _parameterOwner.Dispose();
                base.Dispose(disposing);
            }
        }
    }
}
