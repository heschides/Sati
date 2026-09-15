using System.Data;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sati.Api.Data;
using Sati.Api.Infrastructure;
using Sati.Api.Security;
using Sati.Contracts.V1;
using Sati.Models;

namespace Sati.Api.Endpoints;

internal static partial class ApiEndpoints
{
    private static void MapReleaseObligations(RouteGroupBuilder api)
    {
        api.MapGet("/people/{personId:int}/release-obligations", async Task<IResult> (
            int personId,
            DateTime targetEffectiveDate,
            ClaimsPrincipal principal,
            ApiDbContext db,
            ApiClock clock,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            var person = await LoadReleasePersonAsync(
                db, actor, personId, cancellationToken);
            if (person is null)
                return Results.NotFound();
            if (ValidateReleaseTarget(person.EffectiveDate, targetEffectiveDate) is { } error)
                return ReleaseValidation("targetEffectiveDate", error);

            var resolution = await ResolveReleaseAssignmentsAsync(
                db, person, actor.AgencyId, targetEffectiveDate, clock.Today, cancellationToken);
            var rows = await LoadReleaseRowsAsync(
                db, personId, targetEffectiveDate, cancellationToken);
            var issues = await LoadReleaseStatusIssuesAsync(
                db, personId, targetEffectiveDate, clock.Today, rows,
                resolution.Issues, cancellationToken);
            return Results.Ok(ToReleaseStatus(
                person, targetEffectiveDate, clock.Today, rows, issues));
        });

        api.MapPost("/people/{personId:int}/release-obligations/reconcile", async Task<IResult> (
            int personId,
            ReconcileReleaseObligationsRequest request,
            ClaimsPrincipal principal,
            ApiDbContext db,
            AuditTrail audit,
            ApiClock clock,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
            var person = await LoadReleasePersonAsync(
                db, actor, personId, cancellationToken);
            if (person is null)
                return Results.NotFound();
            if (ValidateReleaseTarget(person.EffectiveDate, request.TargetEffectiveDate) is { } error)
                return ReleaseValidation("targetEffectiveDate", error);

            var resolution = await ResolveReleaseAssignmentsAsync(
                db, person, actor.AgencyId, request.TargetEffectiveDate, clock.Today,
                cancellationToken);
            var rows = await LoadReleaseRowsAsync(
                db, personId, request.TargetEffectiveDate, cancellationToken);
            var changes = await ReconcileReleaseRowsAsync(
                db, person, actor.AgencyId, request.TargetEffectiveDate,
                resolution, rows, clock.UtcNow.UtcDateTime, cancellationToken);
            if (changes.CreatedKeys.Count != 0 || changes.RetiredKeys.Count != 0)
            {
                audit.Record(
                    actor,
                    AuditActions.ReleaseObligationsReconciled,
                    "Person",
                    personId,
                    JsonSerializer.Serialize(new
                    {
                        targetEffectiveDate = request.TargetEffectiveDate.Date.ToString("yyyy-MM-dd"),
                        created = changes.CreatedKeys,
                        retired = changes.RetiredKeys
                    }));
                await db.SaveChangesAsync(cancellationToken);
            }

            var issues = await LoadReleaseStatusIssuesAsync(
                db, personId, request.TargetEffectiveDate, clock.Today, rows,
                resolution.Issues, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Results.Ok(ToReleaseStatus(
                person, request.TargetEffectiveDate, clock.Today, rows, issues));
        });

        api.MapPost("/people/{personId:int}/release-obligations/{obligationId:guid}/attest",
            async Task<IResult> (
                int personId,
                Guid obligationId,
                AttestReleaseObligationRequest request,
                ClaimsPrincipal principal,
                ApiDbContext db,
                AuditTrail audit,
                ApiClock clock,
                CancellationToken cancellationToken) =>
            {
                var actor = Actor.From(principal);
                var person = await LoadReleasePersonAsync(
                    db, actor, personId, cancellationToken);
                if (person is null)
                    return Results.NotFound();
                var row = await LoadReleaseRowAsync(
                    db, actor.AgencyId, personId, obligationId, cancellationToken);
                if (row is null)
                    return Results.NotFound();
                if (row.CompletedOn is not null)
                    return Results.Conflict(new ApiErrorDto(
                        "release_already_attested",
                        "This release obligation is already attested.",
                        string.Empty));
                if (request.CompletedOn.Date < row.AvailableOn.Date)
                    return ReleaseValidation("completedOn",
                        $"This release obligation becomes available on {row.AvailableOn:yyyy-MM-dd}.");

                try
                {
                    row.AttestManually(
                        request.CompletedOn,
                        clock.Today,
                        actor.UserId == person.UserId
                            ? AttestationActorKind.CaseManager
                            : AttestationActorKind.Supervisor,
                        actor.UserId,
                        clock.UtcNow.UtcDateTime,
                        request.Reason);
                }
                catch (ArgumentException exception)
                {
                    return ReleaseValidation("completedOn", exception.Message);
                }

                audit.Record(
                    actor,
                    AuditActions.ReleaseObligationAttested,
                    "Person",
                    personId,
                    ReleaseAuditMetadata(row, request.CompletedOn, request.Reason));
                await db.SaveChangesAsync(cancellationToken);
                return Results.Ok(ToReleaseDto(row, clock.Today));
            });

        api.MapPost("/people/{personId:int}/release-obligations/{obligationId:guid}/withdraw",
            async Task<IResult> (
                int personId,
                Guid obligationId,
                WithdrawReleaseAuthorizationRequest request,
                ClaimsPrincipal principal,
                ApiDbContext db,
                AuditTrail audit,
                ApiClock clock,
                CancellationToken cancellationToken) =>
            {
                var actor = Actor.From(principal);
                if (await LoadReleasePersonAsync(db, actor, personId, cancellationToken) is null)
                    return Results.NotFound();
                var row = await LoadReleaseRowAsync(
                    db, actor.AgencyId, personId, obligationId, cancellationToken);
                if (row is null)
                    return Results.NotFound();
                try
                {
                    row.Withdraw(
                        request.WithdrawnOn,
                        clock.Today,
                        actor.UserId,
                        clock.UtcNow.UtcDateTime,
                        request.Reason);
                }
                catch (InvalidOperationException exception)
                {
                    return Results.Conflict(new ApiErrorDto(
                        "release_withdrawal_conflict", exception.Message, string.Empty));
                }
                catch (ArgumentException exception)
                {
                    return ReleaseValidation("withdrawnOn", exception.Message);
                }

                audit.Record(
                    actor,
                    AuditActions.ReleaseAuthorizationWithdrawn,
                    "Person",
                    personId,
                    ReleaseAuditMetadata(row, request.WithdrawnOn, request.Reason));
                await db.SaveChangesAsync(cancellationToken);
                return Results.Ok(ToReleaseDto(row, clock.Today));
            });
    }

    private static async Task<ServerPerson?> LoadReleasePersonAsync(
        ApiDbContext db,
        Actor actor,
        int personId,
        CancellationToken cancellationToken)
    {
        var person = await db.People.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == personId && item.AgencyId == actor.AgencyId,
            cancellationToken);
        return person is not null && await TenantAccess.CanAccessPersonAsync(
            db, actor, person, cancellationToken)
            ? person
            : null;
    }

