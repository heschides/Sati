using Microsoft.Extensions.Configuration;
using Sati.Data;
using Xunit;

namespace Sati.Tests;

public sealed class DataEnvironmentResolverTests
{
    [Fact]
    public void ProductionResolvesFromTrackedExpectedNameAndPrivateConnection()
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["DataEnvironments:Production:ExpectedDatabaseName"] = "SatiProduction",
            ["ConnectionStrings:SatiProduction"] =
                "Server=(localdb)\\MSSQLLocalDB;Database=SatiProduction;Integrated Security=true;Encrypt=false;"
        });

        var environment = DataEnvironmentResolver.Resolve(configuration, SatiDataEnvironment.Production);

        Assert.Equal("SatiProduction", environment.ExpectedDatabaseName);
        Assert.Equal("SatiProduction", environment.ConnectionStringName);
        Assert.Contains("Database=SatiProduction", environment.ConnectionString);
        Assert.False(environment.UsesCloudApi);
    }

    [Fact]
    public void ProductionRefusesTheExactMissingExpectedNameThatBrokeLocal139()
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["ConnectionStrings:SatiProduction"] =
                "Server=(localdb)\\MSSQLLocalDB;Database=SatiProduction;Integrated Security=true;Encrypt=false;"
        });

        var failure = Assert.Throws<InvalidOperationException>(() =>
            DataEnvironmentResolver.Resolve(configuration, SatiDataEnvironment.Production));

        Assert.Equal("Expected database name for Production is not configured.", failure.Message);
    }

    private static IConfiguration Configuration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
