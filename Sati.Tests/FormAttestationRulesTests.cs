using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Xunit;

namespace Sati.Tests;

public sealed class FormAttestationRulesTests
{
    [Fact]
    public async Task SavingAFormNoteDoesNotCompleteTheMatchingForm()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        int formId;
        await using (var db = fixture.Factory.CreateDbContext())
        {
            var person = await db.People.SingleAsync(candidate => candidate.Id == fixture.PersonOneId);
            person.EffectiveDate = DateTime.Today.AddMonths(-6);
            var due = DateTime.Today.AddDays(30);
            var form = new Form(
                FormType.Q2R,
                due,
                targetEffectiveDate: due.AddDays(-180))
            {
                PersonId = fixture.PersonOneId
            };
            db.Forms.Add(form);
            await db.SaveChangesAsync();
            formId = form.Id;
        }

        var note = Note.Create(
            "Evidence narrative that must not mutate the form.",
            DateTime.Today,
            NoteStatus.Pending,
            15,
            fixture.PersonOneId,
            FormType.Q2R,
            NoteType.Form);

        await fixture.NotesFromAnotherSession().AddNoteAsync(note);

        await using var verification = fixture.Factory.CreateDbContext();
        var stored = await verification.Forms.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == formId);
        Assert.Null(stored.CompletedDate);
        Assert.False(stored.IsCompliant);
        Assert.Empty(await verification.FormAttestations
            .Where(candidate => candidate.FormId == formId)
            .ToListAsync());
    }

    [Fact]
    public void PendingListUsesTheNotesPersonAndEventDateCycle()
    {
        var effective = new DateTime(2025, 1, 1);
        var noteDate = new DateTime(2025, 2, 10);
        var notes = new[]
        {
            new NoteFact(71, 22, "Q1R", noteDate, "Logged")
        };
        var forms = new[]
        {
            new FormFact(101, 99, "Q1R", new DateTime(2025, 4, 1), null),
            new FormFact(102, 22, "Q1R", new DateTime(2025, 4, 1), null),
            new FormFact(103, 22, "Q1R", new DateTime(2026, 4, 1), null)
        };

        var pending = Assert.Single(FormAttestationRules.PendingAttestations(
            notes, forms, effective, new DateTime(2026, 9, 3)));

        Assert.Equal(102, pending.FormId);
        Assert.Equal(22, pending.PersonId);
        Assert.Equal(new DateTime(2025, 1, 1), pending.CycleStart);
        Assert.Equal(new DateTime(2026, 1, 1), pending.CycleEnd);
        Assert.Equal(71, pending.EvidenceNoteId);
    }

    [Fact]
    public void AttestationBeforeItsOwnCycleIsRejected()
    {
        var decision = FormAttestationRules.Evaluate(
            "Q1R",
            new DateTime(2026, 1, 31),
            new DateTime(2026, 2, 1),
            new DateTime(2026, 9, 3),
            AttestationActorKind.CaseManager,
            []);

        Assert.False(decision.Accepted);
        Assert.Equal(FormAttestationRules.BeforeCycleMessage, decision.DateError);
    }

    [Fact]
    public void AttestationBeforeTheConfiguredAvailabilityDateIsRejected()
    {
        var availableOn = new DateTime(2026, 11, 7);

        var early = FormAttestationRules.Evaluate(
            "ComprehensiveAssessment",
            availableOn.AddDays(-1),
            new DateTime(2026, 3, 7),
            new DateTime(2027, 1, 1),
            AttestationActorKind.CaseManager,
            [],
            availableOn: availableOn);
        var onOpeningDay = FormAttestationRules.Evaluate(
            "ComprehensiveAssessment",
            availableOn,
            new DateTime(2026, 3, 7),
            new DateTime(2027, 1, 1),
            AttestationActorKind.CaseManager,
            [],
            availableOn: availableOn);

        Assert.False(early.Accepted);
        Assert.Contains("not available", early.DateError, StringComparison.OrdinalIgnoreCase);
        Assert.True(onOpeningDay.Accepted);
    }

    [Fact]
    public void ReleaseAttestationIsSufficientWithoutAnArtifactPrerequisite()
    {
        var cycleStart = new DateTime(2026, 1, 1);
        var draft = new ArtifactFact(
            41, 7, AnnualDocumentKind.ReleaseMedical.ToString(), cycleStart, IsDraft: true);

        var withoutArtifact = FormAttestationRules.Evaluate(
            "Release_Medical", new DateTime(2026, 2, 1), cycleStart,
            new DateTime(2026, 9, 3), AttestationActorKind.CaseManager, []);
        var withDraft = FormAttestationRules.Evaluate(
            "Release_Medical", new DateTime(2026, 2, 1), cycleStart,
            new DateTime(2026, 9, 3), AttestationActorKind.CaseManager, [draft]);
        var withFinished = FormAttestationRules.Evaluate(
            "Release_Medical", new DateTime(2026, 2, 1), cycleStart,
            new DateTime(2026, 9, 3), AttestationActorKind.CaseManager,
            [draft with { ArtifactId = 42, IsDraft = false }]);

        Assert.True(withoutArtifact.Accepted);
        Assert.True(withDraft.Accepted);
        Assert.True(withFinished.Accepted);
    }

    [Fact]
    public void ReclassificationRequiresACompletedComprehensiveAssessmentInTheSameCycle()
    {
        var cycleStart = new DateTime(2026, 1, 1);
        var assessment = new FormFact(
            81, 7, "ComprehensiveAssessment", new DateTime(2026, 2, 1), null);

        var incomplete = FormAttestationRules.Evaluate(
            "Reclassification", new DateTime(2026, 3, 1), cycleStart,
            new DateTime(2026, 9, 3), AttestationActorKind.CaseManager, [], [assessment]);
        var completed = FormAttestationRules.Evaluate(
            "Reclassification", new DateTime(2026, 3, 1), cycleStart,
            new DateTime(2026, 9, 3), AttestationActorKind.CaseManager, [],
            [assessment with { CompletedDate = new DateTime(2026, 2, 2) }]);

        Assert.False(incomplete.Accepted);
        Assert.True(completed.Accepted);
    }

    [Fact]
    public void ReclassificationCannotBypassItsAssessmentImplication()
    {
        var cycleStart = new DateTime(2026, 1, 1);

        var caseManager = FormAttestationRules.Evaluate(
            "Reclassification", new DateTime(2026, 3, 1), cycleStart,
            new DateTime(2026, 9, 3), AttestationActorKind.CaseManager, [], [],
            "Assessment was completed in Evergreen.");
        var supervisorWithoutReason = FormAttestationRules.Evaluate(
            "Reclassification", new DateTime(2026, 3, 1), cycleStart,
            new DateTime(2026, 9, 3), AttestationActorKind.Supervisor, []);
        var supervisor = FormAttestationRules.Evaluate(
            "Reclassification", new DateTime(2026, 3, 1), cycleStart,
            new DateTime(2026, 9, 3), AttestationActorKind.Supervisor, [], [],
            "Assessment was completed in Evergreen.");

        Assert.False(caseManager.Accepted);
        Assert.False(supervisorWithoutReason.Accepted);
        Assert.False(supervisor.Accepted);
        Assert.False(supervisor.SupervisorOverrideAccepted);
    }

    [Fact]
    public void ReclassificationMatchesAssessmentByTargetEffectiveDateAndDateOrder()
    {
        var target = new DateTime(2027, 3, 7);
        var cycleStart = target.AddYears(-1);
        var priorAssessment = new FormFact(
            80, 7, "ComprehensiveAssessment", target.AddYears(-1).AddDays(-90),
            new DateTime(2026, 1, 10), target.AddYears(-1));
        var sameTargetButLaterAssessment = new FormFact(
            81, 7, "ComprehensiveAssessment", target.AddDays(-90),
            new DateTime(2026, 12, 12), target);

        var wrongTarget = FormAttestationRules.Evaluate(
            "Reclassification", new DateTime(2026, 12, 10), cycleStart,
            new DateTime(2026, 12, 20), AttestationActorKind.CaseManager, [],
            [priorAssessment], targetEffectiveDate: target);
        var assessmentAfterReclass = FormAttestationRules.Evaluate(
            "Reclassification", new DateTime(2026, 12, 10), cycleStart,
            new DateTime(2026, 12, 20), AttestationActorKind.CaseManager, [],
            [sameTargetButLaterAssessment], targetEffectiveDate: target);
        var accepted = FormAttestationRules.Evaluate(
            "Reclassification", new DateTime(2026, 12, 12), cycleStart,
            new DateTime(2026, 12, 20), AttestationActorKind.CaseManager, [],
            [sameTargetButLaterAssessment], targetEffectiveDate: target);

        Assert.False(wrongTarget.Accepted);
        Assert.False(assessmentAfterReclass.Accepted);
        Assert.True(accepted.Accepted);
    }

    [Fact]
    public async Task ReclassificationCreatesSeparateAssessmentAndReclassAttestationsAtomically()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var target = DateTime.Today;
        int assessmentId;
        int reclassificationId;
        Form detached;
        await using (var db = fixture.Factory.CreateDbContext())
        {
            var person = await db.People.SingleAsync(candidate => candidate.Id == fixture.PersonOneId);
            person.EffectiveDate = target.AddYears(-2);
            var assessment = new Form(
                FormType.ComprehensiveAssessment,
                target.AddDays(-90),
                targetEffectiveDate: target) { PersonId = fixture.PersonOneId };
            var reclassification = new Form(
                FormType.Reclassification,
                target.AddDays(-30),
                targetEffectiveDate: target) { PersonId = fixture.PersonOneId };
            db.Forms.AddRange(assessment, reclassification);
            await db.SaveChangesAsync();
            assessmentId = assessment.Id;
            reclassificationId = reclassification.Id;
            detached = await db.Forms.AsNoTracking()
                .SingleAsync(candidate => candidate.Id == reclassificationId);
        }

        var session = new SessionService();
        session.SetUser(fixture.CaseManagerOne);
        var service = new FormService(fixture.Factory, session);
        var assessmentCompletedOn = target.AddDays(-5);
        var reclassificationCompletedOn = target.AddDays(-2);

        await service.AttestReclassificationAsync(
            detached,
            reclassificationCompletedOn,
            assessmentCompletedOn);

        await using var verification = fixture.Factory.CreateDbContext();
        var forms = await verification.Forms.AsNoTracking()
            .Where(candidate => candidate.Id == assessmentId || candidate.Id == reclassificationId)
            .OrderBy(candidate => candidate.Id)
            .ToListAsync();
        var attestations = await verification.FormAttestations.AsNoTracking()
            .Where(candidate => candidate.FormId == assessmentId || candidate.FormId == reclassificationId)
            .OrderBy(candidate => candidate.FormId)
            .ToListAsync();

        Assert.Equal(assessmentCompletedOn, forms.Single(candidate => candidate.Id == assessmentId).CompletedDate);
        Assert.Equal(reclassificationCompletedOn, forms.Single(candidate => candidate.Id == reclassificationId).CompletedDate);
        Assert.Collection(
            attestations,
            assessment =>
            {
                Assert.Equal(assessmentId, assessment.FormId);
                Assert.Equal(assessmentCompletedOn, assessment.CompletedOn);
            },
            reclassification =>
            {
                Assert.Equal(reclassificationId, reclassification.FormId);
                Assert.Equal(reclassificationCompletedOn, reclassification.CompletedOn);
                Assert.Contains(
                    assessmentId.ToString(),
                    reclassification.PrerequisiteStateJson,
                    StringComparison.Ordinal);
            });
    }

    [Fact]
    public void RemovingEvidenceFromThePendingProjectionDoesNotRevokeAnAttestation()
    {
        var completedOn = new DateTime(2026, 3, 15);
        var form = new Form(FormType.Q1R, new DateTime(2026, 4, 1));
        form.Attest(FormAttestation.Attested(
            completedOn,
            AttestationActorKind.CaseManager,
            actorUserId: 31,
            recordedAtUtc: DateTime.UtcNow,
            evidenceNoteId: 71));

        var pending = FormAttestationRules.PendingAttestations(
            [],
            [new FormFact(101, 22, "Q1R", form.DueDate, form.CompletedDate)],
            new DateTime(2026, 1, 1),
            new DateTime(2026, 9, 3));

        Assert.Empty(pending);
        Assert.Equal(completedOn, form.CompletedDate);
        Assert.Single(form.Attestations);
        Assert.Equal(71, form.Attestations[0].EvidenceNoteId);
    }

    [Fact]
    public async Task LocalAttestationAndRevocationAppendLedgerRowsAndAuditEvents()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        int formId;
        Form detached;
        await using (var db = fixture.Factory.CreateDbContext())
        {
            var person = await db.People.SingleAsync(candidate => candidate.Id == fixture.PersonOneId);
            person.EffectiveDate = DateTime.Today.AddMonths(-6);
            var due = DateTime.Today.AddDays(5);
            var form = new Form(
                FormType.Q3R,
                due,
                targetEffectiveDate: due.AddDays(-270))
            {
                PersonId = fixture.PersonOneId
            };
            db.Forms.Add(form);
            await db.SaveChangesAsync();
            formId = form.Id;
            detached = await db.Forms.AsNoTracking().SingleAsync(candidate => candidate.Id == formId);
        }

        var session = new SessionService();
        session.SetUser(fixture.CaseManagerOne);
        var service = new FormService(fixture.Factory, session);
        var completedOn = DateTime.Today.AddDays(-2);

        await service.AttestAsync(detached, completedOn);
        await service.RevokeAttestationAsync(detached, "Entered against the wrong cycle.");

        await using var verification = fixture.Factory.CreateDbContext();
        var stored = await verification.Forms.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == formId);
        var ledger = await verification.FormAttestations.AsNoTracking()
            .Where(candidate => candidate.FormId == formId)
            .OrderBy(candidate => candidate.Id)
            .ToListAsync();
        var audits = await verification.AuditEvents.AsNoTracking()
            .Where(candidate => candidate.ResourceType == "Form" && candidate.ResourceId == formId.ToString())
            .OrderBy(candidate => candidate.Id)
            .ToListAsync();

        Assert.Null(stored.CompletedDate);
        Assert.Collection(
            ledger,
            attestation =>
            {
                Assert.Equal(FormAttestationKind.Attested, attestation.Kind);
                Assert.Equal(completedOn, attestation.CompletedOn);
                Assert.Equal(fixture.CaseManagerOne.Id, attestation.ActorUserId);
                Assert.Equal(
                    FormAttestationRules.NoPrerequisitesStateJson,
                    attestation.PrerequisiteStateJson);
            },
            revocation =>
            {
                Assert.Equal(FormAttestationKind.Revoked, revocation.Kind);
                Assert.Equal("Entered against the wrong cycle.", revocation.Reason);
            });
        Assert.Contains(audits, candidate => candidate.Action == "form.attested");
        Assert.Contains(audits, candidate => candidate.Action == "form.attestation-revoked");
        Assert.DoesNotContain(audits, candidate => candidate.MetadataJson.Contains("wrong cycle", StringComparison.OrdinalIgnoreCase));

        verification.FormAttestations.Remove(ledger[0]);
        var appendOnly = await Assert.ThrowsAsync<InvalidOperationException>(
            () => verification.SaveChangesAsync());
        Assert.Contains("append-only", appendOnly.Message, StringComparison.OrdinalIgnoreCase);
    }
}