    private static async Task<ReleaseAssignmentResolutionResult> ResolveReleaseAssignmentsAsync(
        ApiDbContext db,
        ServerPerson person,
        int agencyId,
        DateTime targetEffectiveDate,
        DateTime observedOn,
        CancellationToken cancellationToken)
    {
        var links = await (from link in db.PersonProviders.AsNoTracking()
                           join provider in db.Providers.AsNoTracking()
                               on link.ProviderId equals provider.Id
                           where link.PersonId == person.Id && provider.AgencyId == agencyId
                           select new ReleaseProviderLinkFact(
                               link.Id,
                               link.ProviderId,
                               provider.Type,
                               link.Role,
                               link.StartDate,
                               link.EndDate,
                               link.AssignmentKnownOn,
                               provider.Name))
            .ToListAsync(cancellationToken);
        return ReleaseAssignmentResolution.Resolve(
            targetEffectiveDate,
            observedOn,
            RequiresReleaseServiceProvider(person),
            links);
    }

    private static async Task ReconcileCurrentReleaseCyclesAsync(
        ApiDbContext db,
        AuditTrail audit,
        Actor actor,
        ServerPerson person,
        ApiClock clock,
        string source,
        CancellationToken cancellationToken)
    {
        if (person.EffectiveDate is not DateTime effectiveDate)
            return;

        var currentTarget = ComplianceScheduleRules.CurrentTargetEffectiveDate(
            effectiveDate, clock.Today);
        foreach (var target in new[] { currentTarget, currentTarget.AddYears(1) })
        {
            var resolution = await ResolveReleaseAssignmentsAsync(
                db, person, actor.AgencyId, target, clock.Today, cancellationToken);
            var rows = await LoadReleaseRowsAsync(
                db, person.Id, target, cancellationToken);
            var changes = await ReconcileReleaseRowsAsync(
                db, person, actor.AgencyId, target, resolution, rows,
                clock.UtcNow.UtcDateTime, cancellationToken);
            if (changes.CreatedKeys.Count == 0 && changes.RetiredKeys.Count == 0)
                continue;

            audit.Record(
                actor,
                AuditActions.ReleaseObligationsReconciled,
                "Person",
                person.Id,
                JsonSerializer.Serialize(new
                {
                    targetEffectiveDate = target.ToString("yyyy-MM-dd"),
                    created = changes.CreatedKeys,
                    retired = changes.RetiredKeys,
                    source
                }));
        }
    }

