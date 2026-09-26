using Xunit;

namespace Sati.Tests;

public sealed class DemoFullResetStructureTests
{
    private static string Root => Path.GetFullPath(Path.Combine(
        Path.GetDirectoryName(typeof(DemoFullResetStructureTests).Assembly.Location)!,
        "..", "..", "..", "..", ".."));

    [Fact]
    public void BaselineCaptureIsDemoPinnedProtectedAndSchemaAware()
    {
        var script = File.ReadAllText(Path.Combine(
            Root, "scripts", "Initialize-DemoFullReset.ps1"));

        Assert.Contains("if ($Database -cne 'SatiDemo')", script);
        Assert.Contains("if (-not $ReplaceBaseline)", script);
        Assert.Contains("EnvironmentName=N'Demo'", script);
        Assert.Contains("@LockMode=N'Exclusive'", script);
        Assert.Contains("demo_baseline", script);
        Assert.Contains("WITH EXECUTE AS OWNER", script);
        Assert.Contains("The Demo schema changed after baseline capture", script);
        Assert.Contains("DENY SELECT, INSERT, UPDATE, DELETE ON SCHEMA::demo_baseline", script);
        Assert.Contains("InstanceId=NEWID()", script);
        Assert.DoesNotContain("SatiProduction", script);
    }

    [Fact]
    public void BaselineCaptureOwnsAnExternalRollingTimelineAnchor()
    {
        var script = File.ReadAllText(Path.Combine(
            Root, "scripts", "Initialize-DemoFullReset.ps1"));

        var state = script.IndexOf("SatiDemoResetState", StringComparison.Ordinal);
        var capture = script.IndexOf("SELECT * INTO demo_baseline", StringComparison.Ordinal);

        Assert.True(state >= 0 && state < capture);
        Assert.Contains("TimelineAnchorDate", script);
        Assert.Contains("LastAppliedAsOfDate", script);
        Assert.Contains("t.name NOT LIKE N'SatiDemoReset%'", script);
        Assert.Contains("SET LastAppliedAsOfDate=NULL", script);
    }

    [Fact]
    public void RollingSeedMovesRecurringDatesOnceAndRebuildsScheduledWork()
    {
        var script = File.ReadAllText(Path.Combine(
            Root, "scripts", "Seed-DemoShowcaseData.ps1"));

        Assert.Contains("$timelineShiftDays = ($today - $timelineFromDate).Days", script);
        Assert.Contains("DATEADD(day,@TimelineShiftDays,person.EffectiveDate)", script);
        Assert.Contains("DATEADD(day,@TimelineShiftDays,form.DueDate)", script);
        Assert.Contains("PARTITION BY person.UserId ORDER BY note.Id", script);
        Assert.Contains("LastAppliedAsOfDate=@Today", script);
        Assert.Contains("UpcomingForms", script);
        Assert.Contains("UpcomingScheduledNotes", script);
    }

    [Fact]
    public void RollingSeedMovesEveryCycleIdentityWithTheEffectiveDate()
    {
        // Billing names a form by (type, TargetEffectiveDate) and a release by a StableKey
        // embedding its target. A roll that moved due dates but not those identities made
        // billing project every cycle as missing, and the whole Demo read as overdue.
        var script = File.ReadAllText(Path.Combine(
            Root, "scripts", "Seed-DemoShowcaseData.ps1"));

        Assert.Contains("TargetEffectiveDate=DATEADD(day,@TimelineShiftDays,form.TargetEffectiveDate)", script);
        Assert.Contains("TargetEffectiveDate=DATEADD(day,@TimelineShiftDays,release.TargetEffectiveDate)", script);
        Assert.Contains("DueOn=DATEADD(day,@TimelineShiftDays,release.DueOn)", script);
        Assert.Contains("CONVERT(char(10),DATEADD(day,@TimelineShiftDays,release.TargetEffectiveDate),23)", script);
        Assert.Contains("A Demo release key does not embed its target date", script);
        Assert.Contains("CompletedOn=DATEADD(day,@TimelineShiftDays,attestation.CompletedOn)", script);
        Assert.Contains("StartDate=DATEADD(day,@TimelineShiftDays,link.StartDate)", script);
        Assert.Contains("CycleStart=DATEADD(day,@TimelineShiftDays,plan_.CycleStart)", script);
        Assert.Contains("CycleStart=DATEADD(day,@TimelineShiftDays,artifact.CycleStart)", script);

        // Day shifts cannot keep a February 29 anniversary; stored rows snap back onto
        // EffectiveDate.AddYears(n), which SQL DATEADD(year) reproduces.
        Assert.Contains("DATEADD(year,ROUND(DATEDIFF(day,person.EffectiveDate,form.TargetEffectiveDate)/365.2425,0)", script);
        Assert.Contains("#releaseSnap", script);

        // A form's completion is the projection of its ledger; stale projections are realigned
        // before validation instead of failing every reset.
        Assert.Contains("CompletedDate=CASE WHEN live.Kind=N'Attested' THEN live.CompletedOn END", script);
        Assert.True(script.IndexOf("CompletedDate=CASE WHEN live.Kind=N'Attested'", StringComparison.Ordinal) <
                    script.IndexOf("UnattestedEvergreenCompletions", StringComparison.Ordinal));

        // Seeded completions are never late, which would hold every note in between.
        Assert.Contains("$completedOn = if ($dueDate -lt $personCompletedOn) { $dueDate } else { $personCompletedOn }", script);
        Assert.Contains("((note.PersonId * 11) % 24)", script);
        Assert.DoesNotContain("((note.PersonId * 11) % 45)", script);
    }

