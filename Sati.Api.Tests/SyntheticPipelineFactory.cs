using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Sati.Api.Data;
using Sati.Api.Infrastructure;
using Sati.Api.Security;
using Sati.Contracts.V1;
using Xunit;

namespace Sati.Api.Tests;

/// <summary>
/// A test owns the database independently of its hosts, so disposal/recreation of an API
/// host actually proves persisted recovery. SQL Server is explicitly opt-in, local-only,
/// uses Windows authentication and can never accept a deployment connection string.
/// </summary>
internal sealed class SyntheticPipelineDatabase : IAsyncDisposable
{
    private readonly SqliteConnection? _sqliteKeeper;
    private bool _ownsSqlDatabase;
    public string Name { get; } = $"SatiSyntheticPipeline_{Guid.NewGuid():N}";
    public bool IsSqlServer { get; }
    public string ConnectionString { get; }

    public SyntheticPipelineDatabase(bool sqlServer = false)
    {
        IsSqlServer = sqlServer;
        ValidateOwnedName(Name);
        if (sqlServer)
        {
            if (!OperatingSystem.IsWindows() ||
                Environment.GetEnvironmentVariable("SATI_RUN_SQLSERVER_TESTS") != "1")
                throw new InvalidOperationException("SQL Server tests require explicit SATI_RUN_SQLSERVER_TESTS=1 on Windows.");
            ConnectionString = LocalConnection(Name);
        }
        else
        {
            ConnectionString = $"Data Source={Name};Mode=Memory;Cache=Shared;Default Timeout=30";
            _sqliteKeeper = new SqliteConnection(ConnectionString);
            _sqliteKeeper.Open();
        }
    }

    internal static void ValidateOwnedName(string name)
    {
        if (!Regex.IsMatch(name, "\\ASatiSyntheticPipeline_[0-9a-f]{32}\\z", RegexOptions.CultureInvariant))
            throw new InvalidOperationException("Refusing a database outside the uniquely named synthetic test namespace.");
    }

    private static string LocalConnection(string catalog) => new SqlConnectionStringBuilder
    {
        DataSource = @"(localdb)\MSSQLLocalDB", InitialCatalog = catalog,
        IntegratedSecurity = true, Encrypt = false, ConnectTimeout = 15,
        ApplicationName = "Sati synthetic pipeline acceptance tests"
    }.ConnectionString;

    public DbContextOptions<ApiDbContext> Options(params IInterceptor[] interceptors)
    {
        var builder = new DbContextOptionsBuilder<ApiDbContext>();
        if (IsSqlServer) builder.UseSqlServer(ConnectionString, sql => sql.EnableRetryOnFailure());
        else builder.UseSqlite(ConnectionString);
        return builder.AddInterceptors(interceptors).Options;
    }

    public async Task InitializeAsync()
    {
        if (IsSqlServer)
        {
            ValidateOwnedName(Name);
            await using var connection = new SqlConnection(LocalConnection("master"));
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "IF DB_ID(@name) IS NOT NULL THROW 50000, 'Synthetic database already exists; refusing reuse.', 1; " +
                $"CREATE DATABASE [{Name}];";
            command.Parameters.AddWithValue("@name", Name);
            await command.ExecuteNonQueryAsync();
            _ownsSqlDatabase = true;
        }
        await using var db = new ApiDbContext(Options());
        await db.Database.EnsureCreatedAsync();
    }

    public async Task<bool> HasWaitingApplicationLockAsync()
    {
        if (!IsSqlServer) throw new InvalidOperationException("This assertion requires SQL Server.");
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        // Observe only locks in this exact synthetic database, never application data.
        command.CommandText = "SELECT COUNT(*) FROM sys.dm_tran_locks WHERE resource_database_id = DB_ID() " +
            "AND resource_type = 'APPLICATION' AND request_status = 'WAIT';";
        return Convert.ToInt32(await command.ExecuteScalarAsync()) > 0;
    }

