using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sati.Models;

namespace Sati.Data;

/// <summary>
/// Collapses duplicate compliance-form rows without guessing which annual
/// obligation a row represents.
///
/// The current identity is (PersonId, Type, TargetEffectiveDate). DueDate is a
/// mutable deadline and is never an identity once TargetEffectiveDate exists. A
/// narrowly isolated legacy path still groups targetless rows by the old
/// (PersonId, Type, DueDate) key, but it can run only at the migration immediately
/// before that old unique index was introduced.
///
/// A merge is mechanical only when the copies contain at most one completion date
/// and, for target-based rows, one deadline. Different completion dates or
/// different deadlines would require choosing a billing fact, so those groups are
/// reported and left untouched.
/// </summary>
public static class FormDuplicateRepair
{
    public const int SystemActorUserId = 0;

    internal const string LegacyRepairPrerequisiteMigration =
        "20260830231500_SeparateAgencyWideSupervision";

    internal const string LegacyUniqueIndexMigration =
        "20260901150802_AddUniqueFormPersonTypeDueDateIndex";

    /// <summary>One duplicated annual obligation.</summary>
    public sealed record DuplicateGroup(
        int PersonId,
        FormType Type,
        DateTime? TargetEffectiveDate,
        DateTime? LegacyDueDate,
        IReadOnlyList<int> FormIds,
        IReadOnlyList<DateTime> DistinctDueDates,
        IReadOnlyList<DateTime> DistinctCompletedDates)
    {
        /// <summary>
        /// True only for the bounded pre-TargetEffectiveDate migration path. A
        /// target-based group never falls back to deadline identity.
        /// </summary>
        public bool UsesLegacyDueDateIdentity => TargetEffectiveDate is null;

        public bool HasConflictingDeadlines =>
            !UsesLegacyDueDateIdentity && DistinctDueDates.Count > 1;

        public bool HasConflictingCompletions => DistinctCompletedDates.Count > 1;

        public bool IsConflicted =>
            HasConflictingDeadlines || HasConflictingCompletions;

        public int SurplusRows => FormIds.Count - 1;

        internal DateTime SortDate =>
            TargetEffectiveDate ?? LegacyDueDate ?? DateTime.MaxValue;
    }

    public sealed record RepairPlan(IReadOnlyList<DuplicateGroup> Groups)
    {
        public IReadOnlyList<DuplicateGroup> Mergeable =>
            Groups.Where(group => !group.IsConflicted).ToList();

        public IReadOnlyList<DuplicateGroup> Conflicted =>
            Groups.Where(group => group.IsConflicted).ToList();

        public int RowsToRemove => Mergeable.Sum(group => group.SurplusRows);

        public bool HasWork => Mergeable.Count > 0;

        public bool LeavesDuplicates => Conflicted.Count > 0;
    }

    public sealed record RepairResult(
        int GroupsMerged,
        int RowsRemoved,
        int GroupsLeftConflicted,
        IReadOnlyList<DuplicateGroup> Conflicts);

    private sealed record Candidate(
        int Id,
        int PersonId,
        FormType Type,
        DateTime? TargetEffectiveDate,
        DateTime DueDate,
        DateTime? CompletedDate,
        DateTime? OpenedDate,
        bool IsCompliant,
        int AgencyId);

    private readonly record struct Identity(
        int PersonId,
        FormType Type,
        DateTime? TargetEffectiveDate,
        DateTime? LegacyDueDate);

    /// <summary>
    /// Read-only current-schema plan. Refuses targetless rows rather than quietly
    /// reviving the retired deadline-as-identity rule.
    /// </summary>
    public static async Task<RepairPlan> PlanAsync(
        SatiContext context,
        CancellationToken cancellationToken = default)
    {
        var forms = await context.Forms.AsNoTracking().ToListAsync(cancellationToken);
        return Plan(forms);
    }

