using Sati.Services;
using System.Text.Json.Nodes;
using Xunit;

namespace Sati.Tests;

public sealed class ReleaseReadinessTests
{
    [Theory]
    [InlineData("unknown", 0)]
    [InlineData("planned", 0)]
    [InlineData("blocked", 0)]
    [InlineData("implemented", 25)]
    [InlineData("tested", 50)]
    [InlineData("verified", 100)]
    public void EvidenceStagesUseTheFixedDenominator(string status, double expected)
    {
        var report = Read(Ledger(status));
        Assert.True(report.IsAvailable);
        Assert.All(report.Scores, score => Assert.Equal(expected, score.Percentage));
        Assert.Equal(status == "verified", report.IsReady);
    }

    [Fact]
    public void UnknownAndPlannedChecksCannotDisappearFromTheDenominator()
    {
        var ledger = Ledger("tested");
        Criteria(ledger).Add(Criterion("m2", "multitenancy", 3));
        Assessments(ledger).Add(Assessment("m2", "unknown"));
        var report = Read(ledger);
        Assert.True(report.IsAvailable);
        Assert.Equal(25, report.Scores[0].Percentage);
        Assert.Equal(12.5, report.Scores[1].Percentage);
        Assert.Equal(50, report.Scores[2].Percentage);
        Assert.Contains("6 fixed weighted checks", report.Scores[0].Basis);
    }

