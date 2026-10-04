using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sati.Api.Data;
using Sati.Api.Infrastructure;
using Sati.Api.Security;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models.Billing;

namespace Sati.Api.Endpoints;

internal static partial class ApiEndpoints
{
    private static void MapClearinghouseDispatch(RouteGroupBuilder api)
    {
        api.MapGet("/billing/clearinghouse", async Task<IResult> (
            ClaimsPrincipal principal, ApiDbContext db, ClearinghouseDispatchGate gate,
            CancellationToken token) =>
        {
            var actor = Actor.From(principal);
            if (!actor.HasBillingPermissions) return Results.Forbid();
            if (!gate.IsSyntheticEnvironment) return Results.NotFound();
            if (!gate.IsEnabled)
                return Results.Ok(new ClearinghouseWorkspaceDto(false,
                    "Server-managed test dispatch is not enabled for this deployment.", [], [], []));

            var accounts = await db.ClearinghouseAccounts.AsNoTracking()
                .Where(row => row.AgencyId == actor.AgencyId && row.IsEnabled && row.IsTest)
                .OrderBy(row => row.ConnectorKind).ToListAsync(token);
            accounts = accounts.Where(gate.CanUseAccount).ToList();
            var dispatches = await db.ClearinghouseDispatches.AsNoTracking()
                .Where(row => row.AgencyId == actor.AgencyId)
                .OrderByDescending(row => row.RequestedAtUtc).ThenByDescending(row => row.Id)
                .Take(50).ToListAsync(token);
            var byGeneration = dispatches.ToDictionary(row => row.EdiGenerationId);
            var generations = await (from generation in db.EdiGenerations.AsNoTracking()
                                     join period in db.BillingPeriods.AsNoTracking()
                                         on generation.BillingPeriodId equals period.Id
                                     join owner in db.Users.AsNoTracking() on period.UserId equals owner.Id
                                     where generation.AgencyId == actor.AgencyId && generation.IsTest &&
                                           owner.AgencyId == actor.AgencyId && period.Status == 1
                                     orderby generation.CreatedAtUtc descending, generation.Id descending
                                     select generation).Take(30).ToListAsync(token);
            var candidates = generations.Select(generation =>
            {
                var matching = accounts.Where(account =>
                    ClearinghouseAccountSelection.Matches(generation.Content, account, generation.BillingPeriodId))
                    .Take(2).ToList();
                return new ClearinghouseGenerationDto(generation.Id, generation.BillingPeriodId,
                    generation.FileName, generation.CreatedAtUtc, generation.IsCorrection,
                    matching.Count == 1 ? matching[0].Id : null,
                    byGeneration.GetValueOrDefault(generation.Id)?.State.ToString());
            }).ToList();
            return Results.Ok(new ClearinghouseWorkspaceDto(accounts.Count > 0,
                accounts.Count == 0
                    ? "No enabled test clearinghouse account is configured for this agency."
                    : gate.IsRealSandboxEnabled
                        ? "Claim.MD test transport is enabled; payer acceptance and payment require separate review."
                        : "Synthetic server dispatch is enabled. Queued files are sent only to the fake connector.",
                accounts.Select(row => new ClearinghouseAccountOptionDto(row.Id,
                    row.ConnectorKind.ToString(), row.ConnectorKind == TradingPartnerKind.ClaimMd
                        ? "Claim.MD test profile" : "Office Ally test profile")).ToList(),
                candidates, dispatches.Select(ToDispatchDto).ToList()));
        });

        api.MapPost("/billing/clearinghouse/dispatches", async Task<IResult> (
            QueueClearinghouseDispatchRequest request, ClaimsPrincipal principal, ApiDbContext db,
            ClearinghouseDispatchGate gate, AuditTrail audit, CancellationToken token) =>
        {
            var actor = Actor.From(principal);
            if (!actor.HasBillingPermissions) return Results.Forbid();
            if (!gate.IsEnabled) return Results.NotFound();
            if (request.EdiGenerationId <= 0 || request.AccountId == Guid.Empty)
                return Results.BadRequest(new ApiErrorDto("dispatch_invalid", "Choose a retained test file and account.", string.Empty));
            var periodKey = await (from file in db.EdiGenerations.AsNoTracking()
                                   join period in db.BillingPeriods.AsNoTracking()
                                       on file.BillingPeriodId equals period.Id
                                   join owner in db.Users.AsNoTracking() on period.UserId equals owner.Id
                                   where file.Id == request.EdiGenerationId && file.AgencyId == actor.AgencyId &&
                                         owner.AgencyId == actor.AgencyId
                                   select new { period.UserId, period.Year, period.Month })
                .SingleOrDefaultAsync(token);
            if (periodKey is null) return Results.NotFound();
            // Serialize all queue decisions for this billing period across API hosts. Without
            // this lock, two distinct generations can both pass the unresolved check.
            await using var periodWrite = await BillingPeriodWriteScope.BeginAsync(db,
                actor.AgencyId, periodKey.UserId, periodKey.Year, periodKey.Month, token);
            if (!await TenantAccess.IsCurrentActorAsync(db, actor, token)) return Results.Unauthorized();

            var generation = await (from row in db.EdiGenerations.AsNoTracking()
                                    join period in db.BillingPeriods.AsNoTracking()
                                        on row.BillingPeriodId equals period.Id
                                    join owner in db.Users.AsNoTracking() on period.UserId equals owner.Id
                                    where row.Id == request.EdiGenerationId && row.AgencyId == actor.AgencyId &&
                                          row.IsTest && period.Status == 1 && owner.AgencyId == actor.AgencyId
                                    select row).SingleOrDefaultAsync(token);
            if (generation is null) return Results.NotFound();
            await NoteAmendmentDispatchGuard.ValidateAsync(db, generation, token);
            var account = await db.ClearinghouseAccounts.AsNoTracking().SingleOrDefaultAsync(row =>
                row.Id == request.AccountId && row.AgencyId == actor.AgencyId && row.IsEnabled && row.IsTest, token);
            if (account is null || !gate.CanUseAccount(account)) return Results.NotFound();

            ClearinghouseDispatch? existing = await db.ClearinghouseDispatches.AsNoTracking()
                .SingleOrDefaultAsync(row => row.EdiGenerationId == generation.Id, token);
            if (existing is not null)
                return existing.AccountId == account.Id ? Results.Ok(ToDispatchDto(existing)) :
                    Results.Conflict(new ApiErrorDto("dispatch_account_conflict",
                        "That exact file is already queued for another account.", string.Empty));
            if (!ClearinghouseAccountSelection.Matches(generation.Content, account, generation.BillingPeriodId))
                return Results.Conflict(new ApiErrorDto("dispatch_profile_mismatch",
                    "The retained file does not match this test account and profile. Generate a new file for the selected account.", string.Empty));
            if (await db.BillingSubmissionEvents.AsNoTracking().AnyAsync(row =>
                    row.AgencyId == actor.AgencyId && row.EdiGenerationId == generation.Id &&
                    row.Stage >= BillingSubmissionStage.Transmitted, token))
                return Results.Conflict(new ApiErrorDto("dispatch_already_sent",
                    "This retained file already has transmission history and cannot be queued again.", string.Empty));
            var unresolved = await (from prior in db.ClearinghouseDispatches.AsNoTracking()
                                    join file in db.EdiGenerations.AsNoTracking()
                                        on prior.EdiGenerationId equals file.Id
                                    where prior.AgencyId == actor.AgencyId &&
                                          file.BillingPeriodId == generation.BillingPeriodId &&
                                          (prior.State == ClearinghouseDispatchState.Queued ||
                                           prior.State == ClearinghouseDispatchState.Sending ||
                                           prior.State == ClearinghouseDispatchState.OutcomeUnknown)
                                    select prior.Id).AnyAsync(token);
            if (unresolved)
                return Results.Conflict(new ApiErrorDto("dispatch_outcome_unresolved",
                    "An earlier file for this period is queued, sending, or has an unknown upload outcome. Reconcile it before queuing another.", string.Empty));

            var dispatch = new ClearinghouseDispatch
            {
                Id = Guid.NewGuid(), AgencyId = actor.AgencyId, AccountId = account.Id,
                EdiGenerationId = generation.Id, RequestingUserId = actor.UserId,
                RequestedAtUtc = DateTime.UtcNow, State = ClearinghouseDispatchState.Queued,
                TradingPartnerProfileVersion = account.TradingPartnerProfileVersion
            };
            db.ClearinghouseDispatches.Add(dispatch);
            audit.Record(actor, "billing-clearinghouse.dispatch-queued", "ClearinghouseDispatch",
                metadataJson: JsonSerializer.Serialize(new { dispatchId = dispatch.Id,
                    generationId = generation.Id, accountId = account.Id }));
            try
            {
                await db.SaveChangesAsync(token);
                await periodWrite.CommitAsync(token);
            }
            catch (DbUpdateException)
            {
                db.ChangeTracker.Clear();
                existing = await db.ClearinghouseDispatches.AsNoTracking()
                    .SingleOrDefaultAsync(row => row.EdiGenerationId == generation.Id, token);
                if (existing is not null && existing.AccountId == account.Id)
                    return Results.Ok(ToDispatchDto(existing));
                return Results.Conflict(new ApiErrorDto("dispatch_conflict",
                    "The file could not be queued safely. Refresh the dispatch list before trying again.", string.Empty));
            }
            return Results.Ok(ToDispatchDto(dispatch));
        });
    }

    private static ClearinghouseDispatchDto ToDispatchDto(ClearinghouseDispatch row) =>
        new(row.Id, row.EdiGenerationId, row.AccountId, row.State.ToString(), row.RequestedAtUtc,
            row.ExternalFileId, row.SafeErrorCode);
}
