using Sati.Contracts.V1;
using Xunit;

namespace Sati.Tests;

public sealed class BillingCompliancePolicyAndRecoveryTests
{
    private static readonly DateTime Today = new(2026, 9, 14);

    [Fact]
    public void PolicyResolverUsesTheVersionEffectiveOnTheServiceDate()
    {
        BillingCompliancePolicyVersionSnapshot[] versions =
        [
            Policy(1, new DateTime(2026, 1, 1), BillingComplianceRequirements.Pcp),
            Policy(2, new DateTime(2026, 7, 1), BillingComplianceRequirements.ComprehensiveAssessment),
            Policy(3, new DateTime(2027, 1, 1), BillingComplianceRequirements.All)
        ];

        var beforeChange = BillingCompliancePolicyRules.ResolveForServiceDate(
            versions, agencyId: 7, new DateTime(2026, 6, 30));
        var onChange = BillingCompliancePolicyRules.ResolveForServiceDate(
            versions, agencyId: 7, new DateTime(2026, 7, 1));

        Assert.Equal(BillingComplianceRequirements.Pcp, beforeChange?.Requirements);
        Assert.Equal(BillingComplianceRequirements.ComprehensiveAssessment, onChange?.Requirements);
    }

    [Fact]
    public void PolicyResolverDoesNotReachIntoAnotherAgency()
    {
        BillingCompliancePolicyVersionSnapshot[] versions =
        [
            Policy(1, new DateTime(2026, 1, 1), BillingComplianceRequirements.Pcp),
            Policy(2, new DateTime(2026, 1, 1), BillingComplianceRequirements.All, agencyId: 8)
        ];

        var resolved = BillingCompliancePolicyRules.ResolveForServiceDate(
            versions, agencyId: 7, new DateTime(2026, 9, 1));

        Assert.Equal(BillingComplianceRequirements.Pcp, resolved?.Requirements);
    }

    [Fact]
    public void SameDateCorrectionUsesTheNewestDurableVersionId()
    {
        BillingCompliancePolicyVersionSnapshot[] versions =
        [
            Policy(40, new DateTime(2026, 7, 1), BillingComplianceRequirements.Pcp),
            Policy(41, new DateTime(2026, 7, 1), BillingComplianceRequirements.ComprehensiveAssessment),
            Policy(39, new DateTime(2026, 7, 1), BillingComplianceRequirements.All)
        ];

        var resolved = BillingCompliancePolicyRules.ResolveForServiceDate(
            versions, agencyId: 7, new DateTime(2026, 7, 1));

        Assert.Equal(41, resolved?.VersionId);
        Assert.Equal(BillingComplianceRequirements.ComprehensiveAssessment, resolved?.Requirements);
    }

    [Fact]
    public void PastPolicyDatesAreOffByDefaultAndRequireAReasonWhenEnabled()
    {
        Assert.False(new BillingCompliancePolicyOptions().AllowPastEffectiveDates);

        var defaultDecision = BillingCompliancePolicyRules.ValidateChange(
            BillingComplianceRequirements.Pcp,
            new DateTime(2026, 9, 13),
            Today);
        var enabledWithoutReason = BillingCompliancePolicyRules.ValidateChange(
            BillingComplianceRequirements.Pcp,
            new DateTime(2026, 9, 13),
            Today,
            new BillingCompliancePolicyOptions(AllowPastEffectiveDates: true));
        var enabledWithReason = BillingCompliancePolicyRules.ValidateChange(
            BillingComplianceRequirements.Pcp,
            new DateTime(2026, 9, 13),
            Today,
            new BillingCompliancePolicyOptions(AllowPastEffectiveDates: true),
            "Correcting the date entered on the prior policy.");

        Assert.False(defaultDecision.Accepted);
        Assert.False(enabledWithoutReason.Accepted);
        Assert.True(enabledWithReason.Accepted, string.Join(" ", enabledWithReason.Errors));
    }

