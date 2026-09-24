using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sati.Persistence.Migrations
{
    /// <summary>
    /// Reconciles an unambiguous Scheduled Work Agenda fan-out created across
    /// the 1.3.23 exact-form-link transition. The lowest-ID exact-linked row
    /// remains the active plan; the legacy row, any other exact-linked copies,
    /// and one PHI-minimized audit event per cancellation are retained.
    /// </summary>
    public partial class ReconcileDuplicateScheduledAgendaNotes : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                SET NOCOUNT ON;
                SET XACT_ABORT ON;

                DECLARE @PairCandidates TABLE
                (
                    LegacyNoteId int NOT NULL,
                    LinkedNoteId int NOT NULL,
                    LinkedFormId int NOT NULL,
                    AgencyId int NOT NULL,
                    LegacyRevision int NOT NULL,
                    LinkedRevision int NOT NULL,
                    LegacyMatches int NOT NULL,
                    HasValidForm bit NOT NULL,
                    PRIMARY KEY (LegacyNoteId, LinkedNoteId)
                );

                ;WITH StructuralPairs AS
                (
                    SELECT
                        legacy.Id AS LegacyNoteId,
                        linked.Id AS LinkedNoteId,
                        linked.FormId AS LinkedFormId,
                        legacy.AgencyId,
                        legacy.Revision AS LegacyRevision,
                        linked.Revision AS LinkedRevision,
                        COUNT(*) OVER (PARTITION BY linked.Id) AS LegacyMatches,
                        CONVERT(bit, CASE
                            WHEN formRow.Id IS NOT NULL THEN 1
                            ELSE 0
                        END) AS HasValidForm
                    FROM dbo.Notes AS legacy WITH (UPDLOCK, HOLDLOCK)
                    INNER JOIN dbo.Notes AS linked WITH (UPDLOCK, HOLDLOCK)
                        ON linked.PersonId = legacy.PersonId
                       AND linked.AgencyId = legacy.AgencyId
                       AND linked.EventDate = legacy.EventDate
                       AND linked.Status = legacy.Status
                       AND linked.Minutes = legacy.Minutes
                       AND (linked.StartTime = legacy.StartTime OR
                            (linked.StartTime IS NULL AND legacy.StartTime IS NULL))
                       AND linked.FormType = legacy.FormType
                       AND linked.NoteType = legacy.NoteType
                       AND (linked.Activities = legacy.Activities OR
                            (linked.Activities IS NULL AND legacy.Activities IS NULL))
                       AND (linked.GoalProgress = legacy.GoalProgress OR
                            (linked.GoalProgress IS NULL AND legacy.GoalProgress IS NULL))
                       AND linked.Narrative COLLATE Latin1_General_100_BIN2 =
                           legacy.Narrative COLLATE Latin1_General_100_BIN2
                       AND DATALENGTH(linked.Narrative) = DATALENGTH(legacy.Narrative)
                    INNER JOIN dbo.People AS personRow WITH (UPDLOCK, HOLDLOCK)
                        ON personRow.Id = linked.PersonId
                       AND personRow.AgencyId = linked.AgencyId
                    INNER JOIN dbo.Users AS ownerRow WITH (UPDLOCK, HOLDLOCK)
                        ON ownerRow.Id = personRow.UserId
                       AND ownerRow.AgencyId = linked.AgencyId
                    LEFT JOIN dbo.Forms AS formRow WITH (UPDLOCK, HOLDLOCK)
                        ON formRow.Id = linked.FormId
                       AND formRow.PersonId = linked.PersonId
                       AND formRow.Type = CASE linked.FormType
                           WHEN 0 THEN N'Q1R'
                           WHEN 1 THEN N'Q2R'
                           WHEN 2 THEN N'Q3R'
                           WHEN 3 THEN N'Q4R'
                           WHEN 4 THEN N'PCP'
                           WHEN 5 THEN N'ComprehensiveAssessment'
                           WHEN 6 THEN N'Reclassification'
                           WHEN 7 THEN N'SafetyPlan'
                           WHEN 8 THEN N'PrivacyPractices'
                       END
                    WHERE legacy.Status = 0
                      AND legacy.NoteType = 2
                      AND legacy.FormType BETWEEN 0 AND 8
                      AND legacy.EventDate IS NOT NULL
                      AND legacy.AgencyId IS NOT NULL
                      AND legacy.Minutes = 15
                      AND linked.Minutes = 15
                      AND legacy.StartTime IS NULL
                      AND linked.StartTime IS NULL
                      AND legacy.GoalProgress IS NULL
                      AND linked.GoalProgress IS NULL
                      AND legacy.Revision >= 1
                      AND linked.Revision >= 1
                      AND legacy.FormId IS NULL
                      AND linked.FormId IS NOT NULL
                      AND legacy.ReleaseObligationId IS NULL
                      AND linked.ReleaseObligationId IS NULL
                      AND (legacy.Activities IS NULL OR legacy.Activities = 8)
                      AND (linked.Activities IS NULL OR linked.Activities = 8)
                      AND legacy.CaseManagerJustification IS NULL
                      AND linked.CaseManagerJustification IS NULL
                      AND legacy.FormDateCorrectionReason IS NULL
                      AND linked.FormDateCorrectionReason IS NULL
                      AND legacy.VisitDocumentationJson IS NULL
                      AND linked.VisitDocumentationJson IS NULL
                      AND legacy.ReturnReason IS NULL
                      AND linked.ReturnReason IS NULL
                      AND legacy.ReturnedById IS NULL
                      AND linked.ReturnedById IS NULL
                      AND legacy.ReturnedAt IS NULL
                      AND linked.ReturnedAt IS NULL
                      AND legacy.ApprovedById IS NULL
                      AND linked.ApprovedById IS NULL
                      AND legacy.ApprovedAt IS NULL
                      AND linked.ApprovedAt IS NULL
                      AND legacy.ComplianceOverride = 0
                      AND linked.ComplianceOverride = 0
                      AND legacy.OverrideReason IS NULL
                      AND linked.OverrideReason IS NULL
                      AND legacy.OverrideApprovedById IS NULL
                      AND linked.OverrideApprovedById IS NULL
                      AND legacy.OverrideApprovedAt IS NULL
                      AND linked.OverrideApprovedAt IS NULL
                      AND legacy.OverrideAttestationConfirmed = 0
                      AND linked.OverrideAttestationConfirmed = 0
                      AND legacy.OverrideObligationIdsJson IS NULL
                      AND linked.OverrideObligationIdsJson IS NULL
                )
                INSERT INTO @PairCandidates
                    (LegacyNoteId, LinkedNoteId, LinkedFormId, AgencyId,
                     LegacyRevision, LinkedRevision, LegacyMatches, HasValidForm)
                SELECT
                    LegacyNoteId, LinkedNoteId, LinkedFormId, AgencyId,
                    LegacyRevision, LinkedRevision, LegacyMatches, HasValidForm
                FROM StructuralPairs;

                DECLARE @CandidateGroups TABLE
                (
                    LegacyNoteId int NOT NULL PRIMARY KEY,
                    SurvivingNoteId int NOT NULL UNIQUE,
                    SurvivingFormId int NOT NULL,
                    AgencyId int NOT NULL,
                    LegacyRevision int NOT NULL,
                    SurvivingRevision int NOT NULL,
                    LinkedCount int NOT NULL
                );

                ;WITH UnambiguousGroups AS
                (
                    SELECT
                        pair.LegacyNoteId,
                        MIN(pair.LinkedNoteId) AS SurvivingNoteId,
                        MIN(pair.LinkedFormId) AS SurvivingFormId,
                        MIN(pair.AgencyId) AS AgencyId,
                        MIN(pair.LegacyRevision) AS LegacyRevision,
                        COUNT(*) AS LinkedCount
                    FROM @PairCandidates AS pair
                    GROUP BY pair.LegacyNoteId
                    HAVING MIN(pair.LinkedFormId) = MAX(pair.LinkedFormId)
                       AND MIN(CONVERT(int, pair.HasValidForm)) = 1
                       AND MAX(pair.LegacyMatches) = 1
                )
                INSERT INTO @CandidateGroups
                    (LegacyNoteId, SurvivingNoteId, SurvivingFormId, AgencyId,
                     LegacyRevision, SurvivingRevision, LinkedCount)
                SELECT
                    grouped.LegacyNoteId,
                    grouped.SurvivingNoteId,
                    grouped.SurvivingFormId,
                    grouped.AgencyId,
                    grouped.LegacyRevision,
                    survivor.LinkedRevision,
                    grouped.LinkedCount
                FROM UnambiguousGroups AS grouped
                INNER JOIN @PairCandidates AS survivor
                    ON survivor.LegacyNoteId = grouped.LegacyNoteId
                   AND survivor.LinkedNoteId = grouped.SurvivingNoteId;

                DECLARE @CandidateMembers TABLE
                (
                    LegacyNoteId int NOT NULL,
                    NoteId int NOT NULL,
                    Revision int NOT NULL,
                    IsLegacy bit NOT NULL,
                    PRIMARY KEY (LegacyNoteId, NoteId)
                );

                INSERT INTO @CandidateMembers (LegacyNoteId, NoteId, Revision, IsLegacy)
                SELECT LegacyNoteId, LegacyNoteId, LegacyRevision, CONVERT(bit, 1)
                FROM @CandidateGroups
                UNION ALL
                SELECT pair.LegacyNoteId, pair.LinkedNoteId, pair.LinkedRevision, CONVERT(bit, 0)
                FROM @PairCandidates AS pair
                INNER JOIN @CandidateGroups AS candidate
                    ON candidate.LegacyNoteId = pair.LegacyNoteId;

                -- A plan that has become evidence, entered billing/recovery review,
                -- or acquired a meaningful workflow audit is not an automatic repair.
                DELETE candidate
                FROM @CandidateGroups AS candidate
                WHERE EXISTS
                    (
                        SELECT 1
                        FROM @CandidateMembers AS member
                        WHERE member.LegacyNoteId = candidate.LegacyNoteId
                          AND
                          (
                              EXISTS (SELECT 1 FROM dbo.ClaimLines AS row WITH (UPDLOCK, HOLDLOCK) WHERE row.NoteId = member.NoteId)
                           OR EXISTS (SELECT 1 FROM dbo.ClaimCorrections AS row WITH (UPDLOCK, HOLDLOCK) WHERE row.NoteId = member.NoteId)
                           OR EXISTS (SELECT 1 FROM dbo.BillingComplianceRecoveryNotes AS row WITH (UPDLOCK, HOLDLOCK) WHERE row.NoteId = member.NoteId)
                           OR EXISTS (SELECT 1 FROM dbo.BillingCompliancePolicyReviewFlags AS row WITH (UPDLOCK, HOLDLOCK) WHERE row.NoteId = member.NoteId)
                           OR EXISTS (SELECT 1 FROM dbo.FormAttestationChangeReviewFlags AS row WITH (UPDLOCK, HOLDLOCK) WHERE row.NoteId = member.NoteId)
                           OR EXISTS (SELECT 1 FROM dbo.FormAttestations AS row WITH (UPDLOCK, HOLDLOCK) WHERE row.EvidenceNoteId = member.NoteId)
                           OR EXISTS (SELECT 1 FROM dbo.ReleaseObligationAttestations AS row WITH (UPDLOCK, HOLDLOCK) WHERE row.EvidenceNoteId = member.NoteId)
                           OR EXISTS
                              (
                                  SELECT 1
                                  FROM dbo.AuditEvents AS auditRow WITH (UPDLOCK, HOLDLOCK)
                                  WHERE auditRow.ResourceType = N'Note'
                                    AND auditRow.ResourceId = CONVERT(nvarchar(50), member.NoteId)
                                    AND auditRow.[Action] NOT IN (N'note.created', N'note.updated')
                              )
                          )
                    );

                DECLARE @Cancellations TABLE
                (
                    NoteId int NOT NULL PRIMARY KEY,
                    LegacyNoteId int NOT NULL,
                    SurvivingNoteId int NOT NULL,
                    SurvivingFormId int NOT NULL,
                    AgencyId int NOT NULL,
                    PreviousRevision int NOT NULL,
                    IsLegacy bit NOT NULL
                );

                INSERT INTO @Cancellations
                    (NoteId, LegacyNoteId, SurvivingNoteId, SurvivingFormId,
                     AgencyId, PreviousRevision, IsLegacy)
                SELECT
                    member.NoteId,
                    candidate.LegacyNoteId,
                    candidate.SurvivingNoteId,
                    candidate.SurvivingFormId,
                    candidate.AgencyId,
                    member.Revision,
                    member.IsLegacy
                FROM @CandidateGroups AS candidate
                INNER JOIN @CandidateMembers AS member
                    ON member.LegacyNoteId = candidate.LegacyNoteId
                   AND member.NoteId <> candidate.SurvivingNoteId;

                DECLARE @Repaired TABLE
                (
                    NoteId int NOT NULL PRIMARY KEY,
                    PreviousRevision int NOT NULL,
                    NewRevision int NOT NULL
                );

                UPDATE target
                   SET Status = 4,
                       Revision = target.Revision + 1
                OUTPUT inserted.Id, deleted.Revision, inserted.Revision
                    INTO @Repaired (NoteId, PreviousRevision, NewRevision)
                FROM dbo.Notes AS target
                INNER JOIN @Cancellations AS cancellation
                    ON cancellation.NoteId = target.Id
                   AND cancellation.PreviousRevision = target.Revision
                INNER JOIN @CandidateGroups AS candidate
                    ON candidate.LegacyNoteId = cancellation.LegacyNoteId
                INNER JOIN dbo.Notes AS survivor
                    ON survivor.Id = candidate.SurvivingNoteId
                   AND survivor.Revision = candidate.SurvivingRevision
                   AND survivor.Status = 0
                   AND survivor.FormId = candidate.SurvivingFormId
                WHERE target.Status = 0
                  AND
                  (
                      (cancellation.IsLegacy = 1 AND target.FormId IS NULL)
                   OR (cancellation.IsLegacy = 0 AND target.FormId = candidate.SurvivingFormId)
                  );

                INSERT INTO dbo.AuditEvents
                    (EventId, AgencyId, ActorUserId, [Action], ResourceType, ResourceId,
                     OccurredAtUtc, CorrelationId, MetadataJson)
                SELECT
                    NEWID(),
                    cancellation.AgencyId,
                    0,
                    N'note.scheduled-duplicate-cancelled',
                    N'Note',
                    CONVERT(nvarchar(50), repaired.NoteId),
                    SYSUTCDATETIME(),
                    CONCAT(N'migration-work-agenda-dedup-',
                           REPLACE(CONVERT(nvarchar(36), NEWID()), N'-', N'')),
                    CONCAT(
                        N'{"reason":"exact-form-work-agenda-fan-out",',
                        N'"duplicateKind":"',
                        CASE WHEN cancellation.IsLegacy = 1
                            THEN N'legacy-unlinked' ELSE N'extra-exact-linked' END,
                        N'","survivingNoteId":', cancellation.SurvivingNoteId, N',',
                        N'"survivingFormId":', cancellation.SurvivingFormId, N',',
                        N'"previousStatus":"Scheduled",',
                        N'"newStatus":"Cancelled",',
                        N'"previousRevision":', repaired.PreviousRevision, N',',
                        N'"newRevision":', repaired.NewRevision, N'}')
                FROM @Repaired AS repaired
                INNER JOIN @Cancellations AS cancellation
                    ON cancellation.NoteId = repaired.NoteId;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deliberately irreversible. Reactivating a row later would recreate the
            // duplicate and could overwrite a user's post-repair scheduling decision.
        }
    }
}
