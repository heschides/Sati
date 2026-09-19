using System.Globalization;
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

/// <summary>
/// Recording the bank deposit behind a remittance, and correcting claims after the payer has
/// answered. Both write only append-only rows; nothing that was sent or received is changed.
/// </summary>
internal static partial class ApiEndpoints
{
    private static void MapBillingCorrections(RouteGroupBuilder api)
    {
        api.MapGet("/billing/remittance-deposits/{depositId:long}/eft", async Task<IResult> (
            long depositId, ClaimsPrincipal principal, ApiDbContext db, CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!actor.HasBillingPermissions)
                return Results.Forbid();
            if (!await db.RemittanceDeposits.AsNoTracking().AnyAsync(
                    item => item.Id == depositId && item.AgencyId == actor.AgencyId, cancellationToken))
                return Results.NotFound();

            var rows = await (from record in db.EftDepositRecords.AsNoTracking()
                              join user in db.Users.AsNoTracking() on record.RecordedByUserId equals user.Id
                              where record.RemittanceDepositId == depositId && record.AgencyId == actor.AgencyId
                              orderby record.Id descending
                              select new EftDepositRecordDto(
                                  record.Id, record.RemittanceDepositId, record.Amount, record.DepositDate,
                                  record.BankTraceNumber, record.Note, record.SupersedesRecordId,
                                  user.DisplayName, record.RecordedAtUtc)).ToListAsync(cancellationToken);
            return Results.Ok(rows);
        });

        api.MapPost("/billing/remittance-deposits/{depositId:long}/eft", async Task<IResult> (
            long depositId, RecordEftDepositRequest request, ClaimsPrincipal principal, ApiDbContext db,
            AuditTrail auditTrail, ApiClock clock, CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!actor.HasBillingPermissions)
                return Results.Forbid();
            if (!await TenantAccess.IsCurrentActorAsync(db, actor, cancellationToken))
                return Results.Unauthorized();

            var deposit = await db.RemittanceDeposits.AsNoTracking().SingleOrDefaultAsync(
                item => item.Id == depositId && item.AgencyId == actor.AgencyId, cancellationToken);
            if (deposit is null)
                return Results.NotFound();

            var latest = await db.EftDepositRecords.AsNoTracking()
                .Where(record => record.RemittanceDepositId == depositId)
                .OrderByDescending(record => record.Id)
                .FirstOrDefaultAsync(cancellationToken);
            // The caller names the entry it saw. Anything else means someone recorded or corrected
            // the deposit in between, and silently stacking a second figure on theirs would hide it.
            if (latest?.Id != request.PreviousRecordId)
                return EftChangedConflict();

            var errors = EftDepositRules.Validate(request.Amount, request.DepositDate, clock.Today,
                request.BankTraceNumber, request.Note, isCorrection: latest is not null);
            if (errors.Count > 0)
                return Results.ValidationProblem(errors.ToDictionary(item => item.Key, item => item.Value));

            var record = new EftDepositRecord
            {
                AgencyId = actor.AgencyId,
                RemittanceDepositId = depositId,
                Amount = request.Amount,
                DepositDate = request.DepositDate.Date,
                BankTraceNumber = NullIfBlank(request.BankTraceNumber),
                Note = NullIfBlank(request.Note),
                SupersedesRecordId = latest?.Id,
                RecordedByUserId = actor.UserId,
                RecordedAtUtc = DateTime.UtcNow,
                IsSynthetic = deposit.IsSynthetic
            };
            db.EftDepositRecords.Add(record);
            // Amounts are financial facts about the agency, not about a person, so they are
            // safe to keep in the audit metadata; the free-text note is not copied there.
            auditTrail.Record(actor, AuditActions.BillingEftDepositRecorded, "RemittanceDeposit",
                metadataJson: JsonSerializer.Serialize(new
                {
                    depositId, amount = record.Amount, supersedes = record.SupersedesRecordId
                }));
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // The filtered unique index on SupersedesRecordId: someone else corrected the
                // same entry first.
                return EftChangedConflict();
            }