    /// <summary>
    /// Merges current-schema duplicates by explicit target identity. This method is
    /// not used to prepare the old due-date unique-index migration; that path cannot
    /// materialize the current EF model and is isolated below.
    /// </summary>
    public static async Task<RepairResult> ApplyAsync(
        SatiContext context,
        CancellationToken cancellationToken = default)
    {
        var forms = await context.Forms.ToListAsync(cancellationToken);
        var plan = Plan(forms);

        if (!plan.HasWork)
            return new RepairResult(0, 0, plan.Conflicted.Count, plan.Conflicted);

        var personIds = plan.Mergeable.Select(group => group.PersonId).Distinct().ToList();
        var agencyByPerson = await context.People
            .Where(person => personIds.Contains(person.Id))
            .Select(person => new { person.Id, person.AgencyId })
            .ToDictionaryAsync(entry => entry.Id, entry => entry.AgencyId, cancellationToken);

        var byId = forms.ToDictionary(form => form.Id);
        var rowsRemoved = 0;

        foreach (var group in plan.Mergeable)
        {
            var copies = group.FormIds.Select(id => byId[id]).ToList();
            var survivor = ChooseSurvivor(copies);

            if (group.DistinctCompletedDates.Count == 1 &&
                survivor.CompletedDate?.Date != group.DistinctCompletedDates[0])
            {
                context.Entry(survivor)
                    .Property(form => form.CompletedDate)
                    .CurrentValue = group.DistinctCompletedDates[0];
            }

            var earliestOpened = copies
                .Where(copy => copy.OpenedDate.HasValue)
                .Select(copy => copy.OpenedDate!.Value.Date)
                .DefaultIfEmpty()
                .Min();
            if (earliestOpened != default &&
                (survivor.OpenedDate is null || earliestOpened < survivor.OpenedDate.Value.Date))
            {
                survivor.OpenedDate = earliestOpened;
            }

            foreach (var duplicate in copies.Where(copy => copy.Id != survivor.Id))
            {
                context.Forms.Remove(duplicate);
                rowsRemoved++;

                context.AuditEvents.Add(new AuditEvent
                {
                    AgencyId = agencyByPerson.TryGetValue(group.PersonId, out var agencyId)
                        ? agencyId ?? 0
                        : 0,
                    ActorUserId = SystemActorUserId,
                    Action = LocalAuditActions.FormDuplicateRemoved,
                    ResourceType = "Form",
                    ResourceId = duplicate.Id.ToString(CultureInfo.InvariantCulture),
                    CorrelationId = $"desktop-form-dedup-{Guid.NewGuid():N}",
                    MetadataJson = DescribeCurrentRemoval(group, duplicate, survivor)
                });
            }
        }

        await context.SaveChangesAsync(cancellationToken);

        return new RepairResult(
            plan.Mergeable.Count,
            rowsRemoved,
            plan.Conflicted.Count,
            plan.Conflicted);
    }

    /// <summary>
    /// Current-schema classifier. Every row must carry the explicit target written
    /// by CorrectAnnualComplianceAndBillingPolicy. There is intentionally no runtime
    /// fallback to DueDate.
    /// </summary>
    public static RepairPlan Plan(IReadOnlyList<Form> forms)
    {
        ArgumentNullException.ThrowIfNull(forms);

        if (forms.Any(form => form.TargetEffectiveDate == default))
        {
            throw new InvalidOperationException(
                "Target-based duplicate repair refuses forms without TargetEffectiveDate. " +
                "The deadline-based fallback is restricted to the pre-target migration stage.");
        }

        return BuildPlan(
            forms.Select(form => new Candidate(
                form.Id,
                form.PersonId,
                form.Type,
                form.TargetEffectiveDate.Date,
                form.DueDate.Date,
                form.CompletedDate?.Date,
                form.OpenedDate?.Date,
                form.IsCompliant,
                0)),
            useLegacyDueDateIdentity: false);
    }

    /// <summary>
    /// Pure test seam for the one historical stage where forms had no target. It is
    /// internal so application callers cannot opt back into deadline identity.
    /// </summary>
    internal static RepairPlan PlanLegacyTargetlessRowsForMigration(
        IReadOnlyList<Form> forms)
    {
        ArgumentNullException.ThrowIfNull(forms);
        if (forms.Any(form => form.TargetEffectiveDate != default))
        {
            throw new InvalidOperationException(
                "Legacy duplicate repair accepts targetless pre-migration rows only.");
        }

        return BuildPlan(
            forms.Select(form => new Candidate(
                form.Id,
                form.PersonId,
                form.Type,
                null,
                form.DueDate.Date,
                form.CompletedDate?.Date,
                form.OpenedDate?.Date,
                form.IsCompliant,
                0)),
            useLegacyDueDateIdentity: true);
    }

