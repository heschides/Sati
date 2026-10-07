using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Sati.Models.Assessments;

namespace Sati.Contracts.V1;

public sealed record AssessmentValidationIssue(string Location, string Message);
public sealed class AssessmentValidationException(IReadOnlyList<AssessmentValidationIssue> issues)
    : ArgumentException("The assessment has unresolved validation issues.")
{ public IReadOnlyList<AssessmentValidationIssue> Issues { get; } = issues; }

public sealed record SubmitAssessmentRequest(int ExpectedRevision, string ContentSha256,
    int FormId, DateTime TargetEffectiveDate, DateTime DueDate);
public sealed record ReopenLegacyAssessmentRequest(int ExpectedRevision);
public sealed record AssessmentReviewRequest(int SubmissionId, int ExpectedRevision,
    string ContentSha256, string Action, string Location = "document", string Text = "",
    bool Blocking = false, long? FlagId = null, DateTime? CompletedOn = null,
    bool CompletionAttested = false);
public sealed record AssessmentSubmissionDto(int Id, int AssessmentId, int AssessmentVersion,
    int CycleNumber, int DocumentRevision, int FormId, DateTime TargetEffectiveDate,
    DateTime DueDate, int RulesVersion, string ContentSha256, string DocumentJson,
    string ConsumerName, DateTime SubmittedAtUtc);
public sealed record AssessmentReviewEventDto(long Id, int SubmissionId, string Action,
    string Location, string Text, bool Blocking, long? FlagId, int ActorUserId,
    DateTime RecordedAtUtc, int AssessmentRevision, int? ArtifactId, DateTime? CompletedOn);
public sealed record AssessmentReviewDetailsDto(ComprehensiveAssessmentDto Assessment,
    IReadOnlyList<AssessmentSubmissionDto> Submissions, IReadOnlyList<AssessmentReviewEventDto> Events);
public sealed record AssessmentQueueItemDto(int AssessmentId, int PersonId, string ConsumerName,
    string AuthorName, int Version, int Revision, int SubmissionId, DateTime SubmittedAtUtc,
    DateTime TargetEffectiveDate);
public sealed record AssessmentPdfDto(int SubmissionId, int ArtifactId, string ContentSha256,
    string PdfSha256, string FileName, byte[] Content);

/// <summary>Versioned completeness and review authority, shared by both persistence paths.</summary>
public static class AssessmentReviewRules
{
    public const int Version = 1;
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public static string Hash(string json) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    public static bool CanReview(AgencyActor actor, CaseloadParticipant owner, int authorUserId) =>
        actor.UserId != authorUserId && CaseloadTransferRules.CanReachCaseloadOf(actor, owner);
    public static bool CanEdit(string status) => status is "Draft" or "Returned";
    public static bool CanReopenLegacy(string status, bool hasSnapshots) => status == "ReadyForReview" && !hasSnapshots;
    public static bool IsLocation(string location) => location is "document" or "contributors" or "needs" ||
        AssessmentCatalog.Questions.Any(q => q.Key == location) ||
        AssessmentCatalog.Sections.Any(section => $"section:{section.Title}" == location);