            var count = await db.EftDepositRecords.AsNoTracking()
                .CountAsync(item => item.RemittanceDepositId == depositId, cancellationToken);
            return Results.Ok(ToDepositDto(deposit, record, count));
        });

        api.MapGet("/billing/periods/{periodId:int}/claims", async Task<IResult> (
            int periodId, ClaimsPrincipal principal, ApiDbContext db, CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!actor.HasBillingPermissions)
                return Results.Forbid();
            var period = await LoadAgencyPeriodAsync(db, actor, periodId, cancellationToken);
            if (period is null)
                return Results.NotFound();

            var history = await LoadClaimHistoryAsync(db, actor.AgencyId, period, cancellationToken);
            var names = await (from note in db.Notes.AsNoTracking()
                               join person in db.People.AsNoTracking() on note.PersonId equals person.Id
                               where history.NoteIds.Contains(note.Id) && person.AgencyId == actor.AgencyId
                               select new { note.Id, person.FirstName, person.LastName })
                .ToDictionaryAsync(row => row.Id, row => $"{row.FirstName} {row.LastName}".Trim(), cancellationToken);

            return Results.Ok(period.Lines.OrderBy(line => line.DateOfService).ThenBy(line => line.Id).Select(line =>
            {
                var options = history.Options(line.NoteId);
                return new BillingClaimStatusDto(
                    line.Id, line.NoteId, names.GetValueOrDefault(line.NoteId) ?? $"Note {line.NoteId}",
                    line.DateOfService, line.ChargeAmount, options.State.ToString(),
                    ClaimCorrectionRules.Describe(options.State), options.Explanation,
                    options.AllowedActions.Select(action => action.ToString()).ToList(),
                    options.PayerClaimControlNumber, history.SubmissionsFor(line.NoteId).Count);
            }).ToList());
        });

        api.MapPost("/billing/periods/{periodId:int}/corrections", async Task<IResult> (
            int periodId, CreateClaimCorrectionRequest request, ClaimsPrincipal principal, ApiDbContext db,
            AuditTrail auditTrail, CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!actor.HasBillingPermissions)
                return Results.Forbid();
            if (!Enum.IsDefined(request.Action))
                return Results.ValidationProblem(new Dictionary<string, string[]>
                    { ["action"] = ["Choose resend, replace, or void."] });
            if (ClaimCorrectionRules.ValidateReason(request.Reason) is string reasonProblem)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["reason"] = [reasonProblem] });

            var periodKey = await LoadAgencyPeriodAsync(db, actor, periodId, cancellationToken);
            if (periodKey is null)
                return Results.NotFound();
            await using var transaction = await BillingPeriodWriteScope.BeginAsync(
                db, actor.AgencyId, periodKey.UserId, periodKey.Year, periodKey.Month, cancellationToken);
            if (!await TenantAccess.IsCurrentActorAsync(db, actor, cancellationToken))
                return Results.Unauthorized();
            var period = await LoadAgencyPeriodAsync(db, actor, periodId, cancellationToken);
            if (period is null)
                return Results.NotFound();
            if (period.Status != 1)
                return Results.Conflict(new ApiErrorDto("billing_period_not_submitted",
                    "Only a submitted billing period's claims can be corrected.", string.Empty));
            var line = period.Lines.SingleOrDefault(item => item.Id == request.ClaimLineId);
            if (line is null)
                return Results.NotFound();

            var history = await LoadClaimHistoryAsync(db, actor.AgencyId, period, cancellationToken);
            var options = history.Options(line.NoteId);
            if (!options.AllowedActions.Contains(request.Action))
                return Results.Conflict(new ApiErrorDto("correction_not_allowed",
                    $"{ClaimCorrectionRules.Describe(request.Action)} is not available for this claim. {options.Explanation}",
                    string.Empty));

            var correction = new ClaimCorrection
            {
                AgencyId = actor.AgencyId,
                BillingPeriodId = period.Id,
                ClaimLineId = line.Id,
                NoteId = line.NoteId,
                Action = request.Action,
                PayerClaimControlNumber = request.Action == ClaimCorrectionAction.Resubmit
                    ? null : options.PayerClaimControlNumber,
                Reason = request.Reason.Trim(),
                CorrectsEdiGenerationId = history.SubmissionsFor(line.NoteId).LastOrDefault()?.GenerationId,
                RequestedByUserId = actor.UserId,
                RequestedAtUtc = DateTime.UtcNow
            };

            if (request.Action == ClaimCorrectionAction.Void)
            {
                // A void repeats the claim the payer holds, exactly as it was billed.
                var standing = history.StandingSubmission(line.NoteId);
                var source = standing?.CorrectionId is long correctionId
                    ? history.Corrections.Single(item => item.Id == correctionId)
                    : null;
                correction.ClaimSnapshotJson = source?.ClaimSnapshotJson ?? line.ClaimSnapshotJson ?? string.Empty;
                correction.ClientMaineCareId = source?.ClientMaineCareId ?? line.ClientMaineCareId;
                correction.RenderingProviderNpi = source?.RenderingProviderNpi ?? line.RenderingProviderNpi;
                correction.DiagnosisCode = source?.DiagnosisCode ?? line.DiagnosisCode;
                correction.PlaceOfService = source?.PlaceOfService ?? line.PlaceOfService;
            }
            else
            {
                // A resend or replacement is billed from the client's and agency's records as they
                // stand now: fixing those records is usually the point of the correction. It must
                // pass the same checks a new claim line would.
                var row = await (from note in db.Notes
                                 join person in db.People on note.PersonId equals person.Id
                                 join owner in db.Users on person.UserId equals owner.Id
                                 where note.Id == line.NoteId && note.Status == 6 &&
                                       owner.AgencyId == actor.AgencyId &&
                                       person.AgencyId == actor.AgencyId && note.AgencyId == actor.AgencyId
                                 select new ReviewableNote(note, person)).SingleOrDefaultAsync(cancellationToken);
                if (row is null)
                    return Results.Conflict(new ApiErrorDto("invalid_billing_source",
                        "The claim's service note is no longer an approved note in this agency.", string.Empty));
                var agency = await db.Agencies.AsNoTracking()
                    .SingleOrDefaultAsync(candidate => candidate.Id == actor.AgencyId, cancellationToken);
                var forms = await db.Forms.AsNoTracking().Include(form => form.Attestations)
                    .Where(form => form.PersonId == row.Person.Id).ToListAsync(cancellationToken);
                var releaseRows = (await LoadReleaseBillingRowsByPersonAsync(db, [row.Person.Id], cancellationToken))
                    .GetValueOrDefault(row.Person.Id) ?? [];
                var providerLinks = (await LoadReleaseProviderLinksByPersonAsync(
                    db, actor.AgencyId, [row.Person.Id], cancellationToken)).GetValueOrDefault(row.Person.Id) ?? [];
                var compliancePolicy = await LoadBillingCompliancePolicyContextAsync(db, actor.AgencyId, cancellationToken);
                var recoveryByNote = await LoadRecoveryDecisionsByNoteAsync(
                    db, actor.AgencyId, [row.Note.Id], cancellationToken);
                await PopulateContactHistoryAsync(db, actor.AgencyId, [row.Person], cancellationToken);
                var errors = ValidateBillingCandidate(row.Note, row.Person, agency, forms, releaseRows,
                    compliancePolicy, recoveryByNote.GetValueOrDefault(row.Note.Id) ?? [], providerLinks);
                if (errors.Count > 0)
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["note"] = errors.ToArray() });

                correction.ClaimSnapshotJson = ProfessionalClaimSnapshotCodec.Serialize(CreateClaimSnapshot(row.Person, agency!));
                correction.ClientMaineCareId = row.Person.MaineCareId!;
                correction.RenderingProviderNpi = agency!.Npi!;
                correction.DiagnosisCode = row.Person.DiagnosisCode!;
                correction.PlaceOfService = row.Person.PlaceOfService!.Value;
            }

            var readiness = ProfessionalClaimReadiness.EvaluatePeriod(
                period.Year, period.Month, [ContractMapper.ToReadinessFacts(CorrectedLine(line, correction))]);
            if (!readiness.IsReady)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                    { ["claim"] = [readiness.ExplainFailure()] });

            db.ClaimCorrections.Add(correction);
            auditTrail.Record(actor, AuditActions.BillingClaimCorrectionCreated, "BillingPeriod", period.Id,
                JsonSerializer.Serialize(new { claimLineId = line.Id, action = request.Action.ToString() }));
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Results.Ok(new ClaimCorrectionDto(correction.Id, line.Id, correction.Action.ToString(),
                correction.PayerClaimControlNumber, correction.Reason, correction.RequestedAtUtc));
        });

        api.MapPost("/billing/periods/{periodId:int}/corrections/edi", async Task<IResult> (
            int periodId, GenerateEdiRequest request, ClaimsPrincipal principal, ApiDbContext db,
            AuditTrail auditTrail, CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!actor.HasBillingPermissions)
                return Results.Forbid();
            if (!Guid.TryParse(request.IdempotencyKey, out var parsedKey))
                return Results.ValidationProblem(new Dictionary<string, string[]>
                    { ["idempotencyKey"] = ["A valid idempotency key is required."] });
            var normalizedKey = parsedKey.ToString("N");
            var periodKey = await LoadAgencyPeriodAsync(db, actor, periodId, cancellationToken);
            if (periodKey is null)
                return Results.NotFound();
            await using var transaction = await BillingPeriodWriteScope.BeginAsync(
                db, actor.AgencyId, periodKey.UserId, periodKey.Year, periodKey.Month, cancellationToken);
            if (!await TenantAccess.IsCurrentActorAsync(db, actor, cancellationToken))
                return Results.Unauthorized();
            var previous = await db.EdiGenerations.AsNoTracking().SingleOrDefaultAsync(generation =>
                generation.AgencyId == actor.AgencyId && generation.ActorUserId == actor.UserId &&
                generation.IdempotencyKey == normalizedKey, cancellationToken);
            if (previous is not null)
                return previous.IsCorrection
                    ? ReplayEdiOrConflict(previous, periodId, request.IsTest)
                    : Results.Conflict(new ApiErrorDto("idempotency_key_reused",
                        "This retry key was already used for a different EDI request.", string.Empty));

            var period = await LoadAgencyPeriodAsync(db, actor, periodId, cancellationToken);
            if (period is null)
                return Results.NotFound();
            if (period.Status != 1)
                return Results.Conflict(new ApiErrorDto("billing_period_not_submitted",
                    "Only a submitted billing period's corrections can be sent.", string.Empty));

            var corrections = await db.ClaimCorrections.AsNoTracking()
                .Where(item => item.AgencyId == actor.AgencyId && item.BillingPeriodId == periodId)
                .OrderBy(item => item.Id).ToListAsync(cancellationToken);
            var correctionIds = corrections.Select(item => item.Id).ToList();
            var sentInThisMode = await (from submission in db.ClaimCorrectionSubmissions.AsNoTracking()
                                        join sent in db.EdiGenerations.AsNoTracking()
                                            on submission.EdiGenerationId equals sent.Id
                                        where correctionIds.Contains(submission.ClaimCorrectionId) &&
                                              sent.IsTest == request.IsTest
                                        select submission.ClaimCorrectionId).ToListAsync(cancellationToken);
            var waiting = corrections.Where(item => !sentInThisMode.Contains(item.Id)).ToList();
            if (waiting.Count == 0)
                return Results.Conflict(new ApiErrorDto("no_corrections_waiting",
                    "There are no corrections waiting to be sent for this billing period.", string.Empty));

            var generatedAt = DateTime.Now;
            var controlNumber = CreateEdiControlNumber(normalizedKey);
            if (await db.EdiGenerations.AnyAsync(item => item.AgencyId == actor.AgencyId &&
                    item.IsTest == request.IsTest && item.ControlNumber == controlNumber, cancellationToken))
                return Results.Conflict(new ApiErrorDto("edi_control_conflict",
                    "This submission identity has already been used. Start a new generation attempt.", string.Empty));

            string content;
            try
            {
                content = ServerEdiGenerator.GenerateCorrections(period,
                    waiting.Select(item => new EdiClaim(
                        CorrectedLine(period.Lines.Single(line => line.Id == item.ClaimLineId), item),
                        ClaimCorrectionRules.FrequencyCode(item.Action),
                        item.PayerClaimControlNumber)).ToList(),
                    request.IsTest, generatedAt, controlNumber);
            }
            catch (InvalidOperationException failure)
            {
                return Results.Conflict(new ApiErrorDto("billing_export_blocked", failure.Message, string.Empty));
            }

            var timestamp = generatedAt.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            var testMarker = request.IsTest ? ".OATEST" : string.Empty;
            var file = new EdiFileDto(
                $"837P{testMarker}_CORRECTION_{period.Year}{period.Month:D2}_{timestamp}_{normalizedKey[..8]}.txt", content);
            var generation = new ServerEdiGeneration
            {
                AgencyId = actor.AgencyId,
                ActorUserId = actor.UserId,
                BillingPeriodId = periodId,
                IdempotencyKey = normalizedKey,
                IsTest = request.IsTest,
                ControlNumber = controlNumber,
                FileName = file.FileName,
                Content = file.Content,
                CreatedAtUtc = DateTime.UtcNow,
                IsCorrection = true
            };
            db.EdiGenerations.Add(generation);
            await db.SaveChangesAsync(cancellationToken);
            foreach (var item in waiting)
                db.ClaimCorrectionSubmissions.Add(new ClaimCorrectionSubmission
                {
                    ClaimCorrectionId = item.Id,
                    EdiGenerationId = generation.Id
                });
            db.BillingSubmissionEvents.Add(new ServerBillingSubmissionEvent
            {
                AgencyId = actor.AgencyId,
                BillingPeriodId = periodId,
                EdiGeneration = generation,
                OccurredAtUtc = DateTime.UtcNow,
                Stage = BillingSubmissionStage.Generated,
                Reference = file.FileName,
                Explanation = $"Correction 837P generated for {waiting.Count} claim(s)" +
                    (request.IsTest ? "; test file, no external transmission is implied." : "; transmission status has not been recorded."),
                IsSynthetic = request.IsTest
            });
            auditTrail.Record(actor, AuditActions.BillingCorrectionEdiGenerated, "BillingPeriod", periodId,
                JsonSerializer.Serialize(new { claims = waiting.Count, isTest = request.IsTest }));
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Results.Ok(file);
        });
    }

    /// <summary>
    /// A paid batch is reconciled once every remittance that paid it has a bank deposit
    /// matching it to the penny. Derived on each read rather than stored as an event: the
    /// submission trail is append-only and ranks Reconciled above everything, so a stored
    /// Reconciled could never be withdrawn when a mistyped deposit is later corrected.
    /// </summary>
    private static async Task<IReadOnlyList<BillingSubmissionHistoryDto>> DeriveReconciledRowsAsync(
        ApiDbContext db, int agencyId, IReadOnlyList<BillingSubmissionHistoryDto> rows, CancellationToken cancellationToken)
    {
        var paid = rows.Where(row => row.EdiGenerationId.HasValue)
            .GroupBy(row => row.EdiGenerationId!.Value)
            .Select(group => BillingSubmissionProgressRules.Current(group))
            .Where(current => current?.Stage == nameof(BillingSubmissionStage.Paid))
            .Select(current => current!)
            .ToList();
        if (paid.Count == 0)
            return [];

        var generationIds = paid.Select(row => row.EdiGenerationId!.Value).ToList();
        var responses = await db.RemittanceClaimOutcomes.AsNoTracking()
            .Where(item => item.AgencyId == agencyId && item.EdiGenerationId.HasValue &&
                           generationIds.Contains(item.EdiGenerationId.Value) && item.ResponseId.HasValue)
            .Select(item => new { GenerationId = item.EdiGenerationId!.Value, ResponseId = item.ResponseId!.Value })
            .Distinct().ToListAsync(cancellationToken);
        var responseIds = responses.Select(item => item.ResponseId).Distinct().ToList();
        var deposits = await db.RemittanceDeposits.AsNoTracking()
            .Where(item => item.AgencyId == agencyId && item.ResponseId.HasValue && responseIds.Contains(item.ResponseId.Value))
            .ToListAsync(cancellationToken);
        var depositIds = deposits.Select(item => item.Id).ToList();
        var records = await db.EftDepositRecords.AsNoTracking()
            .Where(item => item.AgencyId == agencyId && depositIds.Contains(item.RemittanceDepositId))
            .ToListAsync(cancellationToken);

        var derived = new List<BillingSubmissionHistoryDto>();
        foreach (var row in paid)
        {
            var generationId = row.EdiGenerationId!.Value;
            var itsDeposits = deposits.Where(deposit => responses.Any(response =>
                response.GenerationId == generationId && response.ResponseId == deposit.ResponseId)).ToList();
            if (itsDeposits.Count == 0)
                continue;
            var latest = itsDeposits.Select(deposit => records
                .Where(record => record.RemittanceDepositId == deposit.Id).MaxBy(record => record.Id)).ToList();
            var matched = itsDeposits.Zip(latest).All(pair =>
                DepositReconciliationRules.GetStatus(pair.First.ClaimPaymentAmount, pair.First.ProviderLevelAdjustmentAmount,
                    pair.First.RemittancePaymentAmount, pair.Second?.Amount ?? pair.First.EftDepositAmount)
                == DepositReconciliationStatus.Matched);
            if (!matched)
                continue;
            var reconciledAt = latest.Where(record => record is not null).Select(record => record!.RecordedAtUtc)
                .DefaultIfEmpty(row.OccurredAtUtc).Max();
            derived.Add(row with
            {
                Id = -generationId,
                OccurredAtUtc = reconciledAt,
                Stage = nameof(BillingSubmissionStage.Reconciled),
                Reference = null,
                ResponseType = "EFT",
                ResponseCode = null,
                Explanation = "The bank deposit matches the payment report to the penny."
            });
        }
        return derived;
    }

    private static IResult EftChangedConflict() =>
        Results.Conflict(new ApiErrorDto("eft_changed",
            "Someone recorded or corrected this deposit since you opened it. Refresh and check the latest entry before changing it.",
            string.Empty));

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static Task<ServerBillingPeriod?> LoadAgencyPeriodAsync(
        ApiDbContext db, Actor actor, int periodId, CancellationToken cancellationToken) =>
        (from candidate in db.BillingPeriods.AsNoTracking().Include(value => value.Lines)
         join owner in db.Users.AsNoTracking() on candidate.UserId equals owner.Id
         where candidate.Id == periodId && owner.AgencyId == actor.AgencyId
         select candidate).SingleOrDefaultAsync(cancellationToken);

    /// <summary>The original line as a correction bills it: same service, corrected identity.</summary>
    private static ServerClaimLine CorrectedLine(ServerClaimLine line, ClaimCorrection correction) => new()
    {
        Id = line.Id,
        NoteId = line.NoteId,
        BillingPeriodId = line.BillingPeriodId,
        DateOfService = line.DateOfService,
        ProcedureCode = line.ProcedureCode,
        ProcedureModifier = line.ProcedureModifier,
        Units = line.Units,
        ChargeAmount = line.ChargeAmount,
        ClientMaineCareId = correction.ClientMaineCareId,
        RenderingProviderNpi = correction.RenderingProviderNpi,
        DiagnosisCode = correction.DiagnosisCode,
        PlaceOfService = correction.PlaceOfService,
        ClaimSnapshotJson = correction.ClaimSnapshotJson,
        IsComplianceException = line.IsComplianceException,
        ComplianceExceptionReason = line.ComplianceExceptionReason
    };

    /// <summary>The current EFT entry wins over the legacy column, which only seeds ever wrote.</summary>
    internal static RemittanceDepositDto ToDepositDto(ServerRemittanceDeposit item, EftDepositRecord? latest, int recordCount)
    {
        var eft = latest?.Amount ?? item.EftDepositAmount;
        var status = DepositReconciliationRules.GetStatus(
            item.ClaimPaymentAmount, item.ProviderLevelAdjustmentAmount, item.RemittancePaymentAmount, eft);
        return new RemittanceDepositDto(
            item.Id, item.PaymentReference, item.PayerName, item.ReceivedAtUtc,
            item.PaymentDate, item.ClaimPaymentAmount, item.ProviderLevelAdjustmentAmount,
            item.ProviderLevelAdjustmentSummary, item.RemittancePaymentAmount,
            eft, status.ToString(), eft - item.RemittancePaymentAmount,
            DepositReconciliationRules.Explain(status), item.IsSynthetic)
        {
            EftDepositDate = latest?.DepositDate,
            CurrentEftRecordId = latest?.Id,
            EftRecordCount = recordCount
        };
    }

    /// <summary>
    /// Every time each claim of a period was sent, and what came back, assembled from the
    /// retained files and the recorded answers. Nothing here is stored as a status that could
    /// drift from the evidence it summarizes.
    /// </summary>
    private static async Task<ClaimHistory> LoadClaimHistoryAsync(
        ApiDbContext db, int agencyId, ServerBillingPeriod period, CancellationToken cancellationToken)
    {
        var generations = await db.EdiGenerations.AsNoTracking()
            .Where(item => item.AgencyId == agencyId && item.BillingPeriodId == period.Id)
            .OrderBy(item => item.Id).ToListAsync(cancellationToken);
        var generationIds = generations.Select(item => item.Id).ToList();
        var fileVerdicts = await db.BillingSubmissionEvents.AsNoTracking()
            .Where(item => item.AgencyId == agencyId && item.EdiGenerationId.HasValue &&
                           generationIds.Contains(item.EdiGenerationId.Value) && item.ResponseType == "999")
            .Select(item => new { GenerationId = item.EdiGenerationId!.Value, item.Stage, item.Id })
            .ToListAsync(cancellationToken);
        var acknowledgements = await db.ClaimAcknowledgementOutcomes.AsNoTracking()
            .Where(item => item.AgencyId == agencyId && generationIds.Contains(item.EdiGenerationId))
            .ToListAsync(cancellationToken);
        var remittances = await db.RemittanceClaimOutcomes.AsNoTracking()
            .Where(item => item.AgencyId == agencyId && item.EdiGenerationId.HasValue &&
                           generationIds.Contains(item.EdiGenerationId.Value))
            .ToListAsync(cancellationToken);
        var corrections = await db.ClaimCorrections.AsNoTracking()
            .Where(item => item.AgencyId == agencyId && item.BillingPeriodId == period.Id)
            .ToListAsync(cancellationToken);
        var correctionIds = corrections.Select(item => item.Id).ToList();
        var correctionSubmissions = await db.ClaimCorrectionSubmissions.AsNoTracking()
            .Where(item => correctionIds.Contains(item.ClaimCorrectionId))
            .ToListAsync(cancellationToken);

        var submissions = new Dictionary<int, List<ClaimSubmissionRecord>>();
        foreach (var generation in generations)
        {
            ParsedClaimSubmission parsed;
            try { parsed = ClaimResponseReader.ReadSubmission(generation.Content); }
            catch (Exception failure) when (failure is FormatException or InvalidOperationException or OverflowException)
            { continue; }
            var fileVerdict = fileVerdicts.Where(item => item.GenerationId == generation.Id)
                .OrderByDescending(item => item.Id).Select(item => item.Stage).FirstOrDefault();
            foreach (var claim in parsed.Claims)
            {
                if (!TryReadNoteId(claim.ClaimReference, out var noteId))
                    continue;
                var acknowledgement = acknowledgements
                    .Where(item => item.EdiGenerationId == generation.Id && item.ClaimReference == claim.ClaimReference)
                    .MaxBy(item => item.Id);
                var remittance = remittances
                    .Where(item => item.EdiGenerationId == generation.Id && item.ClaimReference == claim.ClaimReference)
                    .MaxBy(item => item.Id);
                var correctionId = generation.IsCorrection
                    ? corrections.Where(item => item.NoteId == noteId && correctionSubmissions.Any(link =>
                            link.ClaimCorrectionId == item.Id && link.EdiGenerationId == generation.Id))
                        .Select(item => (long?)item.Id).FirstOrDefault()
                    : null;
                ClaimCorrectionAction? action = generation.IsCorrection
                    ? claim.FrequencyCode switch
                    {
                        "7" => ClaimCorrectionAction.Replace,
                        "8" => ClaimCorrectionAction.Void,
                        _ => ClaimCorrectionAction.Resubmit
                    }
                    : null;
                var facts = new ClaimSubmissionFacts(
                    action,
                    fileVerdict switch
                    {
                        BillingSubmissionStage.FunctionalAccepted => ClaimFileVerdict.Accepted,
                        BillingSubmissionStage.FunctionalRejected => ClaimFileVerdict.Rejected,
                        _ => ClaimFileVerdict.None
                    },
                    acknowledgement?.Disposition,
                    remittance?.Status,
                    remittance?.PayerClaimControlNumber);
                if (!submissions.TryGetValue(noteId, out var list))
                    submissions[noteId] = list = [];
                list.Add(new ClaimSubmissionRecord(generation.Id, correctionId, facts));
            }
        }

        var unsent = corrections.Where(item => correctionSubmissions.All(link => link.ClaimCorrectionId != item.Id))
            .Select(item => item.NoteId).ToHashSet();
        return new ClaimHistory(period.Lines.Select(line => line.NoteId).ToList(), submissions, unsent, corrections);
    }

    // CLM01 is "{control}-{period}-{note}"; see ClaimSubmissionIdentity.
    private static bool TryReadNoteId(string claimReference, out int noteId)
    {
        var parts = claimReference.Split('-');
        noteId = 0;
        return parts.Length == 3 && int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out noteId);
    }

    private sealed record ClaimSubmissionRecord(long GenerationId, long? CorrectionId, ClaimSubmissionFacts Facts);

    private sealed class ClaimHistory(
        IReadOnlyList<int> noteIds,
        IReadOnlyDictionary<int, List<ClaimSubmissionRecord>> submissions,
        IReadOnlySet<int> unsentCorrections,
        IReadOnlyList<ClaimCorrection> corrections)
    {
        public IReadOnlyList<int> NoteIds { get; } = noteIds;
        public IReadOnlyList<ClaimCorrection> Corrections { get; } = corrections;

        public IReadOnlyList<ClaimSubmissionRecord> SubmissionsFor(int noteId) =>
            submissions.TryGetValue(noteId, out var list) ? list : [];

        public ClaimCorrectionOptions Options(int noteId) =>
            ClaimCorrectionRules.Evaluate(
                SubmissionsFor(noteId).Select(item => item.Facts).ToList(),
                unsentCorrections.Contains(noteId));

        /// <summary>The submission whose payer claim number a replacement or void cites.</summary>
        public ClaimSubmissionRecord? StandingSubmission(int noteId)
        {
            var number = Options(noteId).PayerClaimControlNumber;
            return number is null ? null : SubmissionsFor(noteId)
                .LastOrDefault(item => item.Facts.PayerClaimControlNumber == number);
        }
    }
}