    /// <summary>
    /// Prepares only migration 20260901150802. The current EF Form mapping cannot
    /// query that schema because TargetEffectiveDate does not exist yet, so this
    /// method uses a deliberately small raw projection. Its schema/history guard
    /// makes it impossible to invoke after the legacy stage or against a current
    /// target-based database.
    /// </summary>
    internal static async Task<RepairResult> ApplyLegacyPreTargetMigrationAsync(
        SatiContext context,
        CancellationToken cancellationToken = default)
    {
        if (!context.Database.IsSqlServer())
        {
            throw new NotSupportedException(
                "Legacy duplicate repair is supported only for the local SQL Server migration stage.");
        }

        var connection = context.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
            await connection.OpenAsync(cancellationToken);

        try
        {
            await using var transaction = await connection.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

            await ValidateLegacyStageAsync(connection, transaction, cancellationToken);
            var candidates = await LoadLegacyCandidatesAsync(
                connection,
                transaction,
                cancellationToken);
            var plan = BuildPlan(candidates, useLegacyDueDateIdentity: true);

            if (!plan.HasWork)
            {
                await transaction.CommitAsync(cancellationToken);
                return new RepairResult(0, 0, plan.Conflicted.Count, plan.Conflicted);
            }

            var byId = candidates.ToDictionary(candidate => candidate.Id);
            var rowsRemoved = 0;

            foreach (var group in plan.Mergeable)
            {
                var copies = group.FormIds.Select(id => byId[id]).ToList();
                var survivor = copies
                    .OrderByDescending(copy => copy.CompletedDate.HasValue)
                    .ThenByDescending(copy => copy.IsCompliant)
                    .ThenBy(copy => copy.Id)
                    .First();

                var completion = group.DistinctCompletedDates.SingleOrDefault();
                var earliestOpened = copies
                    .Where(copy => copy.OpenedDate.HasValue)
                    .Select(copy => copy.OpenedDate!.Value.Date)
                    .DefaultIfEmpty()
                    .Min();
                var isCompliant = copies.Any(copy => copy.IsCompliant);

                await UpdateLegacySurvivorAsync(
                    connection,
                    transaction,
                    survivor.Id,
                    completion == default ? null : completion,
                    earliestOpened == default ? null : earliestOpened,
                    isCompliant,
                    cancellationToken);

                var projectedSurvivor = survivor with
                {
                    CompletedDate = completion == default ? null : completion,
                    OpenedDate = earliestOpened == default ? null : earliestOpened,
                    IsCompliant = isCompliant
                };

                foreach (var duplicate in copies.Where(copy => copy.Id != survivor.Id))
                {
                    await DeleteLegacyDuplicateAsync(
                        connection,
                        transaction,
                        duplicate.Id,
                        cancellationToken);
                    await InsertLegacyRemovalAuditAsync(
                        connection,
                        transaction,
                        group,
                        duplicate,
                        projectedSurvivor,
                        cancellationToken);
                    rowsRemoved++;
                }
            }

            await transaction.CommitAsync(cancellationToken);
            return new RepairResult(
                plan.Mergeable.Count,
                rowsRemoved,
                plan.Conflicted.Count,
                plan.Conflicted);
        }
        finally
        {
            if (openedHere)
                await connection.CloseAsync();
        }
    }