    public static AssessmentDocument Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || Encoding.UTF8.GetByteCount(json) > 4_000_000)
            throw new AssessmentValidationException([new("document", "Assessment data is required and limited to 4 MB.")]);
        try
        {
            var document = JsonSerializer.Deserialize<AssessmentDocument>(json, JsonOptions) ?? throw new JsonException();
            if (document.Answers is null || document.Needs is null || document.Contributors is null ||
                document.Answers.Values.Any(a => a is null) || document.Needs.Any(n => n is null) || document.Contributors.Any(c => c is null))
                throw new JsonException();
            return document;
        }
        catch (JsonException)
        { throw new AssessmentValidationException([new("document", "Assessment data has an invalid structure.")]); }
    }

    public static IReadOnlyList<AssessmentValidationIssue> Validate(AssessmentDocument document)
    {
        var issues = new List<AssessmentValidationIssue>();
        void Add(string key, string message) => issues.Add(new(key, message));
        if (document.Answers is null || document.Contributors is null || document.Needs is null)
            return [new("document", "Answers, contributors and needs must be collections.")];
        foreach (var key in document.Answers.Keys.Where(key => !AssessmentCatalog.Questions.Any(q => q.Key == key)))
            Add("document", "An answer has an unknown question identity.");
        foreach (var question in AssessmentCatalog.Questions)
        {
            if (!document.Answers.TryGetValue(question.Key, out var answer) || answer is null || !IsAddressed(question, answer))
                Add(question.Key, "Address this question, including its required details or a reasoned disposition.");
            if (answer is null) continue;
            if (!Enum.IsDefined(answer.TherapySessionFormat) || !Enum.IsDefined(answer.TherapyFrequencyDirection) ||
                answer.ActivitySupportLevels is null || answer.ActivitySkillsTraining is null ||
                answer.ActivitySupportLevels.Values.Any(level => !Enum.IsDefined(level)))
                Add(question.Key, "Response options must be recognized and activity answers must be collections.");
            if ((answer.Supports & ~((SupportMethod)63)) != 0 ||
                (answer.Supports.HasFlag(SupportMethod.NoSupportCurrentlyNeeded) && answer.Supports != SupportMethod.NoSupportCurrentlyNeeded))
                Add(question.Key, "No support is exclusive; support choices must be recognized.");
            if (!question.UsesSupports && answer.Supports != SupportMethod.None)
                Add(question.Key, "This question does not use support choices.");
            if (answer.Status != AssessmentAnswerStatus.Answered && answer.Supports != SupportMethod.None)
                Add(question.Key, "An exception disposition cannot also claim a support answer.");
            if (!string.IsNullOrWhiteSpace(answer.DissentingOpinion) &&
                (string.IsNullOrWhiteSpace(answer.DissentContributor) || string.IsNullOrWhiteSpace(answer.DissentDiscussion) || answer.DissentUnresolved is null))
                Add(question.Key, "Identify whose differing perspective this is, its discussion, and whether it remains unresolved.");
            if (string.IsNullOrWhiteSpace(answer.DissentingOpinion) &&
                (!string.IsNullOrWhiteSpace(answer.DissentContributor) || !string.IsNullOrWhiteSpace(answer.DissentDiscussion) || answer.DissentUnresolved is not null))
                Add(question.Key, "A differing perspective must include the perspective itself.");
        }
        if (document.Contributors.Count == 0)
            Add("contributors", "Record at least one contributor to the team assessment.");
        if (document.Contributors.Any(c => c is null || c.Id == Guid.Empty || string.IsNullOrWhiteSpace(c.Name) || string.IsNullOrWhiteSpace(c.Relationship)) ||
            document.Contributors.Select(c => c?.Id).Distinct().Count() != document.Contributors.Count)
            Add("contributors", "Each contributor needs a unique identity, name and relationship.");
        if (document.Needs.Any(n => n is null || n.Id == Guid.Empty || !Enum.IsDefined(n.Type) ||
                string.IsNullOrWhiteSpace(n.Description) || string.IsNullOrWhiteSpace(n.DesiredResult)) ||
            document.Needs.Select(n => n?.Id).Distinct().Count() != document.Needs.Count)
            Add("needs", "Each identified need needs a unique identity, recognized type, description and desired result.");
        if (document.Needs.Count == 0 && string.IsNullOrWhiteSpace(document.NoIdentifiedNeedsReason))
            Add("needs", "Record identified needs, or explain why none were identified.");
        if (document.Needs.Count > 0 && !string.IsNullOrWhiteSpace(document.NoIdentifiedNeedsReason))
            Add("needs", "Clear the no-needs explanation when recording identified needs.");
        foreach (var need in document.Needs.Where(n => n is not null))
            if (need.AssociateProvider && (need.ProviderId is null or <= 0 || string.IsNullOrWhiteSpace(need.ProviderNameSnapshot)))
                Add("needs", "A provider association requires a linked provider identity and frozen name.");
            else if (!need.AssociateProvider && (need.ProviderId is not null || !string.IsNullOrWhiteSpace(need.ProviderNameSnapshot)))
                Add("needs", "Clear provider details when no provider is associated.");
        return issues;
    }

    public static bool IsAddressed(AssessmentQuestionDefinition question, AssessmentAnswer answer)
    {
        if (!Enum.IsDefined(answer.Status) || answer.Status is AssessmentAnswerStatus.NotYetAnswered or AssessmentAnswerStatus.FollowUpRequired)
            return false;
        if (answer.Status != AssessmentAnswerStatus.Answered) return !string.IsNullOrWhiteSpace(answer.ExceptionReason);
        var concrete = (answer.Supports & (SupportMethod.SetupOrEnvironmental | SupportMethod.PromptingOrCoaching |
            SupportMethod.HandsOnAssistance | SupportMethod.AnotherPersonCompletes)) != 0;
        return question.Kind switch
        {
            AssessmentQuestionKind.Narrative => !string.IsNullOrWhiteSpace(answer.Narrative) &&
                (!question.UsesSupports || answer.Supports == SupportMethod.NoSupportCurrentlyNeeded || concrete) &&
                (!answer.Supports.HasFlag(SupportMethod.Varies) || concrete && !string.IsNullOrWhiteSpace(answer.SupportDetails)),
            AssessmentQuestionKind.YesNo => answer.YesNoResponse.HasValue,
            AssessmentQuestionKind.HealthConcern => answer.YesNoResponse.HasValue &&
                (answer.YesNoResponse == false || !string.IsNullOrWhiteSpace(answer.Details)),
            AssessmentQuestionKind.Therapy => answer.YesNoResponse == false ? answer.FollowUpYesNoResponse.HasValue :
                answer.YesNoResponse == true && answer.FollowUpYesNoResponse.HasValue &&
                answer.TherapySessionFormat is TherapySessionFormat.InPerson or TherapySessionFormat.Telehealth &&
                answer.WantsOtherSessionFormat.HasValue && answer.WantsFrequencyChange.HasValue &&
                (answer.WantsFrequencyChange == false || answer.TherapyFrequencyDirection is TherapyFrequencyDirection.Increase or TherapyFrequencyDirection.Decrease),
            AssessmentQuestionKind.ActivitySupport => answer.ActivitySupportLevels is not null &&
                question.Activities.All(name => answer.ActivitySupportLevels.TryGetValue(name, out var level) &&
                    Enum.IsDefined(level) && level != ActivitySupportLevel.SkillsTraining) &&
                answer.ActivitySupportLevels.Keys.All(question.Activities.Contains) &&
                answer.ActivitySkillsTraining is not null && answer.ActivitySkillsTraining.Keys.All(question.Activities.Contains),
            _ => false
        };
    }

    public static string? ValidateTarget(int personId, DateTime? effectiveDate, FormFact form,
        SubmitAssessmentRequest request, ComplianceScheduleSettings settings)
    {
        if (effectiveDate is null || form.PersonId != personId || form.FormType != "ComprehensiveAssessment" ||
            form.FormId != request.FormId || form.TargetEffectiveDate is not DateTime target ||
            target != request.TargetEffectiveDate.Date || form.DueDate.Date != request.DueDate.Date)
            return "Select the exact annual assessment Form and reload its current dates.";
        var years = target.Year - effectiveDate.Value.Year;
        if (years < 0 || years > 150 || effectiveDate.Value.AddYears(years).Date != target.Date)
            return "The assessment Form has no canonical annual identity; reconcile it before submission.";
        if (ComplianceScheduleRules.DueDate("ComprehensiveAssessment", target, settings) != form.DueDate.Date)
            return "The assessment Form deadline differs from current agency rules; inspect and reconcile it before submission.";
        return null;
    }

    public static string? ValidateCompletion(FormFact form, DateTime effectiveDate, DateTime completedOn,
        DateTime today, ComplianceScheduleSettings settings)
    {
        if (form.CompletedDate is not null)
            return "This Form already has an attestation. Use its correction workflow; approval cannot replace it.";
        var cycle = FormAttestationRules.ResolveCycleForForm(effectiveDate, form.FormType,
            form.DueDate, form.TargetEffectiveDate);
        if (cycle is null) return "The annual Form identity could not be resolved.";
        var decision = FormAttestationRules.Evaluate(form.FormType, completedOn, cycle.Value.CycleStart,
            today, AttestationActorKind.Supervisor, [], targetEffectiveDate: form.TargetEffectiveDate,
            availableOn: ComplianceScheduleRules.AvailableOn(form.FormType, form.DueDate, settings));
        return decision.Accepted ? null : decision.DateError ?? "The Form attestation requirements are not satisfied.";
    }

    public static IReadOnlyList<AssessmentValidationIssue> ValidateProviders(AssessmentDocument document,
        AssessmentDocument previous, IReadOnlyDictionary<int, ProviderAffiliation.ProviderSnapshot> linkedProviders)
    {
        var issues = new List<AssessmentValidationIssue>();
        if (document.Needs is null) return [new("needs", "Needs must be a collection.")];
        foreach (var need in document.Needs.Where(n => n is not null && n.AssociateProvider))
        {
            // An unchanged historical snapshot remains evidence after a link or directory correction.
            var prior = previous.Needs?.FirstOrDefault(n => n is not null && n.Id == need.Id);
            if (prior is { AssociateProvider: true } && prior.ProviderId == need.ProviderId &&
                prior.ProviderNameSnapshot == need.ProviderNameSnapshot &&
                prior.ProviderPracticeSnapshot == need.ProviderPracticeSnapshot && prior.ProviderNetworkSnapshot == need.ProviderNetworkSnapshot)
                continue;
            if (need.ProviderId is not int id || !linkedProviders.TryGetValue(id, out var snapshot) ||
                need.ProviderNameSnapshot != snapshot.ProviderName || need.ProviderPracticeSnapshot != snapshot.PracticeName ||
                need.ProviderNetworkSnapshot != snapshot.NetworkName)
                issues.Add(new("needs", "New provider references must match a current linked provider. Reload the provider choices."));
        }
        return issues;
    }
}