    public async ValueTask DisposeAsync()
    {
        if (_sqliteKeeper is not null) await _sqliteKeeper.DisposeAsync();
        if (!_ownsSqlDatabase) return;
        ValidateOwnedName(Name);
        using (var pooled = new SqlConnection(ConnectionString)) SqlConnection.ClearPool(pooled);
        await using var connection = new SqlConnection(LocalConnection("master"));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"ALTER DATABASE [{Name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{Name}];";
        await command.ExecuteNonQueryAsync();
        _ownsSqlDatabase = false;
    }
}

internal sealed class SyntheticPipelineFactory : WebApplicationFactory<Program>
{
    private const string Password = "Synthetic-only-password-42!";
    private const string SigningKey = "integration-test-signing-key-that-is-at-least-32-characters";
    private readonly SyntheticPipelineDatabase _database;
    private readonly IInterceptor[] _interceptors;
    public TestKeyWrapper Vault { get; }

    public SyntheticPipelineFactory(SyntheticPipelineDatabase database, TestKeyWrapper? vault = null,
        params IInterceptor[] interceptors)
    {
        _database = database;
        _interceptors = interceptors;
        Vault = vault ?? new TestKeyWrapper();
        // Same bootstrap-only placeholder as SatiApiFactory; the provider is replaced
        // before startup. No external account or connection settings are consulted.
        Environment.SetEnvironmentVariable("ConnectionStrings__SatiDemo",
            @"Server=(localdb)\MSSQLLocalDB;Database=SatiApiTests;Trusted_Connection=True;Encrypt=False;");
        Environment.SetEnvironmentVariable("Authentication__Issuer", "Sati.Api.Tests");
        Environment.SetEnvironmentVariable("Authentication__Audience", "Sati.Api.Tests");
        Environment.SetEnvironmentVariable("Authentication__SigningKey", SigningKey);
        Environment.SetEnvironmentVariable("Authentication__TokenMinutes", "15");
        Environment.SetEnvironmentVariable("Sati__ExpectedDatabaseName", "SatiApiTests");
        Environment.SetEnvironmentVariable("Sati__ExpectedEnvironment", "Testing");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:SatiDemo"] = _database.ConnectionString,
                // Existing synthetic-only endpoint gate uses this test profile label.
                // The connection itself remains our independently validated unique DB.
                ["Sati:ExpectedDatabaseName"] = "SatiApiTests",
                ["Sati:ExpectedEnvironment"] = "Testing",
                ["Authentication:Issuer"] = "Sati.Api.Tests",
                ["Authentication:Audience"] = "Sati.Api.Tests",
                ["Authentication:SigningKey"] = SigningKey,
                ["Authentication:TokenMinutes"] = "15"
            }));
        builder.ConfigureServices(services =>
        {
            foreach (var hosted in services.Where(x => x.ServiceType == typeof(IHostedService) &&
                (x.ImplementationType == typeof(DatabaseIdentityHostedService) ||
                 x.ImplementationType == typeof(SignatureProcessingService))).ToArray())
                services.Remove(hosted);
            services.RemoveAll<ApiDbContext>();
            services.RemoveAll<DbContextOptions<ApiDbContext>>();
            services.RemoveAll<IDbContextFactory<ApiDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<ApiDbContext>>();
            services.RemoveAll<IDatabaseProvider>();
            services.AddDbContextFactory<ApiDbContext>(options =>
            {
                if (_database.IsSqlServer) options.UseSqlServer(_database.ConnectionString, sql => sql.EnableRetryOnFailure());
                else options.UseSqlite(_database.ConnectionString);
                options.AddInterceptors(_interceptors);
            });
            services.AddScoped(provider => provider.GetRequiredService<IDbContextFactory<ApiDbContext>>().CreateDbContext());
            services.RemoveAll<IKeyWrapper>();
            services.AddSingleton<IKeyWrapper>(Vault);
            services.AddDataProtection().UseEphemeralDataProtectionProvider();
        });
    }

    public ApiDbContext OpenDatabase() => new(_database.Options());

    public async Task<PipelineActors> SeedAsync()
    {
        await using var db = OpenDatabase();
        db.DatabaseIdentities.Add(new ServerDatabaseIdentity
        {
            // The API's current Demo identity/token contract also governs Testing.
            EnvironmentName = "Demo", InstanceId = Guid.NewGuid(), CreatedAtUtc = DateTime.UtcNow
        });
        var agency = new ServerAgency
        {
            Name = "Synthetic Pipeline Agency", Npi = "1999999984", TaxId = "111111111",
            Street = "1 Synthetic Way", City = "Portland", State = "ME", Zip = "04101",
            BillingProcedureCode = "G9012", BillingModifier = "HI", BillingUnitRate = 25m,
            EdiSubmitterId = "SATITEST1", EdiPayerName = "MEDICAID MAINE", EdiPayerId = "MCDME",
            EdiContactName = "Synthetic Billing", EdiContactPhone = "2075550101"
        };
        db.Agencies.Add(agency);
        await db.SaveChangesAsync();
        db.Settings.Add(new ServerSettings { AgencyId = agency.Id });
        await db.SaveChangesAsync();
        var verifier = new PasswordVerifier();
        var supervisor = User("synthetic-supervisor", "Supervisor");
        var biller = User("synthetic-biller", "Admin");
        db.Users.AddRange(supervisor, biller);
        await db.SaveChangesAsync();
        var author = User("synthetic-author", "CaseManager");
        author.SupervisorId = supervisor.Id;
        var otherAuthor = User("synthetic-other-author", "CaseManager");
        otherAuthor.SupervisorId = supervisor.Id;
        db.Users.AddRange(author, otherAuthor);
        await db.SaveChangesAsync();
        var first = Person(author.Id, "First");
        var second = Person(author.Id, "Second");
        var other = Person(otherAuthor.Id, "OtherAuthor");
        db.People.AddRange(first, second, other);
        await db.SaveChangesAsync();
        return new PipelineActors(agency.Id, author.Id, otherAuthor.Id, supervisor.Id, biller.Id,
            first.Id, second.Id, other.Id);

        ServerUser User(string username, string role)
        {
            var credential = verifier.Hash(Password);
            return new ServerUser
            {
                AgencyId = agency.Id, Username = username, DisplayName = username, Role = role,
                Permissions = UserPermissionRules.FromLegacyRole(role),
                PasswordHash = credential.Hash, Salt = credential.Salt
            };
        }

        ServerPerson Person(int authorId, string firstName) => new()
        {
            AgencyId = agency.Id, UserId = authorId, FirstName = firstName, LastName = "Synthetic",
            IsTestData = true, BirthDate = new DateTime(1990, 1, 1), EffectiveDate = DateTime.Today.AddMonths(-1),
            MaineCareId = "999999", DiagnosisCode = "F89", PlaceOfService = 11,
            BillingStreet = "2 Synthetic Way", BillingCity = "Portland", BillingState = "ME", BillingZip = "04101",
            // Obligations belong to the consumer's own annual target, and a missing
            // row counts as outstanding, so each one is keyed to the effective date
            // and completed before any synthetic service date.
            Forms = new[] { "PCP", "ComprehensiveAssessment", "Reclassification", "SafetyPlan" }
                .Select(type => new ServerForm
                {
                    Type = type,
                    TargetEffectiveDate = DateTime.Today.AddMonths(-1).Date,
                    DueDate = ComplianceScheduleRules.DueDate(
                        type, DateTime.Today.AddMonths(-1).Date, new ComplianceScheduleSettings()),
                    CompletedDate = DateTime.Today.AddYears(-1)
                }).ToList()
        };
    }

    public async Task<HttpClient> SignInAsync(string username)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        using var response = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(username, Password));
        response.EnsureSuccessStatusCode();
        var login = await response.Content.ReadFromJsonAsync<LoginResponse>() ?? throw new InvalidOperationException("No login response.");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        return client;
    }
}

internal sealed record PipelineActors(int AgencyId, int AuthorId, int OtherAuthorId, int SupervisorId,
    int BillerId, int FirstPersonId, int SecondPersonId, int OtherAuthorPersonId);

public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("SATI_RUN_SQLSERVER_TESTS") != "1")
            Skip = "Opt-in SQL Server rehearsal: set SATI_RUN_SQLSERVER_TESTS=1 on Windows with MSSQLLocalDB installed.";
    }
}
