using System.Diagnostics;
using Xunit;

namespace Sati.Tests;

public sealed class DemoRefreshPublishTests
{
    [Theory]
    [InlineData("PackageOnly")]
    [InlineData("ReviewedDeploy")]
    [InlineData("WrongTarget")]
    [InlineData("WrongRuntime")]
    [InlineData("MissingWatchdogSchedule")]
    [InlineData("ChangedPackage")]
    [InlineData("DirtySource")]
    [InlineData("PrivateConfig")]
    public async Task CodeOnlyPublisherPreservesReviewedArtifactsAndDoesNotConfigureAzure(string scenario)
    {
        var root = Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(typeof(DemoRefreshPublishTests).Assembly.Location)!,
            "..", "..", "..", "..", ".."));
        var start = new ProcessStartInfo("pwsh.exe")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-File",
                     Path.Combine(root, "scripts", "Test-DemoRefreshPublish.ps1"), "-Case", scenario })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("Could not start PowerShell 7.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(deadline.Token); }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"Demo publisher scenario {scenario} exceeded 30 seconds.");
        }
        Assert.True(process.ExitCode == 0, await error);
        Assert.Contains($"DEMO_REFRESH_PUBLISH_TEST_PASSED Case={scenario}", await output);
    }
}