    private static RepairPlan BuildPlan(
        IEnumerable<Candidate> source,
        bool useLegacyDueDateIdentity)
    {
        var candidates = source.ToList();
        if (useLegacyDueDateIdentity && candidates.Any(candidate => candidate.TargetEffectiveDate is not null))
        {
            throw new InvalidOperationException(
                "Legacy duplicate repair cannot mix explicit target identities with targetless rows.");
        }
        if (!useLegacyDueDateIdentity && candidates.Any(candidate => candidate.TargetEffectiveDate is null))
        {
            throw new InvalidOperationException(
                "Target-based duplicate repair cannot classify a targetless row.");
        }

        var groups = candidates
            .GroupBy(candidate => new Identity(
                candidate.PersonId,
                candidate.Type,
                useLegacyDueDateIdentity ? null : candidate.TargetEffectiveDate,
                useLegacyDueDateIdentity ? candidate.DueDate.Date : null))
            .Where(group => group.Count() > 1)
            .Select(group => new DuplicateGroup(
                group.Key.PersonId,
                group.Key.Type,
                group.Key.TargetEffectiveDate,
                group.Key.LegacyDueDate,
                group.Select(candidate => candidate.Id).OrderBy(id => id).ToList(),
                group.Select(candidate => candidate.DueDate.Date)
                    .Distinct()
                    .OrderBy(date => date)
                    .ToList(),
                group.Where(candidate => candidate.CompletedDate.HasValue)
                    .Select(candidate => candidate.CompletedDate!.Value.Date)
                    .Distinct()
                    .OrderBy(date => date)
                    .ToList()))
            .OrderBy(group => group.PersonId)
            .ThenBy(group => group.SortDate)
            .ThenBy(group => group.Type)
            .ToList();

        return new RepairPlan(groups);
    }

    private static Form ChooseSurvivor(IReadOnlyList<Form> copies) =>
        copies
            .OrderByDescending(copy => copy.CompletedDate.HasValue)
            .ThenBy(copy => copy.Id)
            .First();