    [Fact]
    public void AHighAverageCannotHideOneLaunchBlocker()
    {
        var ledger = Ledger("verified");
        Criteria(ledger)[0]!["weight"] = 1;
        Criteria(ledger)[1]!["weight"] = 100;
        Criteria(ledger)[2]!["weight"] = 100;
        Assessments(ledger)[0]!["status"] = "blocked";
        var report = Read(ledger);
        Assert.True(report.IsAvailable);
        Assert.True(report.Scores[0].Percentage > 99);
        Assert.False(report.IsReady);
        Assert.Single(report.Blockers);
        Assert.Equal("Not ready for cloud agency launch yet", report.Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("[]")]
    public void MissingOrMalformedDataFailsClosed(string? json) => AssertUnavailable(ReleaseReadiness.Parse(json, "1.3.37"));

    [Fact]
    public void ValidJsonAtTheByteLimitIsAcceptedButOneAdditionalByteIsRejected()
    {
        var json = Ledger().ToJsonString();
        const int maximumBytes = 16 * 1_048_576;
        var atLimit = json + new string(' ', maximumBytes - System.Text.Encoding.UTF8.GetByteCount(json));
        Assert.Equal(maximumBytes, System.Text.Encoding.UTF8.GetByteCount(atLimit));
        Assert.True(ReleaseReadiness.Parse(atLimit, "1.3.37").IsAvailable);
        AssertUnavailable(ReleaseReadiness.Parse(atLimit + " ", "1.3.37"));
    }

    [Fact]
    public void ValidHistoryAtTheSnapshotLimitIsAcceptedButTheNextSnapshotIsRejected()
    {
        var ledger = Ledger();
        var template = Snapshot(ledger).DeepClone();
        var snapshots = ledger["snapshots"]!.AsArray();
        snapshots.Clear();
        for (var index = 1; index <= 1024; index++)
        {
            var snapshot = template.DeepClone();
            snapshot["release"] = $"1.3.{index}";
            snapshots.Add(snapshot);
        }
        Assert.True(ReleaseReadiness.Parse(ledger.ToJsonString(), "1.3.1024").IsAvailable);
        var oneMore = template.DeepClone();
        oneMore["release"] = "1.3.1025";
        snapshots.Add(oneMore);
        AssertUnavailable(ReleaseReadiness.Parse(ledger.ToJsonString(), "1.3.1025"));
    }

    [Theory]
    [InlineData("missing assessment")]
    [InlineData("duplicate criterion")]
    [InlineData("duplicate assessment")]
    [InlineData("unknown criterion")]
    [InlineData("unknown status")]
    [InlineData("zero weight")]
    [InlineData("negative weight")]
    [InlineData("missing blocker flag")]
    [InlineData("unknown evidence")]
    [InlineData("blank evidence")]
    [InlineData("duplicate rubric")]
    [InlineData("missing dimension")]
    [InlineData("missing owner")]
    [InlineData("missing required evidence")]
    [InlineData("unknown rubric")]
    [InlineData("invalid commit")]
    [InlineData("future date")]
    [InlineData("missing schema")]
    [InlineData("large weight")]
    [InlineData("unknown property")]
    [InlineData("null snapshot")]
    [InlineData("missing notes")]
    public void IncompleteAndAmbiguousAssessmentsFailClosed(string defect)
    {
        var ledger = Ledger("tested");
        switch (defect)
        {
            case "missing assessment": Assessments(ledger).RemoveAt(0); break;
            case "duplicate criterion": Criteria(ledger).Add(Criteria(ledger)[0]!.DeepClone()); break;
            case "duplicate assessment": Assessments(ledger).Add(Assessments(ledger)[0]!.DeepClone()); break;
            case "unknown criterion": Assessments(ledger)[0]!["criterionId"] = "absent"; break;
            case "unknown status": Assessments(ledger)[0]!["status"] = "complete"; break;
            case "zero weight": Criteria(ledger)[0]!["weight"] = 0; break;
            case "negative weight": Criteria(ledger)[0]!["weight"] = -1; break;
            case "missing blocker flag": Criteria(ledger)[0]!.AsObject().Remove("blocking"); break;
            case "unknown evidence": Assessments(ledger)[0]!["evidence"]![0]!["kind"] = "trust-me"; break;
            case "blank evidence": Assessments(ledger)[0]!["evidence"]![0]!["reference"] = " "; break;
            case "duplicate rubric": ledger["rubrics"]!.AsArray().Add(ledger["rubrics"]![0]!.DeepClone()); break;
            case "missing dimension": Criteria(ledger)[2]!["dimension"] = "multitenancy"; break;
            case "missing owner": Criteria(ledger)[0]!["owner"] = " "; break;
            case "missing required evidence": Criteria(ledger)[0]!["requiredEvidence"] = new JsonArray(); break;
            case "unknown rubric": Snapshot(ledger)["rubricVersion"] = "absent"; break;
            case "invalid commit": Snapshot(ledger)["sourceRevision"] = "unreviewed changes"; break;
            case "future date": Snapshot(ledger)["assessedAt"] = "2999-10-08"; break;
            case "missing schema": ledger.Remove("schemaVersion"); break;
            case "large weight": Criteria(ledger)[0]!["weight"] = 1001; break;
            case "unknown property": Snapshot(ledger)["readyForLaunch"] = true; break;
            case "null snapshot": ledger["snapshots"]![0] = null; break;
            case "missing notes": Assessments(ledger)[0]!.AsObject().Remove("notes"); break;
        }
        AssertUnavailable(Read(ledger));
    }

    [Theory]
    [InlineData("implemented")]
    [InlineData("tested")]
    [InlineData("verified")]
    public void ClaimedProgressRequiresEvidence(string stage)
    {
        var ledger = Ledger(stage);
        Assessments(ledger)[0]!["evidence"] = new JsonArray();
        AssertUnavailable(Read(ledger));
    }

    [Fact]
    public void VerifiedRequiresEveryRequiredEvidenceKind()
    {
        var ledger = Ledger("verified");
        Criteria(ledger)[0]!["requiredEvidence"] = new JsonArray("source", "synthetic", "sql", "live", "independent");
        Assessments(ledger)[0]!["evidence"]!.AsArray().RemoveAt(4);
        AssertUnavailable(Read(ledger));
    }

    [Fact]
    public void SourceOnlyCannotBeCalledTested()
    {
        var ledger = Ledger("tested");
        Assessments(ledger)[0]!["evidence"] = new JsonArray(new JsonObject { ["kind"] = "source", ["reference"] = "Services/Synthetic.cs" });
        AssertUnavailable(Read(ledger));
    }

    [Theory]
    [InlineData("1.3.36")]
    [InlineData("1.3.38")]
    [InlineData("not-a-version")]
    public void AssessmentMustMatchTheInstalledAssemblyRelease(string installed) => AssertUnavailable(ReleaseReadiness.Parse(Ledger().ToJsonString(), installed));

    [Fact]
    public void TheFirstAssessmentIsAnExplicitBaselineWithoutInventedHistory()
    {
        var report = Read(Ledger());
        Assert.Single(report.History);
        Assert.Contains("baseline", report.Comparison);
        Assert.All(report.Scores, s => Assert.Contains("no earlier assessment", s.Change));
    }

    [Theory]
    [InlineData("reversed versions")]
    [InlineData("duplicate versions")]
    [InlineData("reversed dates")]
    public void UnexpectedHistoryOrderingFailsClosed(string defect)
    {
        var ledger = Ledger();
        var older = Snapshot(ledger).DeepClone();
        older["release"] = defect == "duplicate versions" ? "1.3.37" : defect == "reversed versions" ? "1.3.38" : "1.3.36";
        older["assessedAt"] = defect == "reversed dates" ? "2026-10-09" : "2026-10-07";
        ledger["snapshots"]!.AsArray().Insert(0, older);
        AssertUnavailable(Read(ledger));
    }

    [Fact]
    public void ScoresCanRegressAndCompareAgainstTheImmediatelyPreviousRelease()
    {
        var ledger = Ledger("implemented");
        var older = Snapshot(Ledger("tested")).DeepClone();
        older["release"] = "1.3.36";
        older["assessedAt"] = "2026-10-07";
        ledger["snapshots"]!.AsArray().Insert(0, older);
        var report = Read(ledger);
        Assert.True(report.IsAvailable);
        Assert.All(report.Scores, s => Assert.Equal("Down 25 percentage points since 1.3.36", s.Change));
        Assert.Equal("1.3.37", report.History[0].Release);
        Assert.Equal("1.3.36", report.History[1].Release);
    }

    [Fact]
    public void ChangedRubricsPreserveTheirOwnHistoricalScoresWithoutAFalseDelta()
    {
        var ledger = Ledger("implemented");
        var newerRubric = ledger["rubrics"]![0]!.DeepClone();
        newerRubric["version"] = "new-checks";
        ledger["rubrics"]!.AsArray().Add(newerRubric);
        Snapshot(ledger)["rubricVersion"] = "new-checks";
        var older = Snapshot(Ledger("tested")).DeepClone();
        older["release"] = "1.3.36";
        older["assessedAt"] = "2026-10-07";
        ledger["snapshots"]!.AsArray().Insert(0, older);
        var report = Read(ledger);
        Assert.True(report.IsAvailable);
        Assert.All(report.Scores, s => Assert.Equal("Checks changed · no comparison", s.Change));
        Assert.Contains("Overall 50%", report.History[1].Scores);
        Assert.Contains("not directly comparable", report.History[1].Scores);
    }

    [Fact]
    public void DuplicateJsonPropertiesCannotOverrideAnEarlierAssessment()
    {
        var json = Ledger().ToJsonString().Replace("\"schemaVersion\":1", "\"schemaVersion\":2,\"schemaVersion\":1", StringComparison.Ordinal);
        AssertUnavailable(ReleaseReadiness.Parse(json, "1.3.37"));
    }

    [Fact]
    public void TheEmbeddedAssessmentMatchesTheInstalledRelease()
    {
        var report = ReleaseReadiness.LoadInstalled();
        Assert.True(report.IsAvailable);
        Assert.Equal(typeof(ReleaseReadiness).Assembly.GetName().Version!.ToString(3), report.Release);
        Assert.Equal(3, report.Scores.Count);
        Assert.NotEmpty(report.History);
    }

    private static ReleaseReadinessReport Read(JsonObject ledger) => ReleaseReadiness.Parse(ledger.ToJsonString(), "1.3.37");
    private static void AssertUnavailable(ReleaseReadinessReport report)
    {
        Assert.False(report.IsAvailable);
        Assert.False(report.IsReady);
        Assert.NotEmpty(report.Blockers);
        Assert.Empty(report.History);
        Assert.All(report.Scores, score => Assert.Equal("Unavailable", score.ScoreLabel));
    }

    internal static JsonObject Ledger(string status = "tested") => new()
    {
        ["schemaVersion"] = 1,
        ["rubrics"] = new JsonArray(new JsonObject
        {
            ["version"] = "2026-10-v1",
            ["criteria"] = new JsonArray(Criterion("m", "multitenancy"), Criterion("i", "idempotency"), Criterion("o", "operations"))
        }),
        ["snapshots"] = new JsonArray(new JsonObject
        {
            ["release"] = "1.3.37", ["assessedAt"] = "2026-10-08", ["sourceRevision"] = new string('a', 40),
            ["rubricVersion"] = "2026-10-v1", ["summary"] = "Synthetic baseline. Agency separation has safeguards, but proof is still needed.",
            ["nextSteps"] = new JsonArray("Prove a busy agency cannot delay another agency's work.", "Prove a repeated save cannot create a second result."),
            ["assessments"] = new JsonArray(Assessment("m", status), Assessment("i", status), Assessment("o", status))
        })
    };
    private static JsonObject Criterion(string id, string dimension, int weight = 1) => new()
    {
        ["id"] = id, ["dimension"] = dimension, ["title"] = dimension == "multitenancy" ? "Protect each agency's work" : dimension == "idempotency" ? "Give a repeated save one result" : "Recover after an outage",
        ["weight"] = weight, ["blocking"] = true, ["owner"] = "Readiness reviewer",
        ["requiredEvidence"] = new JsonArray("source", "synthetic"), ["failureModes"] = new JsonArray("A delayed request is repeated."),
        ["acceptance"] = new JsonArray("The test confirms one intended result."), ["references"] = new JsonArray("docs/readiness/README.md")
    };
    private static JsonObject Assessment(string id, string status) => new()
    {
        ["criterionId"] = id, ["status"] = status,
        ["evidence"] = new JsonArray(new[] { "source", "synthetic", "sql", "live", "independent" }
            .Select(kind => (JsonNode)new JsonObject { ["kind"] = kind, ["reference"] = $"Synthetic/{kind}-evidence.txt" }).ToArray()),
        ["notes"] = "Safeguards are present; the remaining launch checks explain what still needs proof."
    };
    private static JsonArray Criteria(JsonObject ledger) => ledger["rubrics"]![0]!["criteria"]!.AsArray();
    private static JsonObject Snapshot(JsonObject ledger) => ledger["snapshots"]![0]!.AsObject();
    private static JsonArray Assessments(JsonObject ledger) => Snapshot(ledger)["assessments"]!.AsArray();
}
