using System.Diagnostics;
using Xunit;

namespace Sati.Tests;

public sealed class DemoResetExternalClearinghouseGuardTests
{
    private static string Root => Path.GetFullPath(Path.Combine(
        Path.GetDirectoryName(typeof(DemoResetExternalClearinghouseGuardTests).Assembly.Location)!,
        "..", "..", "..", "..", ".."));

    [Fact]
    public void CaptureAndRestoreCheckLiveAndSnapshotExternalStateUnderTheLockBeforeWriting()
    {
        var script = File.ReadAllText(Path.Combine(Root, "scripts", "Initialize-DemoFullReset.ps1"));
        Assert.Contains("ExternalClearinghouseResetGuard.ps1", script);
        Assert.Contains("Get-DemoExternalClearinghouseResetGuardSql -RequireBaseline", script);
        var guard = File.ReadAllText(Path.Combine(Root, "Sati.DemoRefresh", "Shared",
            "ExternalClearinghouseResetGuard.ps1"));

        foreach (var table in new[]
                 {
                     "ClearinghouseAccounts", "ClearinghouseDispatches", "ClearinghouseDispatchAttempts",
                     "ClearinghouseResponseReceipts", "ClearinghouseFeedCheckpoints", "AuditEvents"
                 })
            Assert.Contains(table, guard);
        Assert.Contains("N'dbo'", guard);
        Assert.Contains("N'demo_baseline'", guard);
        Assert.Contains("ConnectorKind=2", guard);
        Assert.Contains("SecretReference", guard);
        Assert.Contains("State NOT IN (1,6)", guard);
        Assert.Contains("ExternalFileId IS NOT NULL", guard);
        Assert.Contains("Source=2", guard);
        Assert.Contains("LastReceiptId IS NOT NULL", guard);
        Assert.Contains("billing-clearinghouse.claimmd-test-account-onboarded", guard);
        var onboarding = File.ReadAllText(Path.Combine(Root, "Sati.Api", "Endpoints", "ClaimMdOnboardingEndpoints.cs"));
        Assert.Contains("ClaimMdOnboardingAction = \"billing-clearinghouse.claimmd-test-account-onboarded\"", onboarding);
        Assert.Contains("THROW 51012, 'DemoResetBlockedByExternalClearinghouseState'", guard);
        Assert.Contains("OBJECT_ID(@externalPrefix+N'[ClearinghouseAccounts]', N'U') IS NULL", guard);
        Assert.Contains("IF @externalSchema=N'dbo' OR @requireBaseline=1", guard);

        var captureLock = script.IndexOf("IF @captureLockResult < 0", StringComparison.Ordinal);
        var captureGuard = script.IndexOf("$captureExternalGuardSql", captureLock, StringComparison.Ordinal);
        var captureMutation = script.IndexOf("IF OBJECT_ID(N'dbo.SatiDemoResetState'", captureLock, StringComparison.Ordinal);
        Assert.True(captureLock >= 0 && captureGuard > captureLock && captureGuard < captureMutation);

        var restoreLock = script.IndexOf("IF @lockResult < 0", captureMutation, StringComparison.Ordinal);
        var restoreGuard = script.IndexOf("$restoreExternalGuardSql", restoreLock, StringComparison.Ordinal);
        var restoreMutation = script.IndexOf("NOCHECK CONSTRAINT ALL", restoreLock, StringComparison.Ordinal);
        Assert.True(restoreLock >= 0 && restoreGuard > restoreLock && restoreGuard < restoreMutation);

        var assertionProcedure = script.IndexOf("CREATE OR ALTER PROCEDURE dbo.SatiAssertCanonicalResetAllowed", StringComparison.Ordinal);
        var restoreProcedure = script.IndexOf("CREATE OR ALTER PROCEDURE dbo.SatiResetToCanonicalBaseline", StringComparison.Ordinal);
        Assert.True(assertionProcedure > captureGuard && assertionProcedure < restoreProcedure);
        var assertionOwner = script.IndexOf("WITH EXECUTE AS OWNER", assertionProcedure, StringComparison.Ordinal);
        var assertionLock = script.IndexOf("IF @guardLockResult < 0", assertionProcedure, StringComparison.Ordinal);
        var assertionGuard = script.IndexOf("$restoreExternalGuardSql", assertionProcedure, StringComparison.Ordinal);
        Assert.True(assertionOwner > assertionProcedure && assertionOwner < assertionLock &&
                    assertionLock < assertionGuard && assertionGuard < restoreProcedure);
        Assert.Contains("GRANT EXECUTE ON dbo.SatiAssertCanonicalResetAllowed TO [$ResetIdentityName]", script);
        Assert.Contains("DENY SELECT, INSERT, UPDATE, DELETE ON SCHEMA::demo_baseline", script);

        var runtime = File.ReadAllText(Path.Combine(Root, "Sati.DemoRefresh", "Shared", "DemoReset.ps1"));
        var runtimeAssert = runtime.IndexOf("$preflight.CommandText = 'EXEC dbo.SatiAssertCanonicalResetAllowed;'", StringComparison.Ordinal);
        var runtimeAssertExecute = runtime.IndexOf("$preflight.ExecuteNonQuery()", runtimeAssert, StringComparison.Ordinal);
        var runtimeRestore = runtime.IndexOf("$command.CommandText = 'EXEC dbo.SatiResetToCanonicalBaseline", StringComparison.Ordinal);
        Assert.True(runtimeAssert >= 0 && runtimeAssertExecute > runtimeAssert && runtimeRestore > runtimeAssertExecute);
        Assert.DoesNotContain("Get-DemoExternalClearinghouseResetGuardSql", runtime);
    }

    [Fact]
    public async Task RuntimeClassifiesBlockedSqlErrorWithoutLoggingExternalData()
    {
        var start = new ProcessStartInfo("pwsh.exe")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var arg in new[]
                 {
                     "-NoProfile", "-NonInteractive", "-File",
                     Path.Combine(Root, "scripts", "Test-DemoResetExternalClearinghouseGuard.ps1")
                 })
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start PowerShell 7.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(deadline.Token); }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("Demo reset external-state fake exceeded 30 seconds.");
        }

        Assert.True(process.ExitCode == 0, await error);
        Assert.Contains("DEMO_RESET_EXTERNAL_GUARD_TEST_PASSED", await output);
    }
}