    [Fact]
    public void PolicyChangeRequiresAnEnforcementDate()
    {
        var decision = BillingCompliancePolicyRules.ValidateChange(
            BillingComplianceRequirements.Pcp,
            effectiveOn: null,
            Today);

        Assert.False(decision.Accepted);
        Assert.Contains(decision.Errors, error => error.Contains("date", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PolicyImpactSeparatesDraftAndFinalizedRecordsAndChangedBlockers()
    {
        var pcp = Obligation(
            "pcp-2026", "PCP", BillingComplianceRequirements.Pcp,
            new DateTime(2026, 3, 7), completedDate: null);
        var assessment = Obligation(
            "ca-2026", "Comprehensive Assessment",
            BillingComplianceRequirements.ComprehensiveAssessment,
            new DateTime(2025, 12, 7), completedDate: null);
        BillingCompliancePolicyImpactRecordSnapshot[] records =
        [
            new(1, 42, new DateTime(2026, 3, 8), false, 0, 0, [pcp]),
            new(2, 42, new DateTime(2026, 3, 8), true, 0, 1, [pcp, assessment]),
            new(3, 42, new DateTime(2026, 3, 8), false, 1, 0, [assessment])
        ];

        var preview = BillingCompliancePolicyImpactRules.Preview(
            agencyId: 7,
            fallbackRequirements: BillingComplianceGate.DefaultRequirements,
            [Policy(1, new DateTime(2026, 1, 1), BillingComplianceRequirements.Pcp)],
            effectiveOn: new DateTime(2026, 3, 1),
            proposedRequirements: BillingComplianceRequirements.ComprehensiveAssessment,
            records);

        Assert.Equal(3, preview.NotesEvaluated);
        Assert.Equal(2, preview.ClaimRecordsEvaluated);
        Assert.Equal(new BillingCompliancePolicyImpactBucket(1, 1, 0),
            preview.DraftOrUnsubmittedNotes);
        Assert.Equal(new BillingCompliancePolicyImpactBucket(0, 0, 1),
            preview.SubmittedOrFinalizedNotes);
        Assert.Equal(new BillingCompliancePolicyImpactBucket(1, 0, 0),
            preview.DraftClaimRecords);
        Assert.Equal(new BillingCompliancePolicyImpactBucket(0, 0, 1),
            preview.SubmittedOrFinalizedClaimRecords);
    }

    [Fact]
    public void PolicyImpactDoesNotOverrideALaterExistingPolicy()
    {
        var record = new BillingCompliancePolicyImpactRecordSnapshot(
            10,
            42,
            new DateTime(2026, 8, 2),
            false,
            0,
            0,
            [Obligation(
                "pcp-2026", "PCP", BillingComplianceRequirements.Pcp,
                new DateTime(2026, 3, 7), completedDate: null)]);

        var preview = BillingCompliancePolicyImpactRules.Preview(
            agencyId: 7,
            fallbackRequirements: BillingComplianceGate.DefaultRequirements,
            [
                Policy(1, new DateTime(2026, 1, 1), BillingComplianceRequirements.Pcp),
                Policy(2, new DateTime(2026, 8, 1), BillingComplianceRequirements.Pcp)
            ],
            effectiveOn: new DateTime(2026, 7, 1),
            proposedRequirements: BillingComplianceRequirements.None,
            [record]);

        Assert.Equal(0, preview.DraftOrUnsubmittedNotes.TotalAffected);
    }

    [Fact]
    public void RecoveryPlanChecksEveryOtherwiseBillableUnbilledLapseNoteByDefault()
    {
        var plan = BillingComplianceRecoveryRules.Prepare(
            agencyId: 7,
            personId: 42,
            [Policy(1, new DateTime(2026, 1, 1), BillingComplianceRequirements.Pcp)],
            [Obligation("pcp-2026", "PCP effective Mar 7, 2026", BillingComplianceRequirements.Pcp,
                new DateTime(2026, 3, 7), new DateTime(2026, 3, 10))],
            [
                new BillingRecoveryNoteSnapshot(1, 42, new DateTime(2026, 3, 7)),
                new BillingRecoveryNoteSnapshot(2, 42, new DateTime(2026, 3, 8)),
                new BillingRecoveryNoteSnapshot(3, 42, new DateTime(2026, 3, 9)),
                new BillingRecoveryNoteSnapshot(4, 42, new DateTime(2026, 3, 10)),
                new BillingRecoveryNoteSnapshot(5, 42, new DateTime(2026, 3, 8), IsSubmittedOrBilled: true)
            ],
            Today);

        Assert.Empty(plan.UnresolvedNoteIds);
        Assert.Equal([2, 3], plan.NoteOptions.Select(option => option.NoteId));
        Assert.All(plan.NoteOptions, option => Assert.True(option.IsSelectedByDefault));
    }

    [Fact]
    public void RecoveryWaitsUntilEveryOverlappingBlockerIsComplete()
    {
        var plan = BillingComplianceRecoveryRules.Prepare(
            agencyId: 7,
            personId: 42,
            [Policy(1, new DateTime(2026, 1, 1),
                BillingComplianceRequirements.Pcp | BillingComplianceRequirements.ComprehensiveAssessment)],
            [
                Obligation("pcp-2026", "PCP effective Mar 7, 2026", BillingComplianceRequirements.Pcp,
                    new DateTime(2026, 3, 7), new DateTime(2026, 3, 10)),
                Obligation("ca-2026", "Comprehensive Assessment for Mar 7, 2026",
                    BillingComplianceRequirements.ComprehensiveAssessment,
                    new DateTime(2025, 12, 7), completedDate: null)
            ],
            [new BillingRecoveryNoteSnapshot(10, 42, new DateTime(2026, 3, 8))],
            Today);

        Assert.Empty(plan.NoteOptions);
        Assert.Equal([10], plan.UnresolvedNoteIds);
    }

    [Fact]
    public void RecoveryDoesNotOfferAnEvidenceFreeCompletion()
    {
        var plan = BillingComplianceRecoveryRules.Prepare(
            agencyId: 7,
            personId: 42,
            [Policy(1, new DateTime(2026, 1, 1), BillingComplianceRequirements.Pcp)],
            [new BillingComplianceObligationSnapshot(
                "pcp-without-evidence", 42, "PCP", BillingComplianceRequirements.Pcp,
                new DateTime(2026, 3, 7), new DateTime(2026, 3, 10))],
            [new BillingRecoveryNoteSnapshot(11, 42, new DateTime(2026, 3, 8))],
            Today);

        Assert.Empty(plan.NoteOptions);
        Assert.Equal([11], plan.UnresolvedNoteIds);
    }

    [Fact]
    public void RecoveryDecisionNamesEveryBlockerAndOnlyReleasesSelectedNotes()
    {
        var requirements =
            BillingComplianceRequirements.Pcp |
            BillingComplianceRequirements.ComprehensiveAssessment;
        var policy = Policy(1, new DateTime(2026, 1, 1), requirements);
        BillingComplianceObligationSnapshot[] obligations =
        [
            Obligation("pcp-2026", "PCP effective Mar 7, 2026", BillingComplianceRequirements.Pcp,
                new DateTime(2026, 3, 7), new DateTime(2026, 3, 10)),
            Obligation("ca-2026", "Comprehensive Assessment for Mar 7, 2026",
                BillingComplianceRequirements.ComprehensiveAssessment,
                new DateTime(2025, 12, 7), new DateTime(2026, 3, 9))
        ];
        var notes = new[]
        {
            new BillingRecoveryNoteSnapshot(20, 42, new DateTime(2026, 3, 8)),
            new BillingRecoveryNoteSnapshot(21, 42, new DateTime(2026, 3, 8))
        };
        var plan = BillingComplianceRecoveryRules.Prepare(
            agencyId: 7, personId: 42, [policy], obligations, notes, Today);

        var result = BillingComplianceRecoveryRules.CreateDecision(
            plan,
            selectedNoteIds: [20],
            adminUserId: 9,
            recordedAtUtc: new DateTime(2026, 9, 14, 16, 30, 0, DateTimeKind.Utc),
            explanation: "Compliance is restored; release the selected service.",
            attestationConfirmed: true);

        Assert.True(result.Accepted, string.Join(" ", result.Errors));
        Assert.NotNull(result.Decision);
        Assert.Equal(["ca-2026", "pcp-2026"],
            result.Decision.Obligations.Select(obligation => obligation.ObligationId));
        Assert.Equal([20], result.Decision.NoteIds);
        Assert.True(BillingComplianceRecoveryRules.IsReleased(
            notes[0], obligations, policy, result.Decision));
        Assert.False(BillingComplianceRecoveryRules.IsReleased(
            notes[1], obligations, policy, result.Decision));
    }

    [Fact]
    public void RecoveryRequiresAnAdminAttestationAndExplanation()
    {
        var policy = Policy(1, new DateTime(2026, 1, 1), BillingComplianceRequirements.Pcp);
        var obligation = Obligation(
            "pcp-2026", "PCP effective Mar 7, 2026", BillingComplianceRequirements.Pcp,
            new DateTime(2026, 3, 7), new DateTime(2026, 3, 10));
        var plan = BillingComplianceRecoveryRules.Prepare(
            agencyId: 7,
            personId: 42,
            [policy],
            [obligation],
            [new BillingRecoveryNoteSnapshot(30, 42, new DateTime(2026, 3, 8))],
            Today);

        var result = BillingComplianceRecoveryRules.CreateDecision(
            plan, [30], 9,
            new DateTime(2026, 9, 14, 16, 30, 0, DateTimeKind.Utc),
            explanation: " ",
            attestationConfirmed: false);

        Assert.False(result.Accepted);
        Assert.Null(result.Decision);
        Assert.Equal(2, result.Errors.Count);
    }

    private static BillingCompliancePolicyVersionSnapshot Policy(
        long id,
        DateTime effectiveOn,
        BillingComplianceRequirements requirements,
        int agencyId = 7) =>
        new(id, agencyId, effectiveOn, requirements);

    private static BillingComplianceObligationSnapshot Obligation(
        string id,
        string name,
        BillingComplianceRequirements requirement,
        DateTime dueDate,
        DateTime? completedDate) =>
        new(id, 42, name, requirement, dueDate, completedDate,
            EvidenceId: completedDate is null ? null : $"form-attestation:{id}");
}
