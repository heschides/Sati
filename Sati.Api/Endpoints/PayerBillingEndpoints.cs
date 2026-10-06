using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Sati.Api.Data;
using Sati.Api.Infrastructure;
using Sati.Api.Security;
using Sati.Contracts.V1;
using Sati.Data;

namespace Sati.Api.Endpoints;

internal static partial class ApiEndpoints
{
    private static IResult PayerValidationProblem(IReadOnlyList<PayerBillingFieldError> errors) =>
        Results.ValidationProblem(errors.GroupBy(e => e.Field).ToDictionary(g => g.Key, g => g.Select(e => e.Message).ToArray()));

    private static void MapPayerBilling(RouteGroupBuilder api)
    {
        api.MapGet("/billing/payer-configurations", async (ClaimsPrincipal principal, ApiDbContext db, CancellationToken ct) =>
        {
            var actor = Actor.From(principal);
            if (!PayerBillingRules.CanRead(actor.ToAgencyActor())) return Results.Forbid();
            return Results.Ok(await PayerBillingStore.ReadAsync(db, actor.AgencyId, ct));
        });
        api.MapPost("/billing/payer-configurations", async Task<IResult> (PublishPayerBillingRequest request,
            ClaimsPrincipal principal, ApiDbContext db, AuditTrail audit, CancellationToken ct) =>
        {
            var actor = Actor.From(principal);
            if (!PayerBillingRules.CanPublish(actor.ToAgencyActor())) return Results.Forbid();
            var errors = PayerBillingRules.Validate(request.Configuration);
            if (request.ChangeId == Guid.Empty || request.ExpectedRevision < 0)
                errors = errors.Append(new PayerBillingFieldError("ChangeId", "A unique change identity and nonnegative expected revision are required.")).ToList();
            if (errors.Count > 0) return PayerValidationProblem(errors);
            try
            {
                await using var tx = await PayerBillingStore.BeginPublicationAsync(db, actor.AgencyId, ct);
                if (!await TenantAccess.IsCurrentActorAsync(db, actor, ct)) return Results.Forbid();
                var result = await PayerBillingStore.AppendAsync(db, actor.ToAgencyActor(), request, DateTime.UtcNow,
                    metadata => audit.Record(actor, "billing-payer-configuration.published", "PayerBillingConfiguration", metadataJson: metadata), ct);
                await tx.CommitAsync(ct);
                return Results.Ok(result);
            }
            catch (PayerBillingConflictException e) { return Results.Conflict(new ApiErrorDto(PayerBillingRules.RevisionCode, e.Message, "")); }
            catch (DbUpdateException e) when (PayerBillingStore.IsWriteConflict(e)) { return Results.Conflict(new ApiErrorDto(PayerBillingRules.RevisionCode, "The profile changed. Refresh before publishing.", "")); }
        });
        api.MapPost("/billing/payer-claims/{noteId:int}/preview", async Task<IResult> (int noteId,
            PayerClaimPreparation preparation, ClaimsPrincipal principal, ApiDbContext db, AuditTrail audit, CancellationToken ct) =>
        {
            var actor = Actor.From(principal);
            if (!PayerBillingRules.CanPrepare(actor.ToAgencyActor())) return Results.Forbid();
            var row = await (from note in db.Notes join person in db.People on note.PersonId equals person.Id
                join owner in db.Users on person.UserId equals owner.Id
                where note.Id == noteId && note.AgencyId == actor.AgencyId && person.AgencyId == actor.AgencyId && owner.AgencyId == actor.AgencyId
                select new ReviewableNote(note, person)).SingleOrDefaultAsync(ct);
            if (row is null) return Results.NotFound();
            db.Entry(row.Note).State = EntityState.Detached;
            var errors = new List<PayerBillingFieldError>();
            try { await ApplyFinancialAmendmentAsync(db, actor.AgencyId, row.Note, ct); }
            catch (InvalidOperationException e) { errors.Add(new("Note.Amendment", e.Message)); }
            var date = row.Note.EventDate?.Date ?? default;
            var version = PayerBillingRules.Resolve(await PayerBillingStore.ReadAsync(db, actor.AgencyId, ct), actor.AgencyId, preparation.ProfileKey, date);
            errors.AddRange(PayerBillingRules.ValidatePreparation(version, preparation, actor.AgencyId, row.Person.Id, date));
            if (version is not null) errors.AddRange(PayerBillingRules.ValidateServiceQuantity(version.Configuration, row.Note.Minutes));
            var agency = await db.Agencies.AsNoTracking().SingleAsync(a => a.Id == actor.AgencyId, ct);
            var forms = await db.Forms.AsNoTracking().Include(f => f.Attestations).Where(f => f.PersonId == row.Person.Id).ToListAsync(ct);
            var releases = (await LoadReleaseBillingRowsByPersonAsync(db, [row.Person.Id], ct)).GetValueOrDefault(row.Person.Id) ?? [];
            var links = (await LoadReleaseProviderLinksByPersonAsync(db, actor.AgencyId, [row.Person.Id], ct)).GetValueOrDefault(row.Person.Id) ?? [];
            var policy = await LoadBillingCompliancePolicyContextAsync(db, actor.AgencyId, ct);
            var recoveries = (await LoadRecoveryDecisionsByNoteAsync(db, actor.AgencyId, [noteId], ct)).GetValueOrDefault(noteId) ?? [];
            await PopulateContactHistoryAsync(db, actor.AgencyId, [row.Person], ct);
            errors.AddRange(ValidateBillingCandidate(row.Note, row.Person, agency, forms, releases, policy, recoveries, links, false)
                .Select(e => new PayerBillingFieldError("Note", e)));
            if (await db.ClaimLines.AnyAsync(l => l.NoteId == noteId, ct)) errors.Add(new("NoteId", "The note already has a claim line."));
            if (date != default && await db.BillingPeriods.AnyAsync(p => p.UserId == row.Person.UserId && p.Year == date.Year && p.Month == date.Month && p.Status != 0, ct))
                errors.Add(new("BillingPeriod", "The billing period is no longer a draft."));
            var timeProblem = await FindReviewServiceTimeProblemAsync(db, row, actor.AgencyId, ct);
            if (timeProblem is not null) errors.Add(new("Note.ServiceTime", "The note conflicts with another service-time reservation."));
            var c = version?.Configuration;
            var units = BillingRules.CalculateSection13Units(row.Note.Minutes);
            if (errors.Count == 0 && version is not null)
            {
                var snapshot = PayerBillingRules.Freeze(CreateClaimSnapshot(row.Person, agency), version, preparation, actor.UserId, DateTime.UtcNow, date);
                var candidate = new ProfessionalClaimLineFacts(0, date, c!.ProcedureCode, c.Modifiers.FirstOrDefault(), units,
                    BillingRules.CalculateCharge(units, c.UnitRate), row.Person.MaineCareId!, c.RenderingProviderNpi,
                    row.Person.DiagnosisCode!, row.Person.PlaceOfService!.Value, ProfessionalClaimSnapshotCodec.Serialize(snapshot));
                var lines = await db.ClaimLines.AsNoTracking().Where(l => db.BillingPeriods.Any(p => p.Id == l.BillingPeriodId &&
                    p.UserId == row.Person.UserId && p.Year == date.Year && p.Month == date.Month)).ToListAsync(ct);
                var readiness = ProfessionalClaimReadiness.EvaluatePeriod(date.Year, date.Month, lines.Select(ContractMapper.ToReadinessFacts).Append(candidate));
                errors.AddRange(readiness.Lines.SelectMany(l => l.Errors).Concat(readiness.PeriodErrors).Distinct().Select(e => new PayerBillingFieldError("ClaimPeriod", e)));
            }
            audit.Record(actor, "billing-payer-claim.previewed", "Note", noteId);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new PayerClaimPreviewDto(noteId, version?.VersionId, errors, c?.ProcedureCode ?? "", c?.Modifiers ?? [],
                units, BillingRules.CalculateCharge(units, c?.UnitRate ?? 0), c?.FacilityId ?? ""));
        });
    }
}
