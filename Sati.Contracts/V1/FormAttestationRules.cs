namespace Sati.Contracts.V1;

public enum AttestationActorKind
{
    CaseManager,
    Supervisor,
    System
}

public enum PrerequisiteKind
{
    None,
    DocumentArtifact,
    ComprehensiveAssessment,
    SafetyPlan,
    PrivacyPracticesAcknowledgment
}

public sealed record ArtifactFact(
    int ArtifactId,
    int PersonId,
    string Kind,
    DateTime CycleStart,
    bool IsDraft,
    bool IsExternal = false,
    bool IsAcknowledged = false);

public sealed record NoteFact(
    int NoteId,
    int PersonId,
    string FormType,
    DateTime EventDate,
    string Status);

public sealed record FormFact(
    int FormId,
    int PersonId,
    string FormType,
    DateTime DueDate,
    DateTime? CompletedDate,
    DateTime? TargetEffectiveDate = null);

public sealed record UnmetPrerequisite(PrerequisiteKind Kind, string Message);

public sealed record AttestationDecision(
    bool Accepted,
    string? DateError,
    IReadOnlyList<UnmetPrerequisite> UnmetPrerequisites,
    bool SupervisorOverrideAccepted = false);

public sealed record PendingAttestation(
    int FormId,
    int PersonId,
    string FormType,
    DateTime CycleStart,
    DateTime CycleEnd,
    DateTime DueDate,
    int EvidenceNoteId,
    DateTime EvidenceDate);

/// <summary>
/// Single owner of the rules that decide whether a compliance-form attestation is
/// legal and which form-note evidence is waiting for a human attestation.
/// </summary>
public static class FormAttestationRules
{
    public const string BeforeCycleMessage =
        "A form completion date cannot be before the compliance cycle began.";
    public const string NoPrerequisitesStateJson = "{\"prerequisiteArtifactIds\":[]}";

    public static AttestationDecision Evaluate(
        string formType,
        DateTime completedOn,
        DateTime cycleStart,
        DateTime today,
        AttestationActorKind actor,
        IReadOnlyCollection<ArtifactFact> artifactsForCycle,
        IReadOnlyCollection<FormFact>? formsForCycle = null,
        string? supervisorOverrideReason = null,
        DateTime? targetEffectiveDate = null,
        DateTime? availableOn = null)
    {
        _ = actor;
        _ = artifactsForCycle;
        _ = supervisorOverrideReason;

        var dateError = ValidateCompletionDate(completedOn, cycleStart, today, availableOn);

        var unmet = EvaluatePrerequisites(
            formType,
            completedOn,
            cycleStart,
            formsForCycle ?? [],
            targetEffectiveDate);

        return new AttestationDecision(
            dateError is null && unmet.Count == 0,
            dateError,
            unmet,
            SupervisorOverrideAccepted: false);
    }

    public static IReadOnlyList<PendingAttestation> PendingAttestations(
        IReadOnlyCollection<NoteFact> notes,
        IReadOnlyCollection<FormFact> forms,
        DateTime? effectiveDate,
        DateTime today)
    {
        if (effectiveDate is null)
            return [];

        var eligibleNotes = notes
            .Where(note => note.EventDate.Date <= today.Date && IsEvidenceStatus(note.Status))
            .OrderByDescending(note => note.EventDate)
            .ThenByDescending(note => note.NoteId);

        var pending = new List<PendingAttestation>();
        foreach (var note in eligibleNotes)
        {
            var form = forms
                .Where(candidate =>
                    candidate.PersonId == note.PersonId &&
                    string.Equals(candidate.FormType, note.FormType, StringComparison.OrdinalIgnoreCase) &&
                    candidate.CompletedDate is null)
                .Select(candidate => new
                {
                    Form = candidate,
                    Cycle = ResolveCycleForForm(
                        effectiveDate.Value,
                        candidate.FormType,
                        candidate.DueDate,
                        candidate.TargetEffectiveDate)
                })
                .Where(candidate => candidate.Cycle is not null &&
                    candidate.Cycle.Value.CycleStart.Date <= note.EventDate.Date &&
                    note.EventDate.Date < candidate.Cycle.Value.CycleEnd.Date)
                .OrderByDescending(candidate => candidate.Form.DueDate)
                .FirstOrDefault();

            if (form is null || pending.Any(item => item.FormId == form.Form.FormId))
                continue;

            pending.Add(new PendingAttestation(
                form.Form.FormId,
                form.Form.PersonId,
                form.Form.FormType,
                form.Cycle!.Value.CycleStart,
                form.Cycle.Value.CycleEnd,
                form.Form.DueDate.Date,
                note.NoteId,
                note.EventDate.Date));
        }

        return pending;
    }

    public static PrerequisiteKind PrerequisiteFor(string formType) => formType switch
    {
        "Reclassification" => PrerequisiteKind.ComprehensiveAssessment,
        _ => PrerequisiteKind.None
    };

    public static string PrerequisiteStateJson(
        AttestationDecision decision,
        IEnumerable<int> artifactIds,
        string? supervisorOverrideReason = null)
    {
        _ = decision;
        _ = artifactIds;
        _ = supervisorOverrideReason;

        // Completion is established by the attestation itself. Artifacts are useful
        // documents, but they are not a second compliance gate and a supervisor
        // cannot bypass the one remaining semantic rule (Reclass implies CA).
        return NoPrerequisitesStateJson;
    }

