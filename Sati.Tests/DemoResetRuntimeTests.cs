using System.Diagnostics;
using Xunit;

namespace Sati.Tests;

public sealed class DemoResetRuntimeTests
{
    [Theory]
    [InlineData("TokenTimeout")]
    [InlineData("Success")]
    [InlineData("TokenFailure")]
    [InlineData("OpenFailure")]
    [InlineData("LockFailure")]
    [InlineData("RestoreFailure")]
    [InlineData("RollFailure")]
    [InlineData("ComplianceFailure")]
    [InlineData("CleanupFailure")]
    [InlineData("CleanupPreservesOriginalFailure")]
    [InlineData("AuditFailure")]
    public async Task ResetOrchestratorReportsStagesAndSafelyAuditsItsOutcome(string scenario)
    {
        var root = Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(typeof(DemoResetRuntimeTests).Assembly.Location)!,
            "..", "..", "..", "..", ".."));
        // The deployed Function runs PowerShell 7; use the same runtime family.
        var start = new ProcessStartInfo("pwsh.exe")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in new[]
                 {
                     "-NoProfile", "-NonInteractive", "-File",
                     Path.Combine(root, "scripts", "Test-DemoResetRuntime.ps1"), "-Case", scenario
                 })
            start.ArgumentList.Add(argument);

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("Could not start PowerShell 7.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await process.WaitForExitAsync(deadline.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"Demo reset runtime scenario {scenario} exceeded 30 seconds.");
        }

        Assert.True(process.ExitCode == 0, await error);
        Assert.Contains($"DEMO_RESET_RUNTIME_TEST_PASSED Case={scenario}", await output);
    }
}
