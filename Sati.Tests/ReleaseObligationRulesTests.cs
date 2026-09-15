using Sati.Contracts.V1;
using Sati.Models;
using Xunit;

namespace Sati.Tests;

public sealed class ReleaseObligationRulesTests
{
    private static readonly DateTime EffectiveOn = new(2027, 3, 7);

    [Fact]
    public void EveryConsumerGetsExactlyOneAnnualDhhsObligation()
    {
        var plans = ReleaseObligationRules.GenerateCycle(EffectiveOn, []);

        var dhhs = Assert.Single(plans);
        Assert.Equal(ReleaseObligationCategory.Dhhs, dhhs.Category);
        Assert.Equal(ReleaseObligationTrigger.AnnualRenewal, dhhs.Trigger);
        Assert.Equal(new DateTime(2026, 12, 7), dhhs.AvailableOn);
        Assert.Equal(EffectiveOn, dhhs.DueOn);
        Assert.Null(dhhs.AssignmentKey);
    }

    [Fact]
    public void CurrentAssignmentsCreateOneReleaseForEachRecipientAndNoCustomCategory()
    {
        var plans = ReleaseObligationRules.GenerateCycle(EffectiveOn,
        [
            Assignment("medical:17", ReleaseAssignmentKind.MedicalProvider,
                new DateTime(2026, 1, 1)),
            Assignment("medical:29", ReleaseAssignmentKind.MedicalProvider,
                new DateTime(2026, 5, 1)),
            Assignment("waiver:4", ReleaseAssignmentKind.ServiceOrWaiverProvider,
                new DateTime(2025, 9, 1))
        ]);

        Assert.Equal(2, plans.Count(x => x.Category == ReleaseObligationCategory.Medical));
        Assert.Single(plans.Where(x => x.Category == ReleaseObligationCategory.Agency));
        Assert.Single(plans.Where(x => x.Category == ReleaseObligationCategory.Dhhs));
        Assert.Equal(4, plans.Select(x => x.StableKey).Distinct(StringComparer.Ordinal).Count());
        Assert.All(plans, plan => Assert.True(Enum.IsDefined(plan.Category)));
    }

    [Fact]
    public void NoServiceOrWaiverAssignmentMeansNoAgencyRelease()
    {
        var plans = ReleaseObligationRules.GenerateCycle(EffectiveOn,
        [
            Assignment("medical:17", ReleaseAssignmentKind.MedicalProvider,
                new DateTime(2025, 1, 1))
        ]);

        Assert.DoesNotContain(plans, x => x.Category == ReleaseObligationCategory.Agency);
    }

    [Theory]
    [InlineData(ReleaseAssignmentKind.MedicalProvider, ReleaseObligationCategory.Medical)]
    [InlineData(ReleaseAssignmentKind.ServiceOrWaiverProvider, ReleaseObligationCategory.Agency)]
    public void NewMidCycleAssignmentIsDueTheDayBeforeServiceBegins(
        ReleaseAssignmentKind assignmentKind,
        ReleaseObligationCategory expectedCategory)
    {
        var serviceBegins = new DateTime(2027, 8, 15);
        var knownOn = new DateTime(2027, 6, 2);

        var plan = ReleaseObligationRules.GenerateCycle(EffectiveOn,
        [
            Assignment("assignment:52", assignmentKind, serviceBegins, knownOn: knownOn)
        ]).Single(x => x.Category == expectedCategory);

        Assert.Equal(ReleaseObligationTrigger.AssignmentStart, plan.Trigger);
        Assert.Equal(serviceBegins.AddDays(-1), plan.DueOn);
        Assert.Equal(knownOn, plan.AvailableOn);
        Assert.Equal(serviceBegins, plan.AppliesFromOn);

        var snapshot = ReleaseBillingRules.BuildSnapshots(
        [Compliance(plan)], serviceBegins).Single();
        var requirements = expectedCategory == ReleaseObligationCategory.Medical
            ? BillingComplianceRequirements.MedicalRelease
            : BillingComplianceRequirements.AgencyRelease;

        Assert.True(BillingComplianceGate.IsBillingWindowBlocked(
            snapshot.Type, snapshot.DueDate, snapshot.CompletedDate, serviceBegins, requirements));
    }

    [Fact]
    public void AnnualRecipientReleaseUsesAnnualWindowEvenWhenAssignmentStartedEarlier()
    {
        var plan = ReleaseObligationRules.GenerateCycle(EffectiveOn,
        [
            Assignment("medical:17", ReleaseAssignmentKind.MedicalProvider,
                new DateTime(2024, 2, 1))
        ]).Single(x => x.Category == ReleaseObligationCategory.Medical);

        Assert.Equal(ReleaseObligationTrigger.AnnualRenewal, plan.Trigger);
        Assert.Equal(EffectiveOn.AddDays(-90), plan.AvailableOn);
        Assert.Equal(EffectiveOn, plan.DueOn);
    }