    [Fact]
    public void BothFunctionsCompleteComplianceHistoryAfterTheRollInsideTheResetLock()
    {
        var script = File.ReadAllText(Path.Combine(Root, "Sati.DemoRefresh", "Shared", "DemoReset.ps1"));
        var seed = script.IndexOf("& $seed", StringComparison.Ordinal);
        var history = script.IndexOf("Invoke-DemoComplianceSeed -Server", StringComparison.Ordinal);
        var release = script.IndexOf("sp_releaseapplock", StringComparison.Ordinal);

        Assert.Contains(@"..\ComplianceSeed\Invoke-DemoComplianceSeed.ps1", script);
        Assert.True(seed >= 0 && seed < history);
        Assert.True(history < release);

        var helper = File.ReadAllText(Path.Combine(
            Root, "Sati.DemoRefresh", "ComplianceSeed", "Invoke-DemoComplianceSeed.ps1"));
        Assert.Contains("--demo --apply", helper);
        Assert.Contains("$env:SATI_SQL_ACCESS_TOKEN = $Token", helper);
        Assert.Contains("Remove-Item Env:SATI_SQL_ACCESS_TOKEN", helper);
        Assert.Contains("if ($exitCode -ne 0)", helper);

        var publish = File.ReadAllText(Path.Combine(Root, "scripts", "Publish-DemoRefresh.ps1"));
        Assert.Contains(@"tools\SatiComplianceSeed\SatiComplianceSeed.csproj", publish);
        Assert.Contains("Join-Path $staging 'ComplianceSeed'", publish);
        Assert.Contains("--self-contained true", publish);

        var tool = File.ReadAllText(Path.Combine(Root, "tools", "SatiComplianceSeed", "DemoRun.cs"));
        Assert.Contains("\"SatiDemo\"", tool);
        Assert.Contains("Seeder.DemoMarker", tool);
        Assert.Contains("SATI_SQL_ACCESS_TOKEN", tool);
    }

    [Fact]
    public void DemoHidesOadsBuildersButKeepsTimestampedEvergreenAttestations()
    {
        var script = File.ReadAllText(Path.Combine(
            Root, "scripts", "Seed-DemoShowcaseData.ps1"));

        Assert.Contains("SET IsComprehensiveAssessmentAuthoringEnabled=0", script);
        Assert.Contains("IsClassificationAuthoringEnabled=0", script);
        Assert.Contains("IsPersonCenteredPlanAuthoringEnabled=0", script);
        Assert.DoesNotContain("SET BillingComplianceRequirements", script);
        Assert.Contains("Demo attestation of work completed in Evergreen.", script);
        Assert.Contains("ActorUserId,RecordedAtUtc", script);
        Assert.Contains("ORDER BY RecordedAtUtc DESC, Id DESC", script);
        Assert.Contains("UnattestedEvergreenCompletions", script);
        Assert.Contains("completed PCP or Comprehensive Assessment rows without a matching live attestation", script);
    }

