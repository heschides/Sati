using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sati.Services;

/// <summary>
/// Reads the release's embedded evidence ledger. This presentation-only owner never
/// contacts a database or service and never treats a missing assessment as success.
/// </summary>
public static class ReleaseReadiness
{
    internal const string ResourceName = "Sati.ReleaseReadiness.json";
    private const int MaximumBytes = 16 * 1_048_576;
    private static readonly HashSet<string> Dimensions = ["multitenancy", "idempotency", "operations"];
    private static readonly HashSet<string> Kinds = ["source", "synthetic", "sql", "live", "independent"];
    private static readonly HashSet<string> Stages = ["unknown", "planned", "implemented", "tested", "verified", "blocked"];
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        MaxDepth = 32
    };

    public static ReleaseReadinessReport LoadInstalled()
    {
        var assembly = typeof(ReleaseReadiness).Assembly;
        var release = assembly.GetName().Version?.ToString(3) ?? "unknown";
        try
        {
            using var stream = assembly.GetManifestResourceStream(ResourceName);
            if (stream is null)
                return Unavailable(release, "This release has no readiness assessment. A reviewed assessment is needed before progress can be shown.");
            using var buffer = new MemoryStream();
            var chunk = new byte[8192];
            int read;
            while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
            {
                if (buffer.Length + read > MaximumBytes)
                    return Unavailable(release, "The readiness assessment cannot be read safely. It needs to be corrected before progress can be shown.");
                buffer.Write(chunk, 0, read);
            }
            return Parse(System.Text.Encoding.UTF8.GetString(buffer.ToArray()), release);
        }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidOperationException or ArgumentException or OverflowException)
        {
            return Unavailable(release, "The readiness assessment could not be read. It needs to be checked before progress can be shown.");
        }
    }

    internal static ReleaseReadinessReport Parse(string? json, string installedRelease)
    {
        try
        {
            Require(json is { Length: > 0 } && System.Text.Encoding.UTF8.GetByteCount(json) <= MaximumBytes);
            using (var document = JsonDocument.Parse(json!, new() { MaxDepth = 32 }))
                RejectDuplicateProperties(document.RootElement);
            var ledger = JsonSerializer.Deserialize<Ledger>(json!, JsonOptions);
            Require(ledger is not null && ledger.SchemaVersion == 1);
            Require(ledger!.Rubrics is { Length: > 0 and <= 128 } && ledger.Snapshots is { Length: > 0 and <= 1024 });
            foreach (var rubric in ledger.Rubrics!)
            {
                Require(rubric is not null && GoodText(rubric.Version, 100) && rubric.Criteria is { Length: > 0 and <= 512 });
                var rubricCriteria = rubric!.Criteria!;
                Require(rubricCriteria.All(c => c is not null && GoodText(c.Id, 120) && Dimensions.Contains(c.Dimension ?? "") &&
                    GoodText(c.Title, 500) && c.Weight > 0 && double.IsFinite(c.Weight) && c.Weight <= 1000 &&
                    GoodText(c.Owner, 200) && ValidStrings(c.RequiredEvidence, 1, 5, 50) &&
                    c.RequiredEvidence!.Distinct(StringComparer.Ordinal).Count() == c.RequiredEvidence.Length &&
                    c.RequiredEvidence.All(Kinds.Contains) && ValidStrings(c.FailureModes, 1, 64, 4000) &&
                    ValidStrings(c.Acceptance, 1, 64, 4000) && ValidStrings(c.References, 1, 64, 2048)));
                Require(rubricCriteria.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count() == rubricCriteria.Length);
                Require(Dimensions.All(d => rubricCriteria.Any(c => c.Dimension == d)));
            }
            Require(ledger.Rubrics.Select(r => r.Version).Distinct(StringComparer.Ordinal).Count() == ledger.Rubrics.Length);
            var rubricsByVersion = ledger.Rubrics.ToDictionary(r => r.Version!, StringComparer.Ordinal);
            Version? previousVersion = null;
            DateOnly? previousDate = null;
            var snapshots = ledger.Snapshots!;
            foreach (var snapshot in snapshots)
            {
                Require(snapshot is not null && TryRelease(snapshot.Release, out var version) &&
                    DateOnly.TryParseExact(snapshot.AssessedAt, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) &&
                    GoodText(snapshot.SourceRevision, 128) && GoodText(snapshot.RubricVersion, 100) &&
                    GoodText(snapshot.Summary, 6000) && ValidStrings(snapshot.NextSteps, 1, 64, 4000) &&
                    snapshot.Assessments is { Length: > 0 and <= 512 });
                // Check chronology instead of silently reordering corrupted history.
                TryRelease(snapshot!.Release, out version);
                DateOnly.TryParseExact(snapshot.AssessedAt, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
                Require(previousVersion is null || version! > previousVersion);
                Require(previousDate is null || date >= previousDate);
                Require(date <= DateOnly.FromDateTime(DateTime.UtcNow));
                Require(snapshot.SourceRevision is { Length: 40 } && snapshot.SourceRevision.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'));
                previousVersion = version;
                previousDate = date;
                var assessments = snapshot.Assessments!;
                Require(assessments.All(a => a is not null && GoodText(a.CriterionId, 120) && Stages.Contains(a.Status ?? "") &&
                    GoodText(a.Notes, 6000) && a.Evidence is { Length: <= 64 } &&
                    a.Evidence.All(e => e is not null && Kinds.Contains(e.Kind ?? "") && GoodText(e.Reference, 2048))));
                Require(assessments.Select(a => a.CriterionId).Distinct(StringComparer.Ordinal).Count() == assessments.Length);
                Require(rubricsByVersion.ContainsKey(snapshot.RubricVersion!));
                var snapshotCriteria = rubricsByVersion[snapshot.RubricVersion!].Criteria!;
                var criteriaById = snapshotCriteria.ToDictionary(c => c.Id!, StringComparer.Ordinal);
                Require(assessments.Length == snapshotCriteria.Length && assessments.All(a => criteriaById.ContainsKey(a.CriterionId!)));
                foreach (var assessment in assessments)
                {
                    var evidence = assessment.Evidence!.Select(e => e.Kind!).ToHashSet(StringComparer.Ordinal);
                    if (assessment.Status is "implemented" or "tested" or "verified") Require(evidence.Contains("source"));
                    if (assessment.Status == "tested") Require(evidence.Contains("synthetic") || evidence.Contains("sql"));
                    if (assessment.Status == "verified") Require(criteriaById[assessment.CriterionId!].RequiredEvidence!.All(evidence.Contains));
                }
            }
            Require(TryRelease(installedRelease, out _) && snapshots[^1].Release == installedRelease);
            var current = snapshots[^1];
            var criteria = rubricsByVersion[current.RubricVersion!].Criteria!;
            var previous = snapshots.Length > 1 ? snapshots[^2] : null;
            var comparable = previous is not null && previous.RubricVersion == current.RubricVersion;
            var cards = new[]
            {
                Card("Overall readiness", null),
                Card("Agency separation", "multitenancy"),
                Card("Safe repeat requests", "idempotency")
            };
            var currentById = current.Assessments!.ToDictionary(a => a.CriterionId!, StringComparer.Ordinal);
            var open = criteria.Where(c => currentById[c.Id!].Status != "verified").ToArray();
            var blockers = open.Where(c => c.Blocking).Select(Describe).ToArray();
            var knownBlockers = open.Where(c => currentById[c.Id!].Status == "blocked").Select(Describe).ToArray();
            var protectedItems = criteria.Where(c => currentById[c.Id!].Status is "tested" or "verified").Select(Describe).ToArray();
            var needsWork = open.Select(Describe).ToArray();
            var history = snapshots.Reverse().Select(s => new ReleaseReadinessHistory(s.Release!, s.AssessedAt!, s.Summary!,
                $"Overall {Score(s, null):0.#}% · Agency separation {Score(s, "multitenancy"):0.#}% · Safe repeat requests {Score(s, "idempotency"):0.#}%" +
                    (s.RubricVersion == current.RubricVersion ? "" : " · Earlier scores used different checks and are not directly comparable."))).ToArray();
            var status = open.Length == 0 ? "Required evidence complete" : "Not ready for cloud agency launch yet";
            var comparison = previous is null
                ? "First recorded assessment: this is a baseline, so there is no earlier score to compare."
                : comparable ? $"Compared with release {previous.Release}. Scores can go down when evidence or safeguards weaken."
                : "The readiness checks changed since the previous release. No direct score comparison is shown.";
            return new(true, installedRelease, $"Assessment recorded {current.AssessedAt}", status, current.Summary!, comparison,
                $"Evidence score across {criteria.Length} fixed checks: planned or unknown 0%, implemented 25%, tested 50%, fully verified 100%. Overall also includes operating and recovery checks. These are evidence points, not confidence ratings or predictions of safety, work completed, or time left. Any open launch check still prevents readiness.",
                Freeze(cards), Freeze(blockers), Freeze(knownBlockers), Freeze(protectedItems), Freeze(needsWork), Freeze(current.NextSteps!), Freeze(history), open.Length == 0);

            ReleaseReadinessCard Card(string title, string? dimension)
            {
                var score = Score(current, dimension);
                var delta = comparable ? score - Score(previous!, dimension) : (double?)null;
                var label = delta is null ? previous is null ? "Baseline · no earlier assessment" : "Checks changed · no comparison"
                    : delta == 0 ? $"No change since {previous!.Release}"
                    : $"{(delta > 0 ? "Up" : "Down")} {Math.Abs(delta.Value):0.#} percentage points since {previous!.Release}";
                var denominator = criteria.Where(c => dimension is null || c.Dimension == dimension).Sum(c => c.Weight);
                return new(title, score, $"{score:0.#}%", label, $"Based on {denominator:0.#} fixed weighted checks.", true);
            }

            double Score(Snapshot snapshot, string? dimension)
            {
                var byId = snapshot.Assessments!.ToDictionary(a => a.CriterionId!, StringComparer.Ordinal);
                var included = rubricsByVersion[snapshot.RubricVersion!].Criteria!.Where(c => dimension is null || c.Dimension == dimension).ToArray();
                return Math.Round(included.Sum(c => c.Weight * StageScore(byId[c.Id!].Status!)) / included.Sum(c => c.Weight), 1, MidpointRounding.AwayFromZero);
            }

            string Describe(Criterion criterion) => $"{criterion.Title} — {currentById[criterion.Id!].Notes}";
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or ArgumentException or OverflowException or NullReferenceException)
        {
            return Unavailable(installedRelease, "The readiness assessment is missing, incomplete, or does not match this release. It needs review before progress can be shown.");
        }
    }

    private static double StageScore(string status) => status switch { "implemented" => 25, "tested" => 50, "verified" => 100, _ => 0 };
    private static bool GoodText([NotNullWhen(true)] string? value, int maximum) => !string.IsNullOrWhiteSpace(value) && value.Length <= maximum && !value.Any(c => char.IsControl(c) && c is not '\n' and not '\r' and not '\t');
    private static bool ValidStrings([NotNullWhen(true)] string[]? values, int minimum, int maximum, int textMaximum) => values is not null && values.Length >= minimum && values.Length <= maximum && values.All(s => GoodText(s, textMaximum));
    private static bool TryRelease(string? value, out Version? version) => Version.TryParse(value, out version) && version.Build >= 0 && version.Revision < 0 && version.ToString(3) == value;
    private static void Require([DoesNotReturnIf(false)] bool valid) { if (!valid) throw new InvalidOperationException("Invalid readiness ledger."); }
    private static ReadOnlyCollection<T> Freeze<T>(IEnumerable<T> items) => Array.AsReadOnly(items.ToArray());
    private static void RejectDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                Require(names.Add(property.Name));
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray()) RejectDuplicateProperties(child);
    }
    private static ReleaseReadinessReport Unavailable(string release, string reason) => new(false, release, "Assessment unavailable", "Readiness unavailable", reason,
        "No progress comparison is available until this release has a valid assessment.", "A missing assessment never counts as a passed check.",
        Freeze(new[] { "Overall readiness", "Agency separation", "Safe repeat requests" }.Select(t => new ReleaseReadinessCard(t, 0, "Unavailable", "No comparison available", "A reviewed assessment is needed.", false))),
        Freeze(new[] { "A valid readiness assessment for this release is required." }), Freeze(new[] { reason }), Freeze(Array.Empty<string>()), Freeze(new[] { reason }),
        Freeze(new[] { "Review and correct the assessment for this release, then repeat the release checks." }), Freeze(Array.Empty<ReleaseReadinessHistory>()), false);

    private sealed record Ledger(int SchemaVersion, Rubric[]? Rubrics, Snapshot[]? Snapshots);
    private sealed record Rubric(string? Version, Criterion[]? Criteria);
    private sealed record Criterion(string? Id, string? Dimension, string? Title, double Weight, [property: JsonRequired] bool Blocking, string? Owner, string[]? RequiredEvidence, string[]? FailureModes, string[]? Acceptance, string[]? References);
    private sealed record Snapshot(string? Release, string? AssessedAt, string? SourceRevision, string? RubricVersion, string? Summary, string[]? NextSteps, Assessment[]? Assessments);
    private sealed record Assessment(string? CriterionId, string? Status, Evidence[]? Evidence, string? Notes);
    private sealed record Evidence(string? Kind, string? Reference);
}

public sealed record ReleaseReadinessCard(string Title, double Percentage, string ScoreLabel, string Change, string Basis, bool IsAvailable)
{
    public string AccessibleSummary => $"{Title}: {ScoreLabel}. {Change} {Basis}";
}
public sealed record ReleaseReadinessHistory(string Release, string AssessedAt, string Summary, string Scores);
public sealed record ReleaseReadinessReport(bool IsAvailable, string Release, string AssessmentDate, string Status, string Summary,
    string Comparison, string Explanation, IReadOnlyList<ReleaseReadinessCard> Scores, IReadOnlyList<string> Blockers, IReadOnlyList<string> KnownBlockers,
    IReadOnlyList<string> ProtectedItems, IReadOnlyList<string> NeedsWork, IReadOnlyList<string> NextSteps,
    IReadOnlyList<ReleaseReadinessHistory> History, bool IsReady)
{
    public string BlockerHeading => $"Launch checks still open ({Blockers.Count})";
    public string KnownBlockerHeading => $"Known problems stopping launch ({KnownBlockers.Count})";
    public string ProtectedHeading => $"What is protected ({ProtectedItems.Count})";
    public string NeedsWorkHeading => $"What still needs work ({NeedsWork.Count})";
}