    [Fact]
    public void AssignmentEndingOnEffectiveDateIsNotAnActiveAnnualRecipient()
    {
        var plans = ReleaseObligationRules.GenerateCycle(EffectiveOn,
        [
            Assignment("medical:17", ReleaseAssignmentKind.MedicalProvider,
                new DateTime(2025, 1, 1), endsOn: EffectiveOn)
        ]);

        Assert.DoesNotContain(plans, x => x.Category == ReleaseObligationCategory.Medical);
    }

    [Fact]
    public void EndingAnAssignmentProducesAProspectiveRetirementWithoutDeletingHistory()
    {
        var initialAssignment = Assignment(
            "medical:17", ReleaseAssignmentKind.MedicalProvider,
            new DateTime(2025, 1, 1));
        var existingPlan = ReleaseObligationRules.GenerateCycle(
            EffectiveOn, [initialAssignment]).Single(x => x.Category == ReleaseObligationCategory.Medical);
        var existingDhhs = ReleaseObligationRules.GenerateCycle(
            EffectiveOn, []).Single();
        var endOn = new DateTime(2027, 9, 30);
        var endedAssignment = initialAssignment with { EndsOn = endOn };

        var result = ReleaseObligationRules.ReconcileCycle(
            EffectiveOn,
            [endedAssignment],
            [
                new ExistingReleaseObligation(existingPlan.StableKey, existingPlan.AssignmentKey, null),
                new ExistingReleaseObligation(existingDhhs.StableKey, null, null)
            ]);

        Assert.Empty(result.ToCreate);
        var retirement = Assert.Single(result.ToRetire);
        Assert.Equal(existingPlan.StableKey, retirement.StableKey);
        Assert.Equal(endOn, retirement.RetiredOn);

        var beforeEnd = ReleaseBillingRules.BuildSnapshots(
            [Compliance(existingPlan with { RetiredOn = endOn })], endOn.AddDays(-1));
        var onEnd = ReleaseBillingRules.BuildSnapshots(
            [Compliance(existingPlan with { RetiredOn = endOn })], endOn);
        Assert.Single(beforeEnd);
        Assert.Empty(onEnd);
    }

    [Fact]
    public void ReconciliationNeverDeletesAnUnknownHistoricalObligation()
    {
        var historical = new ExistingReleaseObligation(
            "release:v1:2025-03-07:medical:annual:old-assignment",
            "old-assignment",
            new DateTime(2025, 8, 1));

        var result = ReleaseObligationRules.ReconcileCycle(EffectiveOn, [], [historical]);

        Assert.Empty(result.ToRetire);
        Assert.Contains(historical.StableKey, result.PreservedHistoricalKeys);
    }

    [Fact]
    public void StableKeysAreDeterministicAndIncludeRecipientAssignmentIdentity()
    {
        var assignments = new[]
        {
            Assignment("provider / 17", ReleaseAssignmentKind.MedicalProvider,
                new DateTime(2025, 1, 1))
        };

        var first = ReleaseObligationRules.GenerateCycle(EffectiveOn, assignments);
        var second = ReleaseObligationRules.GenerateCycle(EffectiveOn, assignments);

        Assert.Equal(first.Select(x => x.StableKey), second.Select(x => x.StableKey));
        Assert.NotEqual(
            ReleaseObligationRules.StableKey(
                EffectiveOn, ReleaseObligationCategory.Medical,
                ReleaseObligationTrigger.AnnualRenewal, "provider / 17"),
            ReleaseObligationRules.StableKey(
                EffectiveOn, ReleaseObligationCategory.Medical,
                ReleaseObligationTrigger.AnnualRenewal, "provider / 18"));
    }

    [Fact]
    public void EachObligationUsesOnlyItsOwnAttestation()
    {
        var plans = ReleaseObligationRules.GenerateCycle(EffectiveOn,
        [
            Assignment("medical:17", ReleaseAssignmentKind.MedicalProvider,
                new DateTime(2025, 1, 1)),
            Assignment("medical:29", ReleaseAssignmentKind.MedicalProvider,
                new DateTime(2025, 1, 1))
        ]).Where(x => x.Category == ReleaseObligationCategory.Medical).ToArray();
        var completed = plans[0];
        var incomplete = plans[1];
        var completedOn = EffectiveOn.AddDays(4);

        var snapshots = ReleaseBillingRules.BuildSnapshots(
        [
            Compliance(completed,
                [new ReleaseAttestationFact(completed.StableKey, completedOn,
                    new DateTime(2027, 3, 12, 14, 0, 0, DateTimeKind.Utc))]),
            Compliance(incomplete)
        ], EffectiveOn.AddDays(3));

        Assert.Null(snapshots.Single(x => x.ObligationKey == incomplete.StableKey).CompletedDate);
        Assert.Equal(completedOn,
            snapshots.Single(x => x.ObligationKey == completed.StableKey).CompletedDate);
    }