    private static async Task ValidateLegacyStageAsync(
        DbConnection connection,
        DbTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            IF OBJECT_ID(N'dbo.Forms', N'U') IS NULL
               OR OBJECT_ID(N'dbo.People', N'U') IS NULL
               OR OBJECT_ID(N'dbo.AuditEvents', N'U') IS NULL
               OR OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NULL
                THROW 50000, 'Legacy duplicate repair prerequisite schema is missing.', 1;

            IF COL_LENGTH(N'dbo.Forms', N'TargetEffectiveDate') IS NOT NULL
                THROW 50000, 'Legacy duplicate repair refuses a target-based Forms schema.', 1;

            IF COL_LENGTH(N'dbo.Forms', N'Id') IS NULL
               OR COL_LENGTH(N'dbo.Forms', N'PersonId') IS NULL
               OR COL_LENGTH(N'dbo.Forms', N'Type') IS NULL
               OR COL_LENGTH(N'dbo.Forms', N'DueDate') IS NULL
               OR COL_LENGTH(N'dbo.Forms', N'CompletedDate') IS NULL
               OR COL_LENGTH(N'dbo.Forms', N'OpenedDate') IS NULL
               OR COL_LENGTH(N'dbo.Forms', N'IsCompliant') IS NULL
               OR COL_LENGTH(N'dbo.People', N'AgencyId') IS NULL
                THROW 50000, 'Legacy duplicate repair found an unsupported pre-target schema.', 1;

            IF (SELECT MAX(MigrationId) FROM dbo.__EFMigrationsHistory) <> N'{LegacyRepairPrerequisiteMigration}'
                THROW 50000, 'Legacy duplicate repair may run only at its exact pre-index migration stage.', 1;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<List<Candidate>> LoadLegacyCandidatesAsync(
        DbConnection connection,
        DbTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT f.Id, f.PersonId, f.[Type], f.DueDate, f.CompletedDate,
                   f.OpenedDate, f.IsCompliant, ISNULL(p.AgencyId, 0)
            FROM dbo.Forms AS f WITH (UPDLOCK, HOLDLOCK)
            LEFT JOIN dbo.People AS p ON p.Id = f.PersonId
            ORDER BY f.PersonId, f.[Type], f.DueDate, f.Id;
            """;

        var candidates = new List<Candidate>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var storedType = reader.GetString(2);
            if (!Enum.TryParse<FormType>(storedType, ignoreCase: false, out var type) ||
                !Enum.IsDefined(type))
            {
                throw new InvalidOperationException(
                    $"Legacy duplicate repair found unsupported form type '{storedType}'.");
            }

            candidates.Add(new Candidate(
                reader.GetInt32(0),
                reader.GetInt32(1),
                type,
                null,
                reader.GetDateTime(3).Date,
                reader.IsDBNull(4) ? null : reader.GetDateTime(4).Date,
                reader.IsDBNull(5) ? null : reader.GetDateTime(5).Date,
                reader.GetBoolean(6),
                reader.GetInt32(7)));
        }

        return candidates;
    }

    private static async Task UpdateLegacySurvivorAsync(
        DbConnection connection,
        DbTransaction transaction,
        int formId,
        DateTime? completedDate,
        DateTime? openedDate,
        bool isCompliant,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE dbo.Forms
               SET CompletedDate = @completedDate,
                   OpenedDate = @openedDate,
                   IsCompliant = @isCompliant
             WHERE Id = @formId;
            """;
        AddParameter(command, "@completedDate", completedDate);
        AddParameter(command, "@openedDate", openedDate);
        AddParameter(command, "@isCompliant", isCompliant);
        AddParameter(command, "@formId", formId);

        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException("The legacy duplicate survivor changed during repair.");
    }

    private static async Task DeleteLegacyDuplicateAsync(
        DbConnection connection,
        DbTransaction transaction,
        int formId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM dbo.Forms WHERE Id = @formId;";
        AddParameter(command, "@formId", formId);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException("A legacy duplicate changed during repair.");
    }

    private static async Task InsertLegacyRemovalAuditAsync(
        DbConnection connection,
        DbTransaction transaction,
        DuplicateGroup group,
        Candidate removed,
        Candidate survivor,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO dbo.AuditEvents
                (EventId, AgencyId, ActorUserId, [Action], ResourceType, ResourceId,
                 OccurredAtUtc, CorrelationId, MetadataJson)
            VALUES
                (@eventId, @agencyId, @actorUserId, @action, N'Form', @resourceId,
                 @occurredAtUtc, @correlationId, @metadataJson);
            """;
        AddParameter(command, "@eventId", Guid.NewGuid());
        AddParameter(command, "@agencyId", removed.AgencyId);
        AddParameter(command, "@actorUserId", SystemActorUserId);
        AddParameter(command, "@action", LocalAuditActions.FormDuplicateRemoved);
        AddParameter(command, "@resourceId", removed.Id.ToString(CultureInfo.InvariantCulture));
        AddParameter(command, "@occurredAtUtc", DateTime.UtcNow);
        AddParameter(command, "@correlationId", $"desktop-form-dedup-{Guid.NewGuid():N}");
        AddParameter(command, "@metadataJson", DescribeLegacyRemoval(group, removed, survivor));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddParameter(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private static string DescribeCurrentRemoval(
        DuplicateGroup group,
        Form removed,
        Form survivor) =>
        JsonSerializer.Serialize(new
        {
            reason = "duplicate-compliance-form",
            identityMode = "target-effective-date",
            personId = group.PersonId,
            type = group.Type.ToString(),
            targetEffectiveDate = group.TargetEffectiveDate?.ToString("yyyy-MM-dd"),
            removedFormId = removed.Id,
            removedDueDate = removed.DueDate.ToString("yyyy-MM-dd"),
            removedCompletedDate = removed.CompletedDate?.ToString("yyyy-MM-dd"),
            survivingFormId = survivor.Id,
            survivingDueDate = survivor.DueDate.ToString("yyyy-MM-dd"),
            survivingCompletedDate = survivor.CompletedDate?.ToString("yyyy-MM-dd")
        });

    private static string DescribeLegacyRemoval(
        DuplicateGroup group,
        Candidate removed,
        Candidate survivor) =>
        JsonSerializer.Serialize(new
        {
            reason = "duplicate-compliance-form",
            identityMode = "legacy-due-date-pre-target-migration",
            personId = group.PersonId,
            type = group.Type.ToString(),
            dueDate = group.LegacyDueDate?.ToString("yyyy-MM-dd"),
            removedFormId = removed.Id,
            removedCompletedDate = removed.CompletedDate?.ToString("yyyy-MM-dd"),
            removedIsCompliant = removed.IsCompliant,
            survivingFormId = survivor.Id,
            survivingCompletedDate = survivor.CompletedDate?.ToString("yyyy-MM-dd"),
            survivingIsCompliant = survivor.IsCompliant
        });
}