    public static string AssessmentPrerequisiteStateJson(int assessmentFormId) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            comprehensiveAssessmentFormId = assessmentFormId
        });

    /// <summary>
    /// Resolves a form's work cycle from its stable annual identity. Due-date
    /// inference remains only for rows created before TargetEffectiveDate existed.
    /// </summary>
    public static (DateTime CycleStart, DateTime CycleEnd)? ResolveCycleForForm(
        DateTime initialEffectiveDate,
        string formType,
        DateTime formDueDate,
        DateTime? targetEffectiveDate)
    {
        if (targetEffectiveDate is DateTime target && target != default)
        {
            target = target.Date;
            return IsReview(formType)
                ? (target, target.AddYears(1))
                : (target.AddYears(-1), target);
        }

        return ResolveCycle(initialEffectiveDate, formDueDate);
    }

    public static string? ValidateAssessmentCompletionDate(
        DateTime assessmentCompletedOn,
        DateTime reclassificationCompletedOn,
        DateTime cycleStart,
        DateTime today,
        DateTime? assessmentAvailableOn = null)
    {
        var dateError = FormCompletionRules.Validate(assessmentCompletedOn, today);
        if (dateError is not null)
            return dateError;
        if (assessmentCompletedOn.Date < cycleStart.Date)
            return BeforeCycleMessage;
        if (assessmentAvailableOn is DateTime available &&
            assessmentCompletedOn.Date < available.Date)
            return $"This form was not available for completion before {available:MMM d, yyyy}.";
        return assessmentCompletedOn.Date > reclassificationCompletedOn.Date
            ? "The Comprehensive Assessment completion date cannot be after the Reclassification completion date."
            : null;
    }

    public static string? ValidateCompletionDate(
        DateTime completedOn,
        DateTime cycleStart,
        DateTime today,
        DateTime? availableOn = null)
    {
        var dateError = FormCompletionRules.Validate(completedOn, today);
        if (dateError is null && completedOn.Date < cycleStart.Date)
            dateError = BeforeCycleMessage;
        if (dateError is null && availableOn is DateTime available &&
            completedOn.Date < available.Date)
            dateError = $"This form was not available for completion before {available:MMM d, yyyy}.";
        return dateError;
    }

    public static (DateTime CycleStart, DateTime CycleEnd)? ResolveCycle(
        DateTime effectiveDate,
        DateTime formDueDate)
    {
        var approximateYear = formDueDate.Year - effectiveDate.Year;
        for (var offset = approximateYear - 1; offset <= approximateYear + 1; offset++)
        {
            var cycleStart = effectiveDate.AddYears(offset).Date;
            var cycleEnd = effectiveDate.AddYears(offset + 1).Date;
            if (formDueDate.Date > cycleStart && formDueDate.Date <= cycleEnd)
                return (cycleStart, cycleEnd);
        }

        return null;
    }

    private static bool IsEvidenceStatus(string status) =>
        status.Equals("Pending", StringComparison.OrdinalIgnoreCase) ||
        status.Equals("Logged", StringComparison.OrdinalIgnoreCase) ||
        status.Equals("Approved", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<UnmetPrerequisite> EvaluatePrerequisites(
        string formType,
        DateTime completedOn,
        DateTime cycleStart,
        IReadOnlyCollection<FormFact> formsForCycle,
        DateTime? targetEffectiveDate)
    {
        var prerequisite = PrerequisiteFor(formType);
        if (prerequisite == PrerequisiteKind.None)
            return [];

        if (prerequisite == PrerequisiteKind.ComprehensiveAssessment)
        {
            var cycleEnd = cycleStart.AddYears(1);
            var completed = formsForCycle.Any(form =>
                form.FormType.Equals("ComprehensiveAssessment", StringComparison.OrdinalIgnoreCase) &&
                BelongsToSameAnnualObligation(form, cycleStart, cycleEnd, targetEffectiveDate) &&
                form.CompletedDate is DateTime assessmentCompletedOn &&
                assessmentCompletedOn.Date <= completedOn.Date);
            return completed
                ? []
                : [new(prerequisite,
                    "A Comprehensive Assessment for this annual effective date must be attested on or before the Reclassification completion date.")];
        }

        return [];
    }

    private static bool BelongsToSameAnnualObligation(
        FormFact form,
        DateTime cycleStart,
        DateTime cycleEnd,
        DateTime? targetEffectiveDate)
    {
        if (targetEffectiveDate is DateTime target && target != default)
        {
            if (form.TargetEffectiveDate is DateTime candidateTarget && candidateTarget != default)
                return candidateTarget.Date == target.Date;

            // Transitional compatibility for a CA row not yet backfilled.
            return form.DueDate.Date > cycleStart.Date && form.DueDate.Date <= cycleEnd.Date;
        }

        return form.DueDate.Date > cycleStart.Date && form.DueDate.Date <= cycleEnd.Date;
    }

    private static bool IsReview(string formType) =>
        formType.Equals("Q1R", StringComparison.OrdinalIgnoreCase) ||
        formType.Equals("Q2R", StringComparison.OrdinalIgnoreCase) ||
        formType.Equals("Q3R", StringComparison.OrdinalIgnoreCase) ||
        formType.Equals("Q4R", StringComparison.OrdinalIgnoreCase);
}
