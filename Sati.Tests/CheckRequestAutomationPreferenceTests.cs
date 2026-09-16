using Sati.Data;
using Sati.Services;
using Xunit;

namespace Sati.Tests;

public sealed class CheckRequestAutomationPreferenceTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(), $"sati-check-request-preferences-{Guid.NewGuid():N}");

    [Fact]
    public async Task NewUserDefaultsToEnabledAndOptOutPersists()
    {
        var path = PreferencePath();
        var first = new CheckRequestAutomationPreferenceService(
            EnvironmentInfo(SatiDataEnvironment.Demo), path);
        Assert.True(await first.LoadForUserAsync(41));

        await first.SetEnabledAsync(41, false);
        var restarted = new CheckRequestAutomationPreferenceService(
            EnvironmentInfo(SatiDataEnvironment.Demo), path);
        Assert.False(await restarted.LoadForUserAsync(41));
        Assert.True(await restarted.LoadForUserAsync(42));
    }

    [Fact]
    public async Task SameUserHasIndependentDemoAndProductionChoices()
    {
        var path = PreferencePath();
        var demo = new CheckRequestAutomationPreferenceService(
            EnvironmentInfo(SatiDataEnvironment.Demo), path);
        var production = new CheckRequestAutomationPreferenceService(
            EnvironmentInfo(SatiDataEnvironment.Production), path);

        await demo.SetEnabledAsync(41, false);

        Assert.False(await demo.LoadForUserAsync(41));
        Assert.True(await production.LoadForUserAsync(41));
    }

    [Fact]
    public async Task CorruptFileFailsOpenButIsNotOverwritten()
    {
        Directory.CreateDirectory(directory);
        var path = PreferencePath();
        await File.WriteAllTextAsync(path, "not json");
        var service = new CheckRequestAutomationPreferenceService(
            EnvironmentInfo(SatiDataEnvironment.Demo), path);

        Assert.True(await service.LoadForUserAsync(41));
        Assert.NotNull(service.LastLoadWarning);
        await Assert.ThrowsAsync<CheckRequestAutomationPreferenceSaveException>(() =>
            service.SetEnabledAsync(41, false));
        Assert.Equal("not json", await File.ReadAllTextAsync(path));
    }

    private string PreferencePath() => Path.Combine(directory, "preferences.json");

    private static DataEnvironmentInfo EnvironmentInfo(SatiDataEnvironment environment) =>
        environment == SatiDataEnvironment.Demo
            ? new DataEnvironmentInfo(
                environment, "SatiDemo", ApiBaseAddress: new Uri("https://demo.invalid"))
            : new DataEnvironmentInfo(
                environment, "SatiProduction", "SatiProduction",
                "Server=(localdb)\\MSSQLLocalDB;Database=SatiProduction;");

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}