    private static bool RequiresReleaseServiceProvider(ServerPerson person) =>
        person.Waiver != 0 ||
        person.HasHomeSupport ||
        person.HasSelfDirectedHomeSupport ||
        person.HasSharedLiving ||
        person.HasCommunitySupport1To1 ||
        person.HasCommunitySupportSelfDirected ||
        person.HasCommunitySupportDayProgram;

    private static async Task<List<ReleaseObligation>> LoadReleaseRowsAsync(
        ApiDbContext db,
        int personId,
        DateTime targetEffectiveDate,
        CancellationToken cancellationToken) =>
        await db.ReleaseObligations
            .Include(item => item.Attestations)
            .Include(item => item.AuthorizationEvents)
            .Where(item => item.PersonId == personId &&
                           item.TargetEffectiveDate == targetEffectiveDate.Date)
            .OrderBy(item => item.DueOn)
            .ThenBy(item => item.StableKey)
            .ToListAsync(cancellationToken);

    private static async Task<Dictionary<int, IReadOnlyList<ReleaseObligation>>>
        LoadReleaseBillingRowsByPersonAsync(
            ApiDbContext db,
            IReadOnlyCollection<int> personIds,
            CancellationToken cancellationToken)
    {
        if (personIds.Count == 0)
            return [];

        return (await db.ReleaseObligations.AsNoTracking()
                .Include(item => item.Attestations)
                .Where(item => personIds.Contains(item.PersonId))
                .ToListAsync(cancellationToken))
            .GroupBy(item => item.PersonId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<ReleaseObligation>)group.ToList());
    }

    private static async Task<ReleaseObligation?> LoadReleaseRowAsync(
        ApiDbContext db,
        int agencyId,
        int personId,
        Guid obligationId,
        CancellationToken cancellationToken) =>
        await db.ReleaseObligations
            .Include(item => item.Attestations)
            .Include(item => item.AuthorizationEvents)
            .SingleOrDefaultAsync(item =>
                item.AgencyId == agencyId &&
                item.PersonId == personId &&
                item.ObligationId == obligationId,
                cancellationToken);

    private static async Task<IReadOnlyList<ReleaseLinkageIssue>> LoadReleaseStatusIssuesAsync(
        ApiDbContext db,
        int personId,
        DateTime targetEffectiveDate,
        DateTime today,
        IEnumerable<ReleaseObligation> rows,
        IReadOnlyList<ReleaseLinkageIssue> assignmentIssues,
        CancellationToken cancellationToken)
    {
        var target = targetEffectiveDate.Date;
        var legacyRows = await db.Forms.AsNoTracking()
            .Where(form => form.PersonId == personId &&
                           form.TargetEffectiveDate == target &&
                           form.CompletedDate != null &&
                           (form.Type == "Release_Agency" ||
                            form.Type == "Release_DHHS" ||
                            form.Type == "Release_Medical"))
            .ToListAsync(cancellationToken);
        var legacyIssues = LegacyReleaseCompletionReview.FindIssues(
            target,
            today,
            legacyRows.Select(form => new LegacyReleaseCompletionFact(
                form.Type,
                form.TargetEffectiveDate,
                form.CompletedDate!.Value)),
            rows.Select(item => item.ToComplianceFact()));
        return assignmentIssues.Concat(legacyIssues).ToArray();
    }

    private static async Task<ReleaseReconcileChanges> ReconcileReleaseRowsAsync(
        ApiDbContext db,
        ServerPerson person,
        int agencyId,
        DateTime targetEffectiveDate,
        ReleaseAssignmentResolutionResult resolution,
        List<ReleaseObligation> rows,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var reconciliation = ReleaseObligationRules.ReconcileCycle(
            targetEffectiveDate,
            resolution.Assignments,
            rows.Select(item => new ExistingReleaseObligation(
                item.StableKey, item.AssignmentKey, item.RetiredOn)));
        var providerSnapshots = await db.PersonProviders.AsNoTracking()
            .Where(link => link.PersonId == person.Id)
            .Join(db.Providers.AsNoTracking(), link => link.ProviderId, provider => provider.Id,
                (link, provider) => new
                {
                    link.Id,
                    link.ProviderId,
                    provider.Name,
                    provider.AgencyId
                })
            .Where(item => item.AgencyId == agencyId)
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var assignmentKeys = resolution.Assignments
            .Select(item => item.AssignmentKey)
            .ToHashSet(StringComparer.Ordinal);
        var created = new List<string>();
        foreach (var plan in reconciliation.ToCreate)
        {
            int? providerId = null;
            string? recipient = null;
            if (plan.AssignmentKey is not null)
            {
                if (!assignmentKeys.Contains(plan.AssignmentKey) ||
                    !ReleaseAssignmentResolution.TryGetProviderLinkId(
                        plan.AssignmentKey, out var linkId) ||
                    !providerSnapshots.TryGetValue(linkId, out var snapshot))
                    continue;
                providerId = snapshot.ProviderId;
                recipient = snapshot.Name;
            }

            var row = ReleaseObligation.Create(
                agencyId,
                person.Id,
                plan,
                DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc),
                providerId,
                recipient);
            db.ReleaseObligations.Add(row);
            rows.Add(row);
            created.Add(plan.StableKey);
        }

        var retired = new List<string>();
        foreach (var retirement in reconciliation.ToRetire)
        {
            var row = rows.Single(item => item.StableKey == retirement.StableKey);
            row.Retire(
                retirement.RetiredOn,
                DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc));
            retired.Add(row.StableKey);
        }

        return new ReleaseReconcileChanges(created, retired);
    }

    private static ReleaseObligationStatusDto ToReleaseStatus(
        ServerPerson person,
        DateTime targetEffectiveDate,
        DateTime today,
        IEnumerable<ReleaseObligation> rows,
        IReadOnlyList<ReleaseLinkageIssue> issues)
    {
        var signer = ReleaseSigningRules.For(person.HasGuardian);
        return new ReleaseObligationStatusDto(
            person.Id,
            targetEffectiveDate.Date,
            signer.RequiredCapacity.ToString(),
            signer.Label,
            rows.OrderBy(item => item.DueOn).ThenBy(item => item.StableKey)
                .Select(item => ToReleaseDto(item, today)).ToArray(),
            issues);
    }

    private static ReleaseObligationDto ToReleaseDto(ReleaseObligation row, DateTime today) =>
        new(
            row.Id,
            row.ObligationId,
            row.PersonId,
            row.StableKey,
            row.Category.ToString(),
            row.Trigger.ToString(),
            row.TargetEffectiveDate,
            row.RecipientProviderId,
            row.RecipientDisplayName,
            row.AvailableOn,
            row.DueOn,
            row.AppliesFromOn,
            row.RetiredOn,
            row.CompletedOn,
            row.WithdrawnOn,
            row.IsAuthorizationActive(today),
            row.Attestations.OrderBy(item => item.RecordedAtUtc)
                .Select(item => new ReleaseObligationAttestationDto(
                    item.Id,
                    item.CompletedOn,
                    item.Source.ToString(),
                    item.ActorKind.ToString(),
                    item.ActorUserId,
                    item.SignerCapacity?.ToString(),
                    item.SignatureCompletionId,
                    item.RecordedAtUtc,
                    item.Reason))
                .ToArray());

    private static string? ValidateReleaseTarget(
        DateTime? initialEffectiveDate,
        DateTime targetEffectiveDate)
    {
        if (initialEffectiveDate is not DateTime initial)
            return "Set the consumer's annual effective date before creating release obligations.";
        var target = targetEffectiveDate.Date;
        var years = target.Year - initial.Date.Year;
        return years >= 0 && initial.Date.AddYears(years) == target
            ? null
            : "The release target must be one of this consumer's annual effective dates.";
    }

    private static IResult ReleaseValidation(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });

    private static string ReleaseAuditMetadata(
        ReleaseObligation row,
        DateTime occurredOn,
        string? reason) =>
        JsonSerializer.Serialize(new
        {
            obligationId = row.ObligationId,
            stableKey = row.StableKey,
            category = row.Category.ToString(),
            targetEffectiveDate = row.TargetEffectiveDate.ToString("yyyy-MM-dd"),
            occurredOn = occurredOn.Date.ToString("yyyy-MM-dd"),
            explanationProvided = !string.IsNullOrWhiteSpace(reason)
        });

    private sealed record ReleaseReconcileChanges(
        IReadOnlyList<string> CreatedKeys,
        IReadOnlyList<string> RetiredKeys);
}