    [Fact]
    public void ManualAndScheduledFunctionsHoldResetLockThroughRollingValidation()
    {
        var shared = File.ReadAllText(Path.Combine(Root, "Sati.DemoRefresh", "Shared", "DemoReset.ps1"));
        var acquire = shared.IndexOf("@LockMode=N'Exclusive'", StringComparison.Ordinal);
        var restore = shared.IndexOf("SatiResetToCanonicalBaseline", StringComparison.Ordinal);
        var seed = shared.IndexOf("& $seed", StringComparison.Ordinal);
        var release = shared.IndexOf("sp_releaseapplock", StringComparison.Ordinal);

        Assert.True(acquire >= 0 && acquire < restore);
        Assert.True(restore < seed);
        Assert.True(seed < release);

        // The nightly timer and the queued Admin request run the same reset.
        foreach (var (folder, trigger) in new[] { ("RefreshCaseload", "'Scheduled'"), ("ResetDemoWorker", "'Manual'") })
        {
            var script = File.ReadAllText(Path.Combine(Root, "Sati.DemoRefresh", folder, "run.ps1"));
            Assert.Contains(@"..\Shared\DemoReset.ps1", script);
            Assert.Contains($"-Trigger {trigger}", script);
            Assert.DoesNotContain("SatiResetToCanonicalBaseline", script);
        }

        var binding = File.ReadAllText(Path.Combine(
            Root, "Sati.DemoRefresh", "ResetDemo", "function.json"));
        Assert.Contains("\"authLevel\": \"function\"", binding);
    }

    [Fact]
    public void AdminResetIsQueuedNotHeldOpenAndItsOutcomeIsAudited()
    {
        // A full reset outlasts the ~230-second HTTP front end, so the request only queues it.
        var request = File.ReadAllText(Path.Combine(Root, "Sati.DemoRefresh", "ResetDemo", "run.ps1"));
        Assert.Contains("Push-OutputBinding -Name ResetRequest", request);
        Assert.Contains("[HttpStatusCode]::Accepted", request);
        Assert.DoesNotContain("SatiResetToCanonicalBaseline", request);
        Assert.DoesNotContain("sp_getapplock", request);

        var output = File.ReadAllText(Path.Combine(Root, "Sati.DemoRefresh", "ResetDemo", "function.json"));
        var trigger = File.ReadAllText(Path.Combine(Root, "Sati.DemoRefresh", "ResetDemoWorker", "function.json"));
        Assert.Contains("\"queueName\": \"demo-reset-requests\"", output);
        Assert.Contains("\"queueName\": \"demo-reset-requests\"", trigger);
        Assert.Contains("\"type\": \"queueTrigger\"", trigger);

        // A destructive reset is never re-run automatically, and never two at once.
        var host = File.ReadAllText(Path.Combine(Root, "Sati.DemoRefresh", "host.json"));
        Assert.Contains("\"maxDequeueCount\": 1", host);
        Assert.Contains("\"batchSize\": 1", host);
        Assert.Contains("\"newBatchThreshold\": 0", host);
        Assert.Contains("Microsoft.Azure.Functions.ExtensionBundle", host);

        // Every token ends at the restore, so the outcome is read from the audit trail.
        var shared = File.ReadAllText(Path.Combine(Root, "Sati.DemoRefresh", "Shared", "DemoReset.ps1"));
        Assert.Contains("'demo.reset.completed'", shared);
        Assert.Contains("'demo.reset.failed'", shared);
        Assert.Contains("EnvironmentName=N'Demo'", shared);
        Assert.DoesNotContain(".Exception.Message", shared);

        var coordinator = File.ReadAllText(Path.Combine(Root, "Sati.Api", "Infrastructure", "DemoResetCoordinator.cs"));
        Assert.Contains("\"Reset started\"", coordinator);
        Assert.DoesNotContain("\"Reset completed\"", coordinator);

        var publish = File.ReadAllText(Path.Combine(Root, "scripts", "Publish-DemoRefresh.ps1"));
        Assert.Contains(@"'Shared\Seed-DemoShowcaseData.ps1'", publish);
    }

    [Fact]
    public void FunctionCredentialStaysAtTheApiBoundary()
    {
        var coordinator = File.ReadAllText(Path.Combine(
            Root, "Sati.Api", "Infrastructure", "DemoResetCoordinator.cs"));
        var publicSettings = Directory.GetFiles(Root, "appsettings*.json", SearchOption.AllDirectories)
            .Select(File.ReadAllText);

        Assert.Contains("x-functions-key", coordinator);
        Assert.Contains("FunctionKey", coordinator);
        Assert.Contains("LoadIntoBufferAsync", coordinator);
        Assert.DoesNotContain("?code=", coordinator);
        Assert.DoesNotContain(publicSettings, contents => contents.Contains(
            "DemoReset__FunctionKey", StringComparison.Ordinal));

        var function = File.ReadAllText(Path.Combine(
            Root, "Sati.DemoRefresh", "ResetDemo", "run.ps1"));
        Assert.Contains("$Request.RawBody", function);
    }
}