    [Fact]
    public void ReleaseSnapshotsEnterTheSharedBillingGateWithTheirExactIdentity()
    {
        var obligationId = Guid.NewGuid();
        var plan = ReleaseObligationRules.GenerateCycle(EffectiveOn, []).Single();
        var snapshots = ReleaseBillingRules.BuildComplianceSnapshots(
        [
            Compliance(plan) with { ObligationId = obligationId }
        ], EffectiveOn.AddDays(1));

        var blocked = BillingComplianceGate.EvaluateBillingWindowDetailed(
            snapshots,
            EffectiveOn.AddDays(1),
            BillingComplianceRequirements.DhhsRelease);
        var ignored = BillingComplianceGate.EvaluateBillingWindowDetailed(
            snapshots,
            EffectiveOn.AddDays(1),
            BillingComplianceRequirements.None);

        var blocker = Assert.Single(blocked.Blockers!);
        Assert.False(blocked.Passed);
        Assert.Equal($"release:{obligationId:D}", blocker.ObligationId);
        Assert.Equal("Release_DHHS", blocker.Type);
        Assert.True(ignored.Passed);
    }

    [Fact]
    public void AttestationAloneCompletesTheObligationWithoutAnArtifact()
    {
        var key = "release:v1:2027-03-07:dhhs:annual";
        var completedOn = new DateTime(2027, 3, 4);
        var attestation = new ReleaseAttestationFact(
            key, completedOn, new DateTime(2027, 3, 5, 14, 0, 0, DateTimeKind.Utc));

        Assert.Equal(completedOn,
            ReleaseAttestationRules.CompletedOn(key, [attestation]));
    }

    [Fact]
    public void OperationalDateMayBeInThePastButNotInTheFuture()
    {
        var today = new DateTime(2027, 4, 5);
        var recordedAt = new DateTime(2027, 4, 5, 14, 0, 0, DateTimeKind.Utc);

        Assert.Empty(ReleaseAttestationRules.Validate(
            new DateTime(2027, 4, 2), today, recordedAt,
            ReleaseAttestationSource.Manual, null));
        Assert.Contains(ReleaseAttestationRules.FutureCompletionMessage,
            ReleaseAttestationRules.Validate(
                new DateTime(2027, 4, 6), today, recordedAt,
                ReleaseAttestationSource.Manual, null));
    }

    [Fact]
    public void ElectronicSignatureIsAttestationEvidenceAndMustNameItsCompletion()
    {
        var today = new DateTime(2027, 4, 5);
        var recordedAt = new DateTime(2027, 4, 5, 14, 0, 0, DateTimeKind.Utc);

        Assert.Empty(ReleaseAttestationRules.Validate(
            today, today, recordedAt, ReleaseAttestationSource.ElectronicSignature, 91));
        Assert.Contains(ReleaseAttestationRules.SignatureEvidenceMessage,
            ReleaseAttestationRules.Validate(
                today, today, recordedAt, ReleaseAttestationSource.ElectronicSignature, null));
    }

    [Fact]
    public void LegacyCategoryCompletionIsSurfacedButNeverCopiedToExactRecipients()
    {
        var legacyCompletedOn = EffectiveOn.AddDays(-4);
        var exact = ReleaseObligationRules.GenerateCycle(EffectiveOn,
        [
            Assignment("medical:17", ReleaseAssignmentKind.MedicalProvider,
                EffectiveOn.AddYears(-1)),
            Assignment("medical:29", ReleaseAssignmentKind.MedicalProvider,
                EffectiveOn.AddMonths(-8))
        ])
            .Where(item => item.Category == ReleaseObligationCategory.Medical)
            .Select(item => Compliance(item))
            .ToArray();

        var issue = Assert.Single(LegacyReleaseCompletionReview.FindIssues(
            EffectiveOn,
            EffectiveOn,
            [new LegacyReleaseCompletionFact(
                "Release_Medical", EffectiveOn, legacyCompletedOn)],
            exact));

        Assert.Equal(ReleaseLinkageIssueCodes.LegacyCategoryCompletionNeedsReview, issue.Code);
        Assert.Contains("2 exact recipient obligations", issue.Message);
        Assert.Contains("did not copy its date", issue.Message);
        Assert.All(exact, item => Assert.Empty(item.Attestations));
    }

