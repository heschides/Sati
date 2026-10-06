using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Helpers;
using Sati.Models;

namespace Sati.Services.Billing;

public partial class BillingService
{
    public async Task<IReadOnlyList<PayerBillingVersionDto>> GetPayerConfigurationsAsync(AgencyActor suppliedActor)
    {
        await using var db = _contextFactory.CreateDbContext();
        var actor = PayerBillingRules.CanPublish(suppliedActor)
            ? await ValidateAdminActorAsync(db, suppliedActor, default) : await ValidateBillingActorAsync(db, suppliedActor);
        return await PayerBillingStore.ReadAsync(db, actor.AgencyId);
    }

    public async Task<PayerBillingVersionDto> PublishPayerConfigurationAsync(AgencyActor suppliedActor, PublishPayerBillingRequest request)
    {
        await using var db = _contextFactory.CreateDbContext();
        await using var transaction = await PayerBillingStore.BeginPublicationAsync(db, suppliedActor.AgencyId);
        var actor = await ValidateAdminActorAsync(db, suppliedActor, default);
        var errors = PayerBillingRules.Validate(request.Configuration);
        if (errors.Count > 0) throw new InvalidOperationException(string.Join("; ", errors.Select(e => $"{e.Field}: {e.Message}")));
        try
        {
            var result = await PayerBillingStore.AppendAsync(db, suppliedActor, request, DateTime.UtcNow,
                metadata => LocalAuditTrail.Record(db, actor, "billing-payer-configuration.published", "PayerBillingConfiguration", metadataJson: metadata));
            await transaction.CommitAsync();
            return result;
        }
        catch (DbUpdateException e) when (PayerBillingStore.IsWriteConflict(e)) { throw new PayerBillingConflictException("The payer profile changed. Refresh before publishing."); }
    }

    public async Task<PayerClaimPreviewDto> PreviewPayerClaimAsync(AgencyActor suppliedActor, int noteId, PayerClaimPreparation preparation)
    {
        await using var db = _contextFactory.CreateDbContext();
        var actor = await ValidateBillingActorAsync(db, suppliedActor);
        var note = await db.Notes.Include(n => n.Person).ThenInclude(p => p.Agency)
            .Include(n => n.Person).ThenInclude(p => p.Forms).ThenInclude(f => f.Attestations)
            .Include(n => n.Person).ThenInclude(p => p.ReleaseObligations).ThenInclude(r => r.Attestations)
            .SingleOrDefaultAsync(n => n.Id == noteId && n.AgencyId == actor.AgencyId && n.Person.AgencyId == actor.AgencyId &&
                db.Users.Any(owner => owner.Id == n.Person.UserId && owner.AgencyId == actor.AgencyId))
            ?? throw new InvalidOperationException("The billing note was not found in your agency.");
        db.Entry(note).State = EntityState.Detached;
        var errors = new List<PayerBillingFieldError>();
        try { await ApplyFinancialAmendmentAsync(db, actor.AgencyId, note); }
        catch (InvalidOperationException e) { errors.Add(new("Note.Amendment", e.Message)); }
        await BillingComplianceProjectionLoader.PopulateAsync(db, [note.Person], actor.AgencyId);
        var date = note.EventDate?.Date ?? default;
        var version = PayerBillingRules.Resolve(await PayerBillingStore.ReadAsync(db, actor.AgencyId), actor.AgencyId, preparation.ProfileKey, date);
        errors.AddRange(PayerBillingRules.ValidatePreparation(version, preparation, actor.AgencyId, note.PersonId, date));
        if (version is not null) errors.AddRange(PayerBillingRules.ValidateServiceQuantity(version.Configuration, note.Minutes));
        var policy = await BillingCompliancePolicyContextLoader.LoadAsync(db, actor.AgencyId);
        var recoveries = await LoadRecoveryDecisionsForNoteAsync(db, actor.AgencyId, note.PersonId, noteId);
        errors.AddRange(ValidateNoteForBilling(note, policy, recoveries, false).Errors.Select(e => new PayerBillingFieldError("Note", e)));
        if (await db.ClaimLines.AnyAsync(l => l.NoteId == noteId)) errors.Add(new("NoteId", "The note already has a claim line."));
        if (date != default)
        {
            if (await db.BillingPeriods.AnyAsync(p => p.UserId == note.Person.UserId && p.Year == date.Year && p.Month == date.Month && p.Status != BillingStatus.Draft))
                errors.Add(new("BillingPeriod", "The billing period is no longer a draft."));
            try { await NoteService.EnsureServiceTimeAvailableAsync(db, note.Person.UserId, note, note.Id); }
            catch (InvalidOperationException e) { errors.Add(new("Note.ServiceTime", e.Message)); }
        }
        var c = version?.Configuration;
        var units = BillingRules.CalculateSection13Units(note.Minutes);
        if (errors.Count == 0 && version is not null)
        {
            var snapshot = PayerBillingRules.Freeze(CreateClaimSnapshot(note.Person, note.Person.Agency!), version, preparation, actor.Id, DateTime.UtcNow, date);
            var candidate = new ProfessionalClaimLineFacts(0, date, c!.ProcedureCode, c.Modifiers.FirstOrDefault(), units,
                BillingRules.CalculateCharge(units, c.UnitRate), note.Person.MaineCareId!, c.RenderingProviderNpi,
                note.Person.DiagnosisCode!, (int)note.Person.PlaceOfService!.Value, ProfessionalClaimSnapshotCodec.Serialize(snapshot));
            var lines = await db.ClaimLines.AsNoTracking().Where(l => l.BillingPeriod.UserId == note.Person.UserId &&
                l.BillingPeriod.Year == date.Year && l.BillingPeriod.Month == date.Month).ToListAsync();
            var readiness = ProfessionalClaimReadiness.EvaluatePeriod(date.Year, date.Month, lines.Select(ToReadinessFacts).Append(candidate));
            errors.AddRange(readiness.Lines.SelectMany(l => l.Errors).Concat(readiness.PeriodErrors).Distinct().Select(e => new PayerBillingFieldError("ClaimPeriod", e)));
        }
        LocalAuditTrail.Record(db, actor, "billing-payer-claim.previewed", "Note", noteId);
        await db.SaveChangesAsync();
        return new(noteId, version?.VersionId, errors, c?.ProcedureCode ?? "", c?.Modifiers ?? [], units,
            BillingRules.CalculateCharge(units, c?.UnitRate ?? 0), c?.FacilityId ?? "");
    }
}