    [Fact]
    public void LegacyReviewWarningClearsAfterTheExactObligationIsAttested()
    {
        var plan = ReleaseObligationRules.GenerateCycle(EffectiveOn, []).Single();
        var completedOn = EffectiveOn.AddDays(-2);
        var exact = Compliance(plan,
        [
            new ReleaseAttestationFact(
                plan.StableKey,
                completedOn,
                new DateTime(2027, 3, 7, 14, 0, 0, DateTimeKind.Utc))
        ]);

        var issues = LegacyReleaseCompletionReview.FindIssues(
            EffectiveOn,
            EffectiveOn,
            [new LegacyReleaseCompletionFact("Release_DHHS", EffectiveOn, completedOn)],
            [exact]);

        Assert.Empty(issues);
    }

    [Theory]
    [InlineData(false, SignerCapacity.Consumer, true)]
    [InlineData(false, SignerCapacity.Guardian, false)]
    [InlineData(true, SignerCapacity.Consumer, false)]
    [InlineData(true, SignerCapacity.Guardian, true)]
    [InlineData(false, SignerCapacity.AuthorizedRepresentative, false)]
    [InlineData(true, SignerCapacity.AuthorizedRepresentative, false)]
    public void OnlyTheConsumerOrRequiredGuardianMaySign(
        bool hasGuardian,
        SignerCapacity capacity,
        bool expected)
    {
        Assert.Equal(expected, ReleaseSigningRules.CanSign(hasGuardian, capacity));
    }

    [Fact]
    public void GuardianPolicyUsesTheExplicitOnlyLabel()
    {
        var policy = ReleaseSigningRules.For(hasGuardian: true);

        Assert.Equal(SignerCapacity.Guardian, policy.RequiredCapacity);
        Assert.Equal("Guardian signature only", policy.Label);
    }

    [Fact]
    public void WithdrawalEndsAuthorizationProspectivelyButDoesNotUndoCompliance()
    {
        var completedOn = new DateTime(2027, 3, 3);
        var withdrawnOn = new DateTime(2027, 6, 1);
        var key = "release:v1:2027-03-07:dhhs:annual";
        var attestation = new ReleaseAttestationFact(
            key, completedOn, new DateTime(2027, 3, 3, 15, 0, 0, DateTimeKind.Utc));

        Assert.True(ReleaseAuthorizationRules.IsActive(
            completedOn, withdrawnOn, withdrawnOn.AddDays(-1)));
        Assert.False(ReleaseAuthorizationRules.IsActive(
            completedOn, withdrawnOn, withdrawnOn));
        Assert.Equal(completedOn, ReleaseAttestationRules.CompletedOn(key, [attestation]));
    }

    [Fact]
    public void PersistenceAggregateRetainsAttestationWhenAuthorizationIsWithdrawn()
    {
        var plan = ReleaseObligationRules.GenerateCycle(EffectiveOn, []).Single();
        var obligation = ReleaseObligation.Create(
            agencyId: 3, personId: 8, plan,
            new DateTime(2026, 12, 7, 14, 0, 0, DateTimeKind.Utc));
        var completedOn = new DateTime(2027, 3, 2);
        obligation.AttestManually(
            completedOn, new DateTime(2027, 3, 2), AttestationActorKind.CaseManager,
            actorUserId: 12,
            new DateTime(2027, 3, 3, 14, 0, 0, DateTimeKind.Utc));

        obligation.Withdraw(
            new DateTime(2027, 6, 1), new DateTime(2027, 6, 1), actorUserId: 12,
            new DateTime(2027, 6, 1, 14, 0, 0, DateTimeKind.Utc),
            "Consumer withdrew authorization.");

        Assert.Single(obligation.Attestations);
        Assert.Equal(completedOn, obligation.CompletedOn);
        Assert.False(obligation.IsAuthorizationActive(new DateTime(2027, 6, 1)));
        Assert.Single(obligation.AuthorizationEvents);
    }

    private static ReleaseAssignmentFact Assignment(
        string key,
        ReleaseAssignmentKind kind,
        DateTime startsOn,
        DateTime? endsOn = null,
        DateTime? knownOn = null) =>
        new(key, kind, startsOn, endsOn, knownOn ?? startsOn.AddDays(-30));

    private static ReleaseComplianceFact Compliance(
        ReleaseObligationPlan plan,
        IReadOnlyCollection<ReleaseAttestationFact>? attestations = null) =>
        new(plan.StableKey, plan.Category, plan.DueOn, plan.AppliesFromOn,
            plan.RetiredOn, attestations ?? [], TargetEffectiveDate: plan.TargetEffectiveDate);
}
