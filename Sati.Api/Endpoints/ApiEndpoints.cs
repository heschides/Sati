using System.Data;
using System.Data.Common;
using System.Security.Claims;
using System.Text.Json;
using System.Text;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using Microsoft.Extensions.Options;
using Sati.Api.Data;
using Sati.Api.Infrastructure;
using Sati.Api.Security;
using Sati;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Forms;
using Sati.Models;

namespace Sati.Api.Endpoints;

internal sealed record CaseloadNoteSummaryRow(
    int Id,
    int PersonId,
    int? Status,
    DateTime? EventDate,
    int? NoteType,
    int? Activities,
    int? FormType,
    long? ReleaseObligationId);

internal static partial class ApiEndpoints
{
    public static void MapSatiApi(this WebApplication app)
    {
        MapAuth(app);
        var api = app.MapGroup("/api/v1")
            .RequireAuthorization()
            .AddEndpointFilter<ValidatedActorFilter>()
            .AddEndpointFilter<SingleAttemptWriteFilter>();
        MapProfile(api);
        MapAudit(api);
        MapAdmin(api);
        MapUsers(api);
        MapAccountLifecycle(api);
        MapSupervisor(api);
        MapCaseload(api);
        MapPeople(api);
        MapPersonPhotos(api);
        MapReviews(api);
        MapAssessments(api);
        MapSafetyPlans(api);
        MapCwicPackets(api);
        MapHousingSupportFunds(api);
        MapChat(api);
        MapAnnualPackets(api);
        MapReleaseObligations(api);
        MapSignatures(api);
        MapProviders(api);
        MapAtRequests(api);
        MapCheckRequests(api);
        MapAiContext(api);
        MapNotes(api);
        MapAdminFormNoteCorrections(api);
        MapAdminReleaseNoteCorrections(api);
        MapSettings(api);
        MapScratchpads(api);
        MapExemptDates(api);
        MapIncentives(api);
        MapReports(api);
        MapBilling(api);
        MapForms(api);
        MapFormAttestationChangeReviewFlags(api);
        MapDocuments(api);
        MapDocumentTemplates(api);
        MapIncidents(api);
    }

    private static void MapIncidents(RouteGroupBuilder api)
    {
        api.MapPost("/incidents", async Task<IResult> (
            IncidentReportRequest request,
            ClaimsPrincipal principal,
            IncidentAggregator aggregator,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            var validation = ValidateIncidentReport(request);
            if (validation is not null)
                return Results.ValidationProblem(validation);

            var occurredAt = DateTime.SpecifyKind(request.OccurredAtUtc, DateTimeKind.Utc);
            var now = DateTime.UtcNow;
            if (occurredAt < now.AddDays(-90) || occurredAt > now.AddMinutes(5))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["occurredAtUtc"] = ["Incident time must be within the last 90 days and not in the future."]
                });
            }

            var incident = await aggregator.UpsertAsync(new IncidentAggregation(
                actor.AgencyId,
                actor.Role == "PlatformOperator" ? IncidentScopes.Platform : IncidentScopes.Agency,
                request.Source,
                request.Severity,
                request.Operation,
                request.Release,
                request.ExceptionFingerprint,
                occurredAt,
                request.Reference,
                actor.Role,
                request.CrashDiagnostic), cancellationToken);
            return Results.Accepted(value: ToIncidentDto(incident));
        });

        api.MapGet("/admin/incidents", async Task<IResult> (
            int? days,
            int? take,
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!actor.HasAdminPermissions)
                return Results.Forbid();

            var window = days ?? 30;
            var limit = take ?? 250;
            if (window is < 1 or > 90 || limit is < 1 or > 500)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["query"] = ["Use 1-90 days and request 1-500 rows."] });

            var start = DateTime.UtcNow.AddDays(-window);
            var incidents = await db.IncidentGroups.AsNoTracking()
                .Where(candidate => candidate.AgencyId == actor.AgencyId &&
                                    candidate.Scope == IncidentScopes.Agency &&
                                    candidate.LastSeenUtc >= start)
                .OrderByDescending(candidate => candidate.LastSeenUtc)
                .ThenByDescending(candidate => candidate.Id)
                .Take(limit)
                .ToListAsync(cancellationToken);
            var dtos = incidents.Select(ToIncidentDto).ToList();
            return Results.Ok(new AdminIncidentDashboardDto(
                DateTime.UtcNow,
                IncidentHealthScoring.Calculate(dtos, DateTime.UtcNow, window),
                dtos));
        });

        api.MapPut("/admin/incidents/{incidentId:long}/status", async Task<IResult> (
            long incidentId,
            UpdateIncidentStatusRequest request,
            ClaimsPrincipal principal,
            ApiDbContext db,
            AuditTrail auditTrail,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!actor.HasAdminPermissions)
                return Results.Forbid();
            if (request.Status is not ("Open" or "Investigating" or "Resolved"))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["status"] = ["Status must be Open, Investigating, or Resolved."] });
            var incident = await db.IncidentGroups.SingleOrDefaultAsync(candidate =>
                candidate.Id == incidentId && candidate.AgencyId == actor.AgencyId &&
                candidate.Scope == IncidentScopes.Agency,
                cancellationToken);
            if (incident is null)
                return Results.NotFound();
            incident.Status = request.Status;
            auditTrail.Record(actor, AuditActions.IncidentStatusUpdated, "IncidentGroup", metadataJson:
                JsonSerializer.Serialize(new { incidentId, status = request.Status }));
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(ToIncidentDto(incident));
        });

        api.MapGet("/platform/incidents", async Task<IResult> (
            int? days,
            int? take,
            ClaimsPrincipal principal,
            ApiDbContext db,
            AuditTrail auditTrail,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (actor.Role != "PlatformOperator")
                return Results.Forbid();
            var window = days ?? 30;
            var limit = take ?? 500;
            if (window is < 1 or > 90 || limit is < 1 or > 1000)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["query"] = ["Use 1-90 days and request 1-1000 rows."] });

            var observedAt = DateTime.UtcNow;
            var start = observedAt.AddDays(-window);
            var incidents = await db.IncidentGroups.AsNoTracking()
                .Where(candidate => candidate.LastSeenUtc >= start)
                .OrderByDescending(candidate => candidate.LastSeenUtc)
                .ThenByDescending(candidate => candidate.Id)
                .Take(limit)
                .ToListAsync(cancellationToken);
            var dtos = incidents.Select(ToIncidentDto).ToList();
            var agencies = await db.Agencies.AsNoTracking().OrderBy(candidate => candidate.Name)
                .Select(candidate => new { candidate.Id, candidate.Name })
                .ToListAsync(cancellationToken);
            var agencyHealth = agencies.Select(agency => new PlatformAgencyHealthDto(
                agency.Id,
                agency.Name,
                IncidentHealthScoring.Calculate(dtos.Where(item =>
                    item.AgencyId == agency.Id && item.Scope == IncidentScopes.Agency), observedAt, window)))
                .ToList();
            auditTrail.Record(actor, AuditActions.PlatformIncidentsViewed, "IncidentGroup");
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(new PlatformIncidentDashboardDto(
                observedAt,
                IncidentHealthScoring.Calculate(dtos, observedAt, window),
                agencyHealth,
                dtos));
        });
    }

    private static void MapAudit(RouteGroupBuilder api)
    {
        api.MapGet("/audit-events", async Task<IResult> (
            DateTime? from,
            DateTime? to,
            string? action,
            int? take,
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!actor.HasAdminPermissions)
                return Results.Forbid();

            var start = from?.ToUniversalTime() ?? DateTime.UtcNow.AddDays(-30);
            var end = to?.ToUniversalTime() ?? DateTime.UtcNow;
            var limit = take ?? 100;
            if (end < start || (end - start).TotalDays > 366 || limit is < 1 or > 500 || action?.Length > 100)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["query"] = ["Use a valid window no longer than one year, an action up to 100 characters, and a take value from 1 to 500."]
                });
            }

            var query = db.AuditEvents.AsNoTracking()
                .Where(candidate => candidate.AgencyId == actor.AgencyId &&
                                    candidate.OccurredAtUtc >= start &&
                                    candidate.OccurredAtUtc <= end);
            if (!string.IsNullOrWhiteSpace(action))
                query = query.Where(candidate => candidate.Action == action);

            var events = await query
                .OrderByDescending(candidate => candidate.OccurredAtUtc)
                .ThenByDescending(candidate => candidate.Id)
                .Take(limit)
                .Select(candidate => new AuditEventDto(
                    candidate.Id,
                    candidate.EventId,
                    candidate.AgencyId,
                    candidate.ActorUserId,
                    candidate.Action,
                    candidate.ResourceType,
                    candidate.ResourceId,
                    candidate.OccurredAtUtc,
                    candidate.CorrelationId))
                .ToListAsync(cancellationToken);
            return Results.Ok(events);
        });
    }

    private static void MapAdmin(RouteGroupBuilder api)
    {
        api.MapPost("/admin/demo/reset", async Task<IResult> (
            DemoResetRequest request,
            ClaimsPrincipal principal,
            IOptions<SatiApiOptions> sati,
            DemoResetCoordinator reset,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!actor.HasAdminPermissions)
                return Results.Forbid();
            var isValidatedDemo =
                string.Equals(sati.Value.ExpectedDatabaseName, "SatiDemo", StringComparison.Ordinal) &&
                string.Equals(sati.Value.ExpectedEnvironment, "Demo", StringComparison.Ordinal);
            var isIsolatedTestHost =
                string.Equals(sati.Value.ExpectedDatabaseName, "SatiApiTests", StringComparison.Ordinal) &&
                string.Equals(sati.Value.ExpectedEnvironment, "Testing", StringComparison.Ordinal);
            if (!isValidatedDemo && !isIsolatedTestHost)
                return Results.NotFound();
            if (!string.Equals(request.Confirmation?.Trim(), "RESET DEMO", StringComparison.Ordinal))
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["confirmation"] = ["Enter RESET DEMO exactly to restore the canonical Demo baseline."]
                });

            try
            {
                return Results.Ok(await reset.RequestAsync(actor.UserId, cancellationToken));
            }
            catch (InvalidOperationException exception)
            {
                return Results.Problem(exception.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
            }
            catch (HttpRequestException)
            {
                return Results.Problem(
                    "The Demo reset service did not report successful completion. An administrator must review the reset operation before retrying.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return Results.Problem(
                    "The Demo reset service did not finish within the protected reset window. An administrator must review the reset operation before retrying.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        });

        api.MapGet("/admin/overview", async Task<IResult> (
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!actor.HasAdminPermissions)
                return Results.Forbid();

            var now = DateTime.UtcNow;
            var today = now.Date;
            var thirtyDaysAgo = now.AddDays(-30);
            var monthStart = new DateTime(now.Year, now.Month, 1);
            var agencyName = await db.Agencies.AsNoTracking()
                .Where(agency => agency.Id == actor.AgencyId)
                .Select(agency => agency.Name)
                .SingleAsync(cancellationToken);
            var userCount = await db.Users.AsNoTracking()
                .CountAsync(user => user.AgencyId == actor.AgencyId && user.Role != "PlatformOperator", cancellationToken);
            var caseManagerCount = await db.Users.AsNoTracking()
                .CountAsync(user => user.AgencyId == actor.AgencyId &&
                    (user.Permissions & UserPermissions.CaseManagement) != 0, cancellationToken);
            var personCount = await db.People.AsNoTracking()
                .CountAsync(person => person.AgencyId == actor.AgencyId &&
                    db.Users.Any(user => user.Id == person.UserId && user.AgencyId == actor.AgencyId),
                    cancellationToken);
            var notesThisMonth = await db.Notes.AsNoTracking()
                .CountAsync(note => note.EventDate >= monthStart &&
                    db.People.Any(person => person.Id == note.PersonId && person.AgencyId == actor.AgencyId &&
                        db.Users.Any(user => user.Id == person.UserId && user.AgencyId == actor.AgencyId)),
                    cancellationToken);
            var activeUsers = await db.AuditEvents.AsNoTracking()
                .Where(auditEvent => auditEvent.AgencyId == actor.AgencyId &&
                    auditEvent.OccurredAtUtc >= thirtyDaysAgo)
                .Select(auditEvent => auditEvent.ActorUserId)
                .Distinct()
                .CountAsync(cancellationToken);
            var successfulSignIns = await db.AuditEvents.AsNoTracking()
                .CountAsync(auditEvent => auditEvent.AgencyId == actor.AgencyId &&
                    auditEvent.OccurredAtUtc >= thirtyDaysAgo &&
                    auditEvent.Action == AuditActions.AuthenticationSucceeded,
                    cancellationToken);
            var personChanges = await db.PersonVersions.AsNoTracking()
                .CountAsync(version => version.AgencyId == actor.AgencyId &&
                    version.ChangedAtUtc >= thirtyDaysAgo && version.ChangeKind != "TrackingBaseline",
                    cancellationToken);
            var auditEventsToday = await db.AuditEvents.AsNoTracking()
                .CountAsync(auditEvent => auditEvent.AgencyId == actor.AgencyId &&
                    auditEvent.OccurredAtUtc >= today,
                    cancellationToken);
            var lastActivity = await db.AuditEvents.AsNoTracking()
                .Where(auditEvent => auditEvent.AgencyId == actor.AgencyId)
                .MaxAsync(auditEvent => (DateTime?)auditEvent.OccurredAtUtc, cancellationToken);

            return Results.Ok(new AdminOverviewDto(
                actor.AgencyId, agencyName, userCount, caseManagerCount, personCount,
                notesThisMonth, activeUsers, successfulSignIns, personChanges,
                auditEventsToday, lastActivity));
        });

        api.MapGet("/admin/operations", async Task<IResult> (
            ClaimsPrincipal principal,
            ApiDbContext db,
            Microsoft.Extensions.Options.IOptions<SatiApiOptions> options,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!actor.HasAdminPermissions)
                return Results.Forbid();

            PreventSensitiveResponseCaching(httpContext);
            var auditCount = await db.AuditEvents.AsNoTracking()
                .LongCountAsync(candidate => candidate.AgencyId == actor.AgencyId, cancellationToken);
            var ediCount = await db.EdiGenerations.AsNoTracking()
                .LongCountAsync(candidate => candidate.AgencyId == actor.AgencyId, cancellationToken);
            var ediCharacters = await db.EdiGenerations.AsNoTracking()
                .Where(candidate => candidate.AgencyId == actor.AgencyId)
                .SumAsync(candidate => (long?)candidate.Content.Length, cancellationToken) ?? 0;
            var oldestAudit = await db.AuditEvents.AsNoTracking()
                .Where(candidate => candidate.AgencyId == actor.AgencyId)
                .MinAsync(candidate => (DateTime?)candidate.OccurredAtUtc, cancellationToken);
            var oldestEdi = await db.EdiGenerations.AsNoTracking()
                .Where(candidate => candidate.AgencyId == actor.AgencyId)
                .MinAsync(candidate => (DateTime?)candidate.CreatedAtUtc, cancellationToken);

            return Results.Ok(new AdminOperationsDto(
                DateTime.UtcNow,
                "Healthy",
                "PolicyOnly",
                options.Value.AuditRetentionDays,
                options.Value.EdiReplayRetentionDays,
                auditCount,
                ediCount,
                ediCharacters,
                oldestAudit,
                oldestEdi));
        });

        // Schema drift detail, for the reconciliation that has to classify each
        // discrepancy before it can be fixed. `/health/ready` reports only a status
        // word, and its description never reaches the anonymous response writer, so
        // without this the drift is visible only in the API's own logs.
        //
        // The report names tables and columns, never row data, so it carries no
        // consumer information. It is still Admin-gated: schema shape is
        // operational detail about the deployment, not something every signed-in
        // case manager needs.
        api.MapGet("/admin/schema-drift", async Task<IResult> (
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!actor.HasAdminPermissions)
                return Results.Forbid();

            // The chain is passed empty on purpose. Every migration belongs to
            // SatiContext in the desktop project; this model has no chain of its
            // own, so it reports the applied ids as data and leaves the verdict to
            // a caller that owns the chain.
            return Results.Ok(SchemaComparison.Report(
                SchemaSnapshotReader.FromModel(db.Model, "The API model", describesEveryTable: false),
                await SchemaSnapshotReader.ReadDatabaseAsync(db, "the database", cancellationToken),
                chainMigrationIds: [],
                await SchemaSnapshotReader.ReadAppliedMigrationsAsync(db, cancellationToken)));
        });

        api.MapGet("/admin/people", async Task<IResult> (
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!actor.HasAdminPermissions)
                return Results.Forbid();

            var rows = await (
                from person in db.People.AsNoTracking()
                join user in db.Users.AsNoTracking() on person.UserId equals user.Id
                where person.AgencyId == actor.AgencyId && user.AgencyId == actor.AgencyId
                orderby person.LastName, person.FirstName, person.Id
                select new
                {
                    PersonId = person.Id,
                    person.LastName,
                    person.FirstName,
                    person.Revision,
                    AssignedUserId = user.Id,
                    user.DisplayName,
                    person.IsTestData,
                    person.CreatedAtUtc,
                    person.Status
                })
                .ToListAsync(cancellationToken);

            // Status name lookup happens here, client-side, after materialization — indexing a
            // string array by a column value does not translate to SQL.
            var people = rows.Select(row => new AdminPersonListItemDto(
                row.PersonId,
                ((row.LastName ?? string.Empty) + ", " + (row.FirstName ?? string.Empty)).Trim(' ', ','),
                row.Revision,
                row.AssignedUserId,
                row.DisplayName,
                row.IsTestData,
                row.CreatedAtUtc,
                ContractMapper.NameAt(ContractMapper.PersonStatusNames, row.Status, "Active"))).ToList();
            return Results.Ok(people);
        });

        api.MapPost("/admin/test-data/consumers/{personId:int}/delete", async Task<IResult> (
            int personId,
            DeleteTestConsumerRequest request,
            ClaimsPrincipal principal,
            ApiDbContext db,
            AuditTrail auditTrail,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!actor.HasAdminPermissions)
                return Results.Forbid();
            if (personId <= 0 || request.ExpectedRevision <= 0)
            {
                return Results.BadRequest(new ApiErrorDto(
                    "invalid_test_consumer",
                    "Select a current consumer record and try again.",
                    string.Empty));
            }
            if (!TestDataDeletionRules.HasValidConsumerAttestation(request.Attestation))
            {
                return Results.BadRequest(new ApiErrorDto(
                    "invalid_test_data_attestation",
                    "The required test-data affirmation was not supplied.",
                    string.Empty));
            }

            PreventSensitiveResponseCaching(httpContext);
            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

            var person = await db.People.AsNoTracking().SingleOrDefaultAsync(candidate =>
                candidate.Id == personId && candidate.AgencyId == actor.AgencyId &&
                db.Users.Any(user => user.Id == candidate.UserId && user.AgencyId == actor.AgencyId),
                cancellationToken);
            if (person is null)
                return Results.NotFound();
            if (!person.IsTestData)
            {
                return Results.Conflict(new ApiErrorDto(
                    "consumer_not_test_data",
                    "This consumer was not marked as Test when created and cannot be deleted with the test-data tool.",
                    string.Empty));
            }
            if (person.Revision != request.ExpectedRevision)
                return StaleTestConsumerConflict();
            if (await db.FrozenSignatureDocuments.AsNoTracking().AnyAsync(document => document.PersonId == personId &&
                document.AgencyId == actor.AgencyId, cancellationToken))
                return Results.Conflict(new ApiErrorDto("consumer_has_signature_history", SignatureRules.RetainedHistoryMessage, string.Empty));
            if (await db.ChatRooms.AsNoTracking().AnyAsync(room => room.PersonId == personId &&
                room.AgencyId == actor.AgencyId, cancellationToken))
                return ChatRetainedConsumerConflict();

            var claimLineCount = await db.ClaimLines.AsNoTracking().CountAsync(claimLine =>
                db.Notes.Any(note => note.Id == claimLine.NoteId && note.PersonId == personId),
                cancellationToken);
            if (claimLineCount > 0)
            {
                return Results.Conflict(new ApiErrorDto(
                    "test_consumer_has_claims",
                    TestDataDeletionRules.ConsumerHasClaimsMessage,
                    string.Empty));
            }

            try
            {
                var appointmentsDeleted = await db.Appointments
                    .Where(appointment => db.ReviewItems.Any(review =>
                        review.Id == appointment.ReviewItemId && review.PersonId == personId))
                    .ExecuteDeleteAsync(cancellationToken);
                var reviewsDeleted = await db.ReviewItems
                    .Where(review => review.PersonId == personId)
                    .ExecuteDeleteAsync(cancellationToken);
                var contactsDeleted = await db.PersonContacts
                    .Where(contact => contact.PersonId == personId)
                    .ExecuteDeleteAsync(cancellationToken);
                var personProvidersDeleted = await db.PersonProviders
                    .Where(link => link.PersonId == personId)
                    .ExecuteDeleteAsync(cancellationToken);
                var documentAcknowledgmentsDeleted = await db.DocumentAcknowledgments
                    .Where(receipt => db.DocumentArtifacts.Any(artifact => artifact.Id == receipt.DocumentArtifactId && artifact.PersonId == personId && artifact.AgencyId == actor.AgencyId))
                    .ExecuteDeleteAsync(cancellationToken);
                var safetyPlansDeleted = await db.SafetyPlans.Where(plan => plan.PersonId == personId).ExecuteDeleteAsync(cancellationToken);
                var documentArtifactsDeleted = await db.DocumentArtifacts
                    .Where(artifact => artifact.PersonId == personId && artifact.AgencyId == actor.AgencyId)
                    .ExecuteDeleteAsync(cancellationToken);
                // Append-only clinical history is normally never deleted. The
                // versioned Admin attestation makes synthetic test-data deletion
                // the narrow exception, matching PersonVersion below.
                var formAttestationsDeleted = await db.FormAttestations
                    .Where(attestation => db.Forms.Any(form =>
                        form.Id == attestation.FormId && form.PersonId == personId))
                    .ExecuteDeleteAsync(cancellationToken);
                var formsDeleted = await db.Forms
                    .Where(form => form.PersonId == personId)
                    .ExecuteDeleteAsync(cancellationToken);
                var atRequestItemsDeleted = await db.AtRequestItems
                    .Where(item => db.AtRequests.Any(atRequest =>
                        atRequest.Id == item.ATRequestId && atRequest.PersonId == personId))
                    .ExecuteDeleteAsync(cancellationToken);
                var atRequestsDeleted = await db.AtRequests
                    .Where(atRequest => atRequest.PersonId == personId)
                    .ExecuteDeleteAsync(cancellationToken);
                await db.RepresentativePayeeLedgerEntries
                    .Where(entry => entry.PersonId == personId)
                    .ExecuteDeleteAsync(cancellationToken);
                var checkRequestsDeleted = await db.CheckRequests
                    .Where(request => request.PersonId == personId)
                    .ExecuteDeleteAsync(cancellationToken);
                await db.CheckRequestTemplates
                    .Where(template => template.PersonId == personId)
                    .ExecuteDeleteAsync(cancellationToken);
                var assessmentsDeleted = await db.ComprehensiveAssessments
                    .Where(assessment => assessment.PersonId == personId)
                    .ExecuteDeleteAsync(cancellationToken);
                var notesDeleted = await db.Notes
                    .Where(note => note.PersonId == personId)
                    .ExecuteDeleteAsync(cancellationToken);

                // PersonVersion is normally append-only. Test-data deletion is the
                // one narrow exception because each snapshot contains a copy of the
                // synthetic consumer record. AuditEvent remains append-only.
                var personVersionsDeleted = await db.PersonVersions
                    .Where(version => version.PersonId == personId && version.AgencyId == actor.AgencyId)
                    .ExecuteDeleteAsync(cancellationToken);

                var peopleDeleted = await db.People
                    .Where(candidate => candidate.Id == personId &&
                        candidate.Revision == request.ExpectedRevision &&
                        candidate.AgencyId == actor.AgencyId && candidate.IsTestData &&
                        db.Users.Any(user => user.Id == candidate.UserId && user.AgencyId == actor.AgencyId))
                    .ExecuteDeleteAsync(cancellationToken);
                if (peopleDeleted != 1)
                    return StaleTestConsumerConflict();

                var result = new TestConsumerDeletionResultDto(
                    personId,
                    formsDeleted,
                    notesDeleted,
                    contactsDeleted,
                    reviewsDeleted,
                    appointmentsDeleted,
                    assessmentsDeleted,
                    atRequestsDeleted,
                    atRequestItemsDeleted,
                    personVersionsDeleted,
                    personProvidersDeleted,
                    formAttestationsDeleted,
                    documentArtifactsDeleted, safetyPlansDeleted, documentAcknowledgmentsDeleted, checkRequestsDeleted);
                auditTrail.Record(
                    actor,
                    AuditActions.TestConsumerDeleted,
                    "Person",
                    personId,
                    JsonSerializer.Serialize(new
                    {
                        attestationVersion = TestDataDeletionRules.ConsumerAttestation,
                        relatedRecordsDeleted = result.RelatedRecordsDeleted,
                        formsDeleted = result.FormsDeleted,
                        notesDeleted = result.NotesDeleted,
                        contactsDeleted = result.ContactsDeleted,
                        personProvidersDeleted = result.PersonProvidersDeleted,
                        formAttestationsDeleted = result.FormAttestationsDeleted,
                        documentArtifactsDeleted = result.DocumentArtifactsDeleted,
                        safetyPlansDeleted = result.SafetyPlansDeleted,
                        documentAcknowledgmentsDeleted = result.DocumentAcknowledgmentsDeleted,
                        reviewsDeleted = result.ReviewsDeleted,
                        appointmentsDeleted = result.AppointmentsDeleted,
                        assessmentsDeleted = result.AssessmentsDeleted,
                        atRequestsDeleted = result.AtRequestsDeleted,
                        checkRequestsDeleted = result.CheckRequestsDeleted,
                        atRequestItemsDeleted = result.AtRequestItemsDeleted,
                        personVersionsDeleted = result.PersonVersionsDeleted
                    }));
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return Results.Ok(result);
            }
            catch (DbUpdateException)
            {
                return Results.Conflict(new ApiErrorDto(
                    "test_consumer_related_record_changed",
                    "The consumer was not deleted because a related record changed or is protected. Refresh and try again, or seek guidance in the help menu.",
                    string.Empty));
            }
            catch (DbException)
            {
                return Results.Conflict(new ApiErrorDto(
                    "test_consumer_related_record_changed",
                    "The consumer was not deleted because a related record changed or is protected. Refresh and try again, or seek guidance in the help menu.",
                    string.Empty));
            }
        });

        // Legal holds — the fail-closed gate rule-3 deletion checks before removing a record.
        // Deliberately narrower than OPERATIONS.md's full record-class/scope hold model; this
        // exists only to gate consumer deletion. Release is single-admin for v1, a documented
        // shortfall against OPERATIONS.md's dual-control requirement.
        api.MapPost("/admin/legal-holds", async Task<IResult> (
            PlaceLegalHoldRequest request,
            ClaimsPrincipal principal,
            ApiDbContext db,
            AuditTrail auditTrail,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!actor.HasAdminPermissions)
                return Results.Forbid();
            if (string.IsNullOrWhiteSpace(request.Reason))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["reason"] = ["A reason is required to place a legal hold."]
                });
            }

            var personExists = await db.People.AsNoTracking().AnyAsync(candidate =>
                candidate.Id == request.PersonId && candidate.AgencyId == actor.AgencyId,
                cancellationToken);
            if (!personExists)
                return Results.NotFound();

            var hold = new ServerLegalHold
            {
                AgencyId = actor.AgencyId,
                PersonId = request.PersonId,
                Reason = request.Reason.Trim(),
                CaseReference = string.IsNullOrWhiteSpace(request.CaseReference) ? null : request.CaseReference.Trim(),
                IssuedBy = string.IsNullOrWhiteSpace(request.IssuedBy) ? null : request.IssuedBy.Trim(),
                EffectiveAtUtc = request.EffectiveAtUtc,
                PlacedByUserId = actor.UserId
            };
            db.LegalHolds.Add(hold);
            auditTrail.Record(
                actor,
                AuditActions.LegalHoldPlaced,
                "LegalHold",
                metadataJson: JsonSerializer.Serialize(new { personId = request.PersonId }));
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(ToLegalHoldDto(hold));
        });

        api.MapPost("/admin/legal-holds/{legalHoldId:int}/release", async Task<IResult> (
            int legalHoldId,
            ReleaseLegalHoldRequest request,
            ClaimsPrincipal principal,
            ApiDbContext db,
            AuditTrail auditTrail,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!actor.HasAdminPermissions)
                return Results.Forbid();

            var hold = await db.LegalHolds.SingleOrDefaultAsync(candidate =>
                candidate.Id == legalHoldId && candidate.AgencyId == actor.AgencyId,
                cancellationToken);
            if (hold is null)
                return Results.NotFound();
            if (hold.IsReleased)
            {
                return Results.Conflict(new ApiErrorDto(
                    "legal_hold_already_released",
                    "This legal hold has already been released.",
                    string.Empty));
            }

            hold.IsReleased = true;
            hold.ReleasedByUserId = actor.UserId;
            hold.ReleasedAtUtc = DateTime.UtcNow;
            hold.ReleaseNote = string.IsNullOrWhiteSpace(request.ReleaseNote) ? null : request.ReleaseNote.Trim();
            auditTrail.Record(
                actor,
                AuditActions.LegalHoldReleased,
                "LegalHold",
                metadataJson: JsonSerializer.Serialize(new { legalHoldId, personId = hold.PersonId }));
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(ToLegalHoldDto(hold));
        });

        api.MapGet("/admin/legal-holds", async Task<IResult> (
            int personId,
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!actor.HasAdminPermissions)
                return Results.Forbid();

            var holds = await db.LegalHolds.AsNoTracking()
                .Where(hold => hold.PersonId == personId && hold.AgencyId == actor.AgencyId)
                .OrderByDescending(hold => hold.PlacedAtUtc)
                .ToListAsync(cancellationToken);
            return Results.Ok(holds.Select(ToLegalHoldDto).ToList());
        });

        // Rule-3 deletion: permanently deletes an ordinary consumer created within
        // ConsumerDeletionRules.DeletionWindowDays. Extends the test-consumer-delete route's
        // transaction shape and child-record cascade, gated on the creation-time window, the A1
        // billing-integrity check, and the A3 legal-hold registry instead of IsTestData. Unlike
        // test-consumer deletion, this deletes claim lines rather than refusing whenever one
        // exists — A1 permits draft and synthetic billing inside the window. The audit event is
        // a tombstone: an itemized inventory by id, date, and type, captured before any delete,
        // with no narrative, name, MaineCareId, birth date, or address.
        // See HANDOFF_CLIENT_DELETION_POLICY.md.
        api.MapPost("/admin/consumers/{personId:int}/delete-in-window", async Task<IResult> (
            int personId,
            DeleteConsumerInWindowRequest request,
            ClaimsPrincipal principal,
            ApiDbContext db,
            AuditTrail auditTrail,
            ILegalHoldRegistry legalHoldRegistry,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!actor.HasAdminPermissions)
                return Results.Forbid();
            if (personId <= 0 || request.ExpectedRevision <= 0)
            {
                return Results.BadRequest(new ApiErrorDto(
                    "invalid_consumer_deletion", "Select a current consumer record and try again.",
                    string.Empty));
            }
            if (!ConsumerDeletionRules.HasValidConsumerAttestation(request.Attestation))
            {
                return Results.BadRequest(new ApiErrorDto(
                    "invalid_deletion_attestation",
                    "The required deletion affirmation was not supplied.", string.Empty));
            }
            if (string.IsNullOrWhiteSpace(request.Reason))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["reason"] = ["A reason is required to delete a consumer."]
                });
            }

            PreventSensitiveResponseCaching(httpContext);
            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);

            var person = await db.People.AsNoTracking().SingleOrDefaultAsync(candidate =>
                candidate.Id == personId && candidate.AgencyId == actor.AgencyId,
                cancellationToken);
            if (person is null)
                return Results.NotFound();
            if (request.ExpectedRevision != person.Revision)
                return StalePersonConflict();
            if (await db.FrozenSignatureDocuments.AsNoTracking().AnyAsync(document => document.PersonId == personId &&
                document.AgencyId == actor.AgencyId, cancellationToken))
                return Results.Conflict(new ApiErrorDto("consumer_has_signature_history", SignatureRules.RetainedHistoryMessage, string.Empty));
            if (await db.ChatRooms.AsNoTracking().AnyAsync(room => room.PersonId == personId &&
                room.AgencyId == actor.AgencyId, cancellationToken))
                return ChatRetainedConsumerConflict();
            if (!ConsumerDeletionRules.IsWithinDeletionWindow(person.CreatedAtUtc, DateTime.UtcNow))
            {
                return Results.Conflict(new ApiErrorDto(
                    "consumer_outside_deletion_window",
                    ConsumerDeletionRules.OutsideWindowMessage, string.Empty));
            }

            // A3: legal hold. Checked before any child row changes, and refused on anything but
            // an explicit Clear — Active, Unavailable, and any registry exception fail closed.
            var holdStatus = await legalHoldRegistry.GetStatusAsync(actor.AgencyId, personId, cancellationToken);
            if (holdStatus != LegalHoldStatus.Clear)
            {
                return Results.Conflict(new ApiErrorDto(
                    "consumer_legal_hold",
                    holdStatus == LegalHoldStatus.Active
                        ? ConsumerDeletionRules.LegalHoldActiveMessage
                        : ConsumerDeletionRules.LegalHoldUnavailableMessage,
                    string.Empty));
            }

            // A1: billing integrity. Draft and synthetic billing is deletable inside the
            // window; only billing that actually reached a payer blocks.
            var billingPeriodIds = await db.ClaimLines.AsNoTracking()
                .Where(claimLine => db.Notes.Any(note => note.Id == claimLine.NoteId && note.PersonId == personId))
                .Select(claimLine => claimLine.BillingPeriodId)
                .Distinct()
                .ToListAsync(cancellationToken);
            var billingFacts = new BillingIntegrityFacts(
                HasTransmittedBillingSubmissionEvent: billingPeriodIds.Count > 0 &&
                    await db.BillingSubmissionEvents.AsNoTracking().AnyAsync(submissionEvent =>
                        billingPeriodIds.Contains(submissionEvent.BillingPeriodId) &&
                        !submissionEvent.IsSynthetic &&
                        submissionEvent.Stage >= BillingSubmissionStage.Transmitted,
                        cancellationToken),
                HasNonSyntheticRemittanceClaimOutcome: billingPeriodIds.Count > 0 &&
                    await db.RemittanceClaimOutcomes.AsNoTracking().AnyAsync(outcome =>
                        outcome.BillingPeriodId != null &&
                        billingPeriodIds.Contains(outcome.BillingPeriodId.Value) &&
                        !outcome.IsSynthetic,
                        cancellationToken),
                HasSubmittedOrNonDraftBillingPeriod: billingPeriodIds.Count > 0 &&
                    await db.BillingPeriods.AsNoTracking().AnyAsync(period =>
                        billingPeriodIds.Contains(period.Id) &&
                        (period.SubmittedAt != null || period.Status != 0),
                        cancellationToken));
            if (ConsumerDeletionRules.HasTransmittedBilling(billingFacts))
            {
                return Results.Conflict(new ApiErrorDto(
                    "consumer_transmitted_billing",
                    ConsumerDeletionRules.TransmittedBillingMessage, string.Empty));
            }
            if (await db.RepresentativePayeeLedgerEntries.AsNoTracking()
                    .AnyAsync(entry => entry.PersonId == personId, cancellationToken))
            {
                return Results.Conflict(new ApiErrorDto(
                    "consumer_has_representative_payee_ledger",
                    ConsumerDeletionRules.RepresentativePayeeLedgerMessage, string.Empty));
            }

            // Itemized tombstone, captured before any delete. Ids, dates, and types only —
            // never narrative, name, MaineCareId, birth date, or address.
            var noteRows = await db.Notes.AsNoTracking()
                .Where(note => note.PersonId == personId)
                .Select(note => new { note.Id, note.EventDate, note.Status, note.Minutes, note.NoteType })
                .ToListAsync(cancellationToken);
            var noteInventory = noteRows.Select(note => new
            {
                note.Id, eventDate = note.EventDate,
                status = ContractMapper.NullableNameAt(ContractMapper.NoteStatusNames, note.Status),
                note.Minutes, noteType = ContractMapper.NullableNameAt(ContractMapper.NoteTypeNames, note.NoteType)
            }).ToList();

            var claimLineRows = await db.ClaimLines.AsNoTracking()
                .Where(claimLine => db.Notes.Any(note => note.Id == claimLine.NoteId && note.PersonId == personId))
                .Select(claimLine => new
                {
                    claimLine.Id, claimLine.DateOfService, claimLine.ProcedureCode,
                    claimLine.ProcedureModifier, claimLine.Units, claimLine.ChargeAmount,
                    claimLine.BillingPeriodId
                })
                .ToListAsync(cancellationToken);

            var formRows = await db.Forms.AsNoTracking()
                .Where(form => form.PersonId == personId)
                .Select(form => new { form.Id, form.Type, form.DueDate })
                .ToListAsync(cancellationToken);
            var formInventory = formRows.Select(form => new
            { form.Id, type = form.Type, dueDate = form.DueDate }).ToList();

            var reviewRows = await db.ReviewItems.AsNoTracking()
                .Where(review => review.PersonId == personId)
                .Select(review => new { review.Id, review.Category, review.RequestedDate })
                .ToListAsync(cancellationToken);
            var reviewInventory = reviewRows.Select(review => new
            { review.Id, category = review.Category, requestedDate = review.RequestedDate }).ToList();

            var assessmentRows = await db.ComprehensiveAssessments.AsNoTracking()
                .Where(assessment => assessment.PersonId == personId)
                .Select(assessment => new { assessment.Id, assessment.Status, assessment.CreatedAt })
                .ToListAsync(cancellationToken);

            var atRequestRows = await db.AtRequests.AsNoTracking()
                .Where(request => request.PersonId == personId)
                .Select(request => new { request.Id, request.Status, request.SubmittedDate })
                .ToListAsync(cancellationToken);

            var checkRequestRows = await db.CheckRequests.AsNoTracking()
                .Where(request => request.PersonId == personId)
                .Select(request => new { request.Id, request.RequestDate, request.PublishedAtUtc })
                .ToListAsync(cancellationToken);

            var contactRows = await db.PersonContacts.AsNoTracking()
                .Where(contact => contact.PersonId == personId)
                .Select(contact => new { contact.Id, contact.Kind })
                .ToListAsync(cancellationToken);

            var personVersionInventory = await db.PersonVersions.AsNoTracking()
                .Where(version => version.PersonId == personId)
                .Select(version => new { version.Id, version.ChangeKind, version.ChangedAtUtc })
                .ToListAsync(cancellationToken);

            // Cascade delete, in dependency order. ClaimLines before Notes: A1 permits draft
            // and synthetic claim lines inside the window, unlike test-consumer deletion, which
            // never has to delete one because it refuses whenever any claim line exists.
            var appointmentsDeleted = await db.Appointments
                .Where(appointment => db.ReviewItems.Any(review =>
                    review.Id == appointment.ReviewItemId && review.PersonId == personId))
                .ExecuteDeleteAsync(cancellationToken);
            var reviewsDeleted = await db.ReviewItems
                .Where(review => review.PersonId == personId)
                .ExecuteDeleteAsync(cancellationToken);
            var contactsDeleted = await db.PersonContacts
                .Where(contact => contact.PersonId == personId)
                .ExecuteDeleteAsync(cancellationToken);
            var personProvidersDeleted = await db.PersonProviders
                .Where(link => link.PersonId == personId)
                .ExecuteDeleteAsync(cancellationToken);
            var documentAcknowledgmentsDeleted = await db.DocumentAcknowledgments
                .Where(receipt => db.DocumentArtifacts.Any(artifact => artifact.Id == receipt.DocumentArtifactId && artifact.PersonId == personId && artifact.AgencyId == actor.AgencyId))
                .ExecuteDeleteAsync(cancellationToken);
            var safetyPlansDeleted = await db.SafetyPlans.Where(plan => plan.PersonId == personId).ExecuteDeleteAsync(cancellationToken);
            var documentArtifactsDeleted = await db.DocumentArtifacts
                .Where(artifact => artifact.PersonId == personId && artifact.AgencyId == actor.AgencyId)
                .ExecuteDeleteAsync(cancellationToken);
            var formAttestationsDeleted = await db.FormAttestations
                .Where(formAttestation => db.Forms.Any(form =>
                    form.Id == formAttestation.FormId && form.PersonId == personId))
                .ExecuteDeleteAsync(cancellationToken);
            var formsDeleted = await db.Forms
                .Where(form => form.PersonId == personId)
                .ExecuteDeleteAsync(cancellationToken);
            var atRequestItemsDeleted = await db.AtRequestItems
                .Where(item => db.AtRequests.Any(request =>
                    request.Id == item.ATRequestId && request.PersonId == personId))
                .ExecuteDeleteAsync(cancellationToken);
            var atRequestsDeleted = await db.AtRequests
                .Where(request => request.PersonId == personId)
                .ExecuteDeleteAsync(cancellationToken);
            await db.RepresentativePayeeLedgerEntries
                .Where(entry => entry.PersonId == personId)
                .ExecuteDeleteAsync(cancellationToken);
            var checkRequestsDeleted = await db.CheckRequests
                .Where(request => request.PersonId == personId)
                .ExecuteDeleteAsync(cancellationToken);
            await db.CheckRequestTemplates
                .Where(template => template.PersonId == personId)
                .ExecuteDeleteAsync(cancellationToken);
            var assessmentsDeleted = await db.ComprehensiveAssessments
                .Where(assessment => assessment.PersonId == personId)
                .ExecuteDeleteAsync(cancellationToken);
            var claimLinesDeleted = await db.ClaimLines
                .Where(claimLine => db.Notes.Any(note => note.Id == claimLine.NoteId && note.PersonId == personId))
                .ExecuteDeleteAsync(cancellationToken);
            var notesDeleted = await db.Notes
                .Where(note => note.PersonId == personId)
                .ExecuteDeleteAsync(cancellationToken);
            var personVersionsDeleted = await db.PersonVersions
                .Where(version => version.PersonId == personId && version.AgencyId == actor.AgencyId)
                .ExecuteDeleteAsync(cancellationToken);

            var peopleDeleted = await db.People
                .Where(candidate => candidate.Id == personId && candidate.Revision == request.ExpectedRevision &&
                    candidate.AgencyId == actor.AgencyId)
                .ExecuteDeleteAsync(cancellationToken);
            if (peopleDeleted != 1)
            {
                return Results.Conflict(new ApiErrorDto(
                    "consumer_changed_during_delete",
                    "This consumer changed while deletion was in progress. Refresh before trying again.",
                    string.Empty));
            }

            var result = new ConsumerDeletionResultDto(
                personId, formsDeleted, notesDeleted, contactsDeleted, reviewsDeleted, appointmentsDeleted,
                assessmentsDeleted, atRequestsDeleted, atRequestItemsDeleted, personVersionsDeleted,
                personProvidersDeleted, formAttestationsDeleted, documentArtifactsDeleted, claimLinesDeleted,
                safetyPlansDeleted, documentAcknowledgmentsDeleted, checkRequestsDeleted);

            auditTrail.Record(
                actor,
                AuditActions.ConsumerDeletedInWindow,
                "Person",
                personId,
                JsonSerializer.Serialize(new
                {
                    attestationVersion = ConsumerDeletionRules.ConsumerAttestation,
                    reason = request.Reason,
                    createdAtUtc = person.CreatedAtUtc,
                    deletedAtUtc = DateTime.UtcNow,
                    billingIntegrityCheck = billingFacts,
                    counts = result,
                    notes = noteInventory,
                    claimLines = claimLineRows,
                    forms = formInventory,
                    reviews = reviewInventory,
                    assessments = assessmentRows,
                    atRequests = atRequestRows,
                    checkRequests = checkRequestRows,
                    contacts = contactRows,
                    personVersions = personVersionInventory
                }));

            try
            {
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return StalePersonConflict();
            }

            return Results.Ok(result);
        });

        // Operational Demo seed only. This is deliberately not a broader permission
        // on the ordinary SSN route: case managers still own day-to-day SSN writes,
        // while this one bounded command lets an agency Admin restore the wholly
        // synthetic Demo dataset without impersonating every case manager.
        api.MapPost("/admin/demo/seed-ssns", async Task<IResult> (
            ClaimsPrincipal principal,
            ApiDbContext db,
            EnvelopeProtector protector,
            AuditTrail auditTrail,
            IOptions<SatiApiOptions> options,
            IHostEnvironment hostEnvironment,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!actor.HasAdminPermissions)
                return Results.Forbid();

            // DatabaseIdentityHostedService validates these configured expectations
            // against dbo.SatiDatabaseIdentity at startup. Requiring both values here
            // keeps this synthetic-data command absent in effect on Production even
            // if the same API binary is deployed there later.
            var isValidatedDemo =
                string.Equals(options.Value.ExpectedDatabaseName, "SatiDemo", StringComparison.Ordinal) &&
                string.Equals(options.Value.ExpectedEnvironment, "Demo", StringComparison.Ordinal);
            var isIsolatedTestHost =
                hostEnvironment.IsEnvironment("Testing") &&
                string.Equals(options.Value.ExpectedDatabaseName, "SatiApiTests", StringComparison.Ordinal) &&
                string.Equals(options.Value.ExpectedEnvironment, "Testing", StringComparison.Ordinal);
            if (!isValidatedDemo && !isIsolatedTestHost)
                return Results.NotFound();

            var people = await db.People
                .Where(person => person.AgencyId == actor.AgencyId)
                .OrderBy(person => person.Id)
                .ToListAsync(cancellationToken);
            if (people.Count is < 1 or > 9999)
                return Results.Conflict(new ApiErrorDto(
                    "demo_seed_range",
                    "The Demo Person count is outside the supported synthetic SSN seed range.",
                    string.Empty));

            for (var index = 0; index < people.Count; index++)
            {
                var syntheticSsn = $"89999{index + 1:D4}";
                await ProtectSsnAsync(
                    people[index], actor.AgencyId, syntheticSsn, protector, cancellationToken);
                auditTrail.Record(actor, AuditActions.PersonSsnUpdated, "Person", people[index].Id);
            }

            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(new CountDto(people.Count));
        });

        api.MapGet("/admin/activity", async Task<IResult> (
            int? days,
            int? take,
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!actor.HasAdminPermissions)
                return Results.Forbid();

            var windowDays = days ?? 30;
            var limit = take ?? 100;
            if (windowDays is < 1 or > 366 || limit is < 1 or > 500)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["query"] = ["Use a day window from 1 to 366 and a take value from 1 to 500."]
                });
            }

            var start = DateTime.UtcNow.AddDays(-windowDays);
            var activity = await (
                from auditEvent in db.AuditEvents.AsNoTracking()
                join user in db.Users.AsNoTracking() on auditEvent.ActorUserId equals user.Id into users
                from user in users.DefaultIfEmpty()
                where auditEvent.AgencyId == actor.AgencyId && auditEvent.OccurredAtUtc >= start
                orderby auditEvent.OccurredAtUtc descending, auditEvent.Id descending
                select new AdminActivityDto(
                    auditEvent.Id,
                    auditEvent.ActorUserId,
                    user == null ? $"User {auditEvent.ActorUserId}" : user.DisplayName,
                    auditEvent.Action,
                    auditEvent.ResourceType,
                    auditEvent.ResourceId,
                    auditEvent.OccurredAtUtc,
                    auditEvent.CorrelationId))
                .Take(limit)
                .ToListAsync(cancellationToken);
            return Results.Ok(activity);
        });

        api.MapPost("/admin/audit-export.csv", async Task<IResult> (
            AdminAuditExportRequest request,
            ClaimsPrincipal principal,
            ApiDbContext db,
            AuditTrail auditTrail,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!actor.HasAdminPermissions)
                return Results.Forbid();

            PreventSensitiveResponseCaching(httpContext);
            var start = request.FromUtc.ToUniversalTime();
            var end = request.ToUtc.ToUniversalTime();
            var reason = request.Reason?.Trim() ?? string.Empty;
            if (end < start || (end - start).TotalDays > 366 || end > DateTime.UtcNow.AddMinutes(5) ||
                reason.Length is < 10 or > 250)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["export"] = ["Use a window no longer than one year ending no later than now and provide a 10-250 character reason."]
                });
            }

            var rows = await (
                from auditEvent in db.AuditEvents.AsNoTracking()
                join user in db.Users.AsNoTracking() on auditEvent.ActorUserId equals user.Id into users
                from user in users.DefaultIfEmpty()
                where auditEvent.AgencyId == actor.AgencyId &&
                      auditEvent.OccurredAtUtc >= start && auditEvent.OccurredAtUtc <= end
                orderby auditEvent.OccurredAtUtc, auditEvent.Id
                select new AuditExportRow(
                    auditEvent.EventId,
                    auditEvent.OccurredAtUtc,
                    auditEvent.ActorUserId,
                    user == null ? $"User {auditEvent.ActorUserId}" : user.DisplayName,
                    auditEvent.Action,
                    auditEvent.ResourceType,
                    auditEvent.ResourceId,
                    auditEvent.CorrelationId))
                .Take(10_000)
                .ToListAsync(cancellationToken);

            var exportedAt = DateTime.UtcNow;
            var metadata = JsonSerializer.Serialize(new
            {
                fromUtc = start,
                toUtc = end,
                rowCount = rows.Count
            });
            auditTrail.Record(actor, AuditActions.AuditExported, "AuditEvent", metadataJson: metadata);
            await db.SaveChangesAsync(cancellationToken);

            var content = BuildAuditCsv(rows, reason, exportedAt);
            var fileName = $"sati-audit-{start:yyyyMMdd}-{end:yyyyMMdd}.csv";
            return Results.File(
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(content),
                "text/csv; charset=utf-8",
                fileName);
        });
    }

    private static void MapAuth(WebApplication app)
    {
        app.MapPost("/api/v1/auth/login", async Task<IResult> (
            LoginRequest request,
            ApiDbContext db,
            PasswordVerifier passwordVerifier,
            TokenIssuer tokenIssuer,
            LoginAttemptGuard attemptGuard,
            AuditTrail auditTrail,
            HttpContext context,
            ILogger<Program> logger,
            CancellationToken cancellationToken) =>
        {
            var username = request.Username?.Trim() ?? string.Empty;
            if (username.Length is < 1 or > 50 || request.Password is null || request.Password.Length is < 1 or > 1024)
                return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["credentials"] = ["A valid username and password are required."] });

            if (!attemptGuard.TryAcquire(username, out var retryAfter))
            {
                var retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
                context.Response.Headers.RetryAfter = retryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
                return Results.Json(
                    new ApiErrorDto(
                        "rate_limited",
                        $"Too many sign-in attempts for this username. Try again in about {retryAfterSeconds} seconds.",
                        context.TraceIdentifier),
                    statusCode: StatusCodes.Status429TooManyRequests);
            }

            var user = await db.Users.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Username == username, cancellationToken);

            // Spend the same key-derivation work whether or not the account
            // exists. Skipping it for an unknown username turns sign-in into a
            // username oracle: a missing account answers in microseconds while a
            // wrong password costs 100,000 PBKDF2 iterations.
            var authenticated = user is null
                ? passwordVerifier.VerifyMissingUser(request.Password)
                : passwordVerifier.Verify(request.Password, user.PasswordHash, user.Salt);

            // VerifyMissingUser never returns true, so a null user cannot reach
            // past here; the explicit null check states that for the compiler and
            // fails closed if that ever changes.
            if (!authenticated || user is null || !user.IsEnabled || user.SecurityVersion <= 0 ||
                !UserPermissionRules.IsSupported(user.Permissions) ||
                !await db.Users.AsNoTracking().AnyAsync(current => current.Id == user.Id &&
                    current.IsEnabled && current.SecurityVersion == user.SecurityVersion &&
                    current.PasswordHash == user.PasswordHash && current.Salt == user.Salt &&
                    current.AgencyId == user.AgencyId && current.Role == user.Role &&
                    current.Permissions == user.Permissions, cancellationToken))
            {
                logger.LogWarning("Sati authentication failed from {RemoteAddress}.", context.Connection.RemoteIpAddress);
                return TypedResults.Unauthorized();
            }

            attemptGuard.Reset(username);
            var actor = new Actor(
                user.Id, user.AgencyId, user.Role, user.DisplayName, user.Permissions, user.SecurityVersion);
            auditTrail.Record(actor, AuditActions.AuthenticationSucceeded, "User", user.Id);
            await db.SaveChangesAsync(cancellationToken);
            var instanceId = await db.DatabaseIdentities.AsNoTracking()
                .Where(item => item.Id == 1 && item.EnvironmentName == "Demo")
                .Select(item => item.InstanceId)
                .SingleAsync(cancellationToken);
            var issued = tokenIssuer.Issue(user, instanceId);
            logger.LogInformation("Sati authentication succeeded for user {UserId} in agency {AgencyId}.", user.Id, user.AgencyId);
            return TypedResults.Ok(new LoginResponse(issued.Token, issued.ExpiresAtUtc, ContractMapper.ToProfile(user)));
        })
        .RequireRateLimiting("login")
        .AllowAnonymous();
    }

    private static void MapProfile(RouteGroupBuilder api)
    {
        api.MapPost("/auth/renew", async Task<IResult> (
            ClaimsPrincipal principal,
            ApiDbContext db,
            TokenIssuer tokenIssuer,
            IOptions<ApiAuthenticationOptions> authenticationOptions,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            var authenticatedAtValue = principal.FindFirst(TokenIssuer.AuthenticatedAtClaim)?.Value;
            if (!long.TryParse(authenticatedAtValue, out var authenticatedAtSeconds))
                return TypedResults.Unauthorized();

            DateTimeOffset authenticatedAt;
            try { authenticatedAt = DateTimeOffset.FromUnixTimeSeconds(authenticatedAtSeconds); }
            catch (ArgumentOutOfRangeException) { return TypedResults.Unauthorized(); }
            var now = DateTimeOffset.UtcNow;
            if (authenticatedAt > now.AddSeconds(30) ||
                now - authenticatedAt > TimeSpan.FromMinutes(authenticationOptions.Value.MaxSessionMinutes))
            {
                return TypedResults.Unauthorized();
            }

            var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(
                x => x.Id == actor.UserId && x.AgencyId == actor.AgencyId &&
                    x.Role == actor.Role && x.IsEnabled && x.SecurityVersion == actor.SecurityVersion,
                cancellationToken);
            if (user is null)
                return TypedResults.Unauthorized();

            var instanceId = await db.DatabaseIdentities.AsNoTracking()
                .Where(item => item.Id == 1 && item.EnvironmentName == "Demo")
                .Select(item => item.InstanceId)
                .SingleAsync(cancellationToken);
            var issued = tokenIssuer.Issue(user, instanceId, authenticatedAt);
            return TypedResults.Ok(new SessionRenewalResponse(issued.Token, issued.ExpiresAtUtc));
        });

        api.MapGet("/me", async Task<Results<Ok<UserProfileDto>, NotFound>> (
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(
                x => x.Id == actor.UserId && x.AgencyId == actor.AgencyId,
                cancellationToken);
            return user is null ? TypedResults.NotFound() : TypedResults.Ok(ContractMapper.ToProfile(user));
        });

        api.MapGet("/users/switchable", async (
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            var users = await db.Users.AsNoTracking()
                .Where(x => x.AgencyId == actor.AgencyId && x.Role != "PlatformOperator")
                .OrderBy(x => x.DisplayName)
                .ThenBy(x => x.Username)
                .ToListAsync(cancellationToken);
            return users.Select(ContractMapper.ToProfile).ToList();
        });
    }

    private static void MapUsers(RouteGroupBuilder api)
    {
        api.MapPost("/users", async Task<IResult> (
            CreateUserRequest request, ClaimsPrincipal principal, ApiDbContext db,
            PasswordVerifier passwordVerifier, AuditTrail auditTrail, CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!actor.HasSupervisorPermissions && !actor.HasAdminPermissions) return Results.Forbid();
            var profile = new SaveUserRequest(
                request.Username, request.DisplayName, request.Permissions, request.SupervisorId,
                request.AgencyId, request.Email, request.Phone);
            var errors = await ValidateUserRequestAsync(db, actor, profile, null, cancellationToken);
            if (!ValidPassword(request.InitialPassword))
                errors["password"] = ["The initial password must be between 8 and 128 characters."];
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            var credential = passwordVerifier.Hash(request.InitialPassword);
            var user = new ServerUser
            {
                Username = request.Username.Trim(), DisplayName = request.DisplayName.Trim(),
                Role = UserPermissionRules.LegacyLabel(request.Permissions),
                Permissions = request.Permissions,
                SupervisorId = request.SupervisorId, AgencyId = actor.AgencyId,
                Email = Normalize(request.Email), Phone = Normalize(request.Phone),
                PasswordHash = credential.Hash, Salt = credential.Salt
            };
            db.Users.Add(user);
            auditTrail.Record(actor, AuditActions.UserCreated, "User");
            await db.SaveChangesAsync(cancellationToken);
            return Results.Created($"/api/v1/users/{user.Id}", ContractMapper.ToProfile(user));
        });

        api.MapPut("/users/{userId:int}", async Task<IResult> (
            int userId, SaveUserRequest request, ClaimsPrincipal principal, ApiDbContext db,
            AuditTrail auditTrail, CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!actor.HasSupervisorPermissions && !actor.HasAdminPermissions) return Results.Forbid();
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            if (!await TenantAccess.IsCurrentActorAsync(db, actor, cancellationToken)) return Results.Unauthorized();
            var user = await db.Users.SingleOrDefaultAsync(x => x.Id == userId && x.AgencyId == actor.AgencyId, cancellationToken);
            if (user is null) return Results.NotFound();
            if (user.Role == "PlatformOperator") return Results.NotFound();
            if (UserManagementRules.DescribeTargetRefusal(actor.ToAgencyActor(), user.Permissions,
                user.SupervisorId, user.AgencyId, user.Role) is not null) return Results.Forbid();
            var errors = await ValidateUserRequestAsync(db, actor, request, userId, cancellationToken);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            user.Username = request.Username.Trim();
            user.DisplayName = request.DisplayName.Trim();
            user.Permissions = request.Permissions;
            user.Role = UserPermissionRules.LegacyLabel(request.Permissions);
            user.SupervisorId = request.SupervisorId;
            user.Email = Normalize(request.Email);
            user.Phone = Normalize(request.Phone);
            auditTrail.Record(actor, AuditActions.UserUpdated, "User", userId);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException) { return AccountStateConflict(); }
            return Results.Ok(ContractMapper.ToProfile(user));
        });

        api.MapPut("/users/{userId:int}/password", async Task<IResult> (
            int userId, ResetPasswordRequest request, ClaimsPrincipal principal, ApiDbContext db,
            PasswordVerifier passwordVerifier, AuditTrail auditTrail, CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!actor.HasSupervisorPermissions && !actor.HasAdminPermissions) return Results.Forbid();
            if (!ValidPassword(request.NewPassword)) return Results.ValidationProblem(
                new Dictionary<string, string[]> { ["password"] = ["The new password must be between 8 and 128 characters."] });
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            if (!await TenantAccess.IsCurrentActorAsync(db, actor, cancellationToken)) return Results.Unauthorized();
            var user = await db.Users.SingleOrDefaultAsync(x => x.Id == userId && x.AgencyId == actor.AgencyId, cancellationToken);
            if (user is null) return Results.NotFound();
            if (user.Role == "PlatformOperator") return Results.NotFound();
            if (UserManagementRules.DescribeTargetRefusal(actor.ToAgencyActor(), user.Permissions,
                user.SupervisorId, user.AgencyId, user.Role) is not null) return Results.Forbid();
            var credential = passwordVerifier.Hash(request.NewPassword);
            if (!TryAdvanceSecurityVersion(user)) return AccountStateConflict();
            user.PasswordHash = credential.Hash;
            user.Salt = credential.Salt;
            auditTrail.Record(actor, AuditActions.UserPasswordReset, "User", userId);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException) { return AccountStateConflict(); }
            return Results.NoContent();
        });

        api.MapPut("/users/me/password", async Task<IResult> (
            ChangePasswordRequest request, ClaimsPrincipal principal, ApiDbContext db,
            PasswordVerifier passwordVerifier, AuditTrail auditTrail, CancellationToken cancellationToken) =>
        {
            if (!ValidPassword(request.NewPassword)) return Results.ValidationProblem(
                new Dictionary<string, string[]> { ["password"] = ["The new password must be between 8 and 128 characters."] });
            var actor = Actor.From(principal);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            if (!await TenantAccess.IsCurrentActorAsync(db, actor, cancellationToken)) return Results.Unauthorized();
            var user = await db.Users.SingleOrDefaultAsync(x => x.Id == actor.UserId && x.AgencyId == actor.AgencyId &&
                x.IsEnabled && x.SecurityVersion == actor.SecurityVersion, cancellationToken);
            if (user is null) return Results.NotFound();
            if (!passwordVerifier.Verify(request.CurrentPassword, user.PasswordHash, user.Salt))
                return Results.BadRequest(new ApiErrorDto(
                    "invalid_current_password", "The current password is incorrect.", string.Empty));
            var credential = passwordVerifier.Hash(request.NewPassword);
            if (!TryAdvanceSecurityVersion(user)) return AccountStateConflict();
            user.PasswordHash = credential.Hash;
            user.Salt = credential.Salt;
            auditTrail.Record(actor, AuditActions.UserPasswordChanged, "User", actor.UserId);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException) { return AccountStateConflict(); }
            return Results.NoContent();
        });
    }

    private static void MapSupervisor(RouteGroupBuilder api)
    {
        api.MapGet("/supervisor/supervisees", async (
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!actor.HasSupervisorPermissions)
                return Results.Forbid();

            var supervisees = await db.Users.AsNoTracking()
                .Where(x => x.SupervisorId == actor.UserId &&
                            x.AgencyId == actor.AgencyId &&
                            (x.Permissions & UserPermissions.CaseManagement) != 0)
                .OrderBy(x => x.DisplayName)
                .ToListAsync(cancellationToken);
            return Results.Ok(supervisees.Select(ContractMapper.ToProfile).ToList());
        });

        api.MapGet("/supervisor/notes/page", async Task<IResult> (
            int? afterId,
            int? throughId,
            int? userId,
            int? personId,
            DateTime? fromDate,
            DateTime? toDate,
            string? searchTerm,
            ClaimsPrincipal principal,
            ApiDbContext db,
            ApiClock clock,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!actor.HasSupervisorPermissions)
                return Results.Forbid();

            if (userId is int selectedUser &&
                !await TenantAccess.CanAccessUserAsync(db, actor, selectedUser, cancellationToken))
                return Results.Forbid();
            if (fromDate?.Date > toDate?.Date)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["dateRange"] = ["The start date must be on or before the end date."]
                });
            var term = string.IsNullOrWhiteSpace(searchTerm)
                ? null
                : searchTerm.Trim()[..Math.Min(searchTerm.Trim().Length, 200)];
            var startDate = fromDate?.Date;
            var endDate = toDate is DateTime selectedEnd && selectedEnd.Date < DateTime.MaxValue.Date
                ? selectedEnd.Date.AddDays(1)
                : (DateTime?)null;
            var canReviewAgency = actor.HasAgencyWideSupervisionPermissions;
            var caseManagerIds = await db.Users.AsNoTracking()
                .Where(user => user.AgencyId == actor.AgencyId &&
                               (user.Permissions & UserPermissions.CaseManagement) != 0 &&
                               (canReviewAgency || user.SupervisorId == actor.UserId))
                .Select(user => user.Id)
                .ToListAsync(cancellationToken);
            if (personId is int selectedPerson &&
                !await db.People.AsNoTracking().AnyAsync(person =>
                    person.Id == selectedPerson && person.AgencyId == actor.AgencyId &&
                    caseManagerIds.Contains(person.UserId), cancellationToken))
                return Results.Forbid();

            var query = (from note in db.Notes.AsNoTracking()
                              join person in db.People.AsNoTracking() on note.PersonId equals person.Id
                              join owner in db.Users.AsNoTracking() on person.UserId equals owner.Id
                              where (note.Status == NoteWorkflow.Logged ||
                                     (note.Status == NoteWorkflow.Approved &&
                                      (note.FormId != null || note.ReleaseObligationId != null) &&
                                      !db.ClaimLines.Any(line => line.NoteId == note.Id))) &&
                                    note.AgencyId == actor.AgencyId &&
                                    person.AgencyId == actor.AgencyId &&
                                    caseManagerIds.Contains(person.UserId) &&
                                    (!userId.HasValue || person.UserId == userId.Value) &&
                                    (!personId.HasValue || person.Id == personId.Value) &&
                                    (!startDate.HasValue || note.EventDate >= startDate.Value) &&
                                    (!endDate.HasValue || note.EventDate < endDate.Value) &&
                                    (term == null || note.Narrative.Contains(term) ||
                                        (person.FirstName ?? "").Contains(term) ||
                                        (person.LastName ?? "").Contains(term) ||
                                        owner.DisplayName.Contains(term))
                              select new { Note = note, Person = person });
            var ceiling = throughId ?? await query.Select(row => (int?)row.Note.Id).MaxAsync(cancellationToken) ?? 0;
            // This is a work inbox, so the most recently submitted note must be on
            // the first page. NextAfterId is an opaque cursor that walks backward.
            var cursor = afterId ?? 0;
            var rows = await query.Where(row => row.Note.Id <= ceiling && (cursor <= 0 || row.Note.Id < cursor))
                .OrderByDescending(row => row.Note.Id).Take(NoteReviewRules.PageSize + 1).ToListAsync(cancellationToken);
            var more = rows.Count > NoteReviewRules.PageSize;
            rows = rows.Take(NoteReviewRules.PageSize).ToList();
            var personIds = rows.Select(row => row.Person.Id).Distinct().ToList();
            var formsByPerson = (await db.Forms.AsNoTracking()
                    .Include(form => form.Attestations)
                    .Where(form => personIds.Contains(form.PersonId))
                    .ToListAsync(cancellationToken))
                .GroupBy(form => form.PersonId)
                .ToDictionary(group => group.Key, group => (IReadOnlyList<ServerForm>)group.ToList());
            var releasesByPerson = await LoadReleaseBillingRowsByPersonAsync(
                db, personIds, cancellationToken);
            var providerLinksByPerson = await LoadReleaseProviderLinksByPersonAsync(
                db, actor.AgencyId, personIds, cancellationToken);
            await PopulateContactHistoryAsync(
                db, actor.AgencyId, rows.Select(row => row.Person), cancellationToken);

            var compliancePolicy = await LoadBillingCompliancePolicyContextAsync(
                db, actor.AgencyId, cancellationToken);
            var result = rows
                .Select(row => new
                {
                    Row = row,
                    Compliance = EvaluateSupervisorNoteCompliance(
                        row.Note,
                        row.Person,
                        formsByPerson.GetValueOrDefault(row.Person.Id) ?? [],
                        releasesByPerson.GetValueOrDefault(row.Person.Id) ?? [],
                        compliancePolicy,
                        providerLinksByPerson.GetValueOrDefault(row.Person.Id) ?? [])
                })
                .Select(row => ContractMapper.ToNote(
                    row.Row.Note,
                    row.Row.Person,
                    row.Compliance.Reasons,
                    row.Compliance.Blockers))
                .ToList();
            return Results.Ok(new NoteReviewPage<NoteDto>(result, more ? rows[^1].Note.Id : null, ceiling));
        });

        api.MapGet("/supervisor/notes/filters", async Task<IResult> (
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!actor.HasSupervisorPermissions)
                return Results.Forbid();

            var canReviewAgency = actor.HasAgencyWideSupervisionPermissions;
            var users = await db.Users.AsNoTracking()
                .Where(user => user.AgencyId == actor.AgencyId &&
                               (user.Permissions & UserPermissions.CaseManagement) != 0 &&
                               (canReviewAgency || user.SupervisorId == actor.UserId))
                .OrderBy(user => user.DisplayName)
                .Select(user => new NoteReviewCaseManagerOption(user.Id, user.DisplayName))
                .ToListAsync(cancellationToken);
            var userIds = users.Select(user => user.UserId).ToList();
            var clients = await db.People.AsNoTracking()
                .Where(person => person.AgencyId == actor.AgencyId && userIds.Contains(person.UserId))
                .Select(person => new
                {
                    PersonId = person.Id,
                    person.UserId,
                    person.FirstName,
                    person.LastName
                }).ToListAsync(cancellationToken);
            return Results.Ok(new NoteReviewFilterOptions(
                users,
                clients.Select(row => new NoteReviewClientOption(
                        row.PersonId, row.UserId, $"{row.FirstName} {row.LastName}".Trim()))
                    .Distinct().OrderBy(option => option.DisplayName).ToList()));
        });

        api.MapGet("/supervisor/notes", async Task<IResult> (
            bool compliant,
            bool allSupervisees,
            ClaimsPrincipal principal,
            ApiDbContext db,
            ApiClock clock,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!actor.HasSupervisorPermissions)
                return Results.Forbid();

            var canReviewAgency = actor.HasAgencyWideSupervisionPermissions;
            var caseManagerIds = await db.Users.AsNoTracking()
                .Where(user => user.AgencyId == actor.AgencyId &&
                               (user.Permissions & UserPermissions.CaseManagement) != 0 &&
                               (canReviewAgency || user.SupervisorId == actor.UserId))
                .Select(user => user.Id)
                .ToListAsync(cancellationToken);

            var rows = await (from note in db.Notes.AsNoTracking()
                              join person in db.People.AsNoTracking() on note.PersonId equals person.Id
                              where note.Status == NoteWorkflow.Logged &&
                                    note.AgencyId == actor.AgencyId &&
                                    person.AgencyId == actor.AgencyId &&
                                    caseManagerIds.Contains(person.UserId)
                              orderby note.EventDate
                              select new ReviewableNote(note, person)).ToListAsync(cancellationToken);
            var personIds = rows.Select(row => row.Person.Id).Distinct().ToList();
            var formsByPerson = (await db.Forms.AsNoTracking()
                    .Include(form => form.Attestations)
                    .Where(form => personIds.Contains(form.PersonId))
                    .ToListAsync(cancellationToken))
                .GroupBy(form => form.PersonId)
                .ToDictionary(group => group.Key, group => (IReadOnlyList<ServerForm>)group.ToList());
            var releasesByPerson = await LoadReleaseBillingRowsByPersonAsync(
                db, personIds, cancellationToken);
            var providerLinksByPerson = await LoadReleaseProviderLinksByPersonAsync(
                db, actor.AgencyId, personIds, cancellationToken);
            await PopulateContactHistoryAsync(
                db, actor.AgencyId, rows.Select(row => row.Person), cancellationToken);

            var compliancePolicy = await LoadBillingCompliancePolicyContextAsync(
                db, actor.AgencyId, cancellationToken);
            var result = rows
                .Select(row => new
                {
                    Row = row,
                    Compliance = EvaluateSupervisorNoteCompliance(
                        row.Note,
                        row.Person,
                        formsByPerson.GetValueOrDefault(row.Person.Id) ?? [],
                        releasesByPerson.GetValueOrDefault(row.Person.Id) ?? [],
                        compliancePolicy,
                        providerLinksByPerson.GetValueOrDefault(row.Person.Id) ?? [])
                })
                .Where(row => row.Compliance.Passed == compliant)
                .Select(row => ContractMapper.ToNote(
                    row.Row.Note,
                    row.Row.Person,
                    row.Compliance.Reasons,
                    row.Compliance.Blockers))
                .ToList();
            return Results.Ok(result);
        });

        api.MapPost("/supervisor/notes/{noteId:int}/approve", async Task<IResult> (
            int noteId,
            SupervisorNoteActionRequest request,
            ClaimsPrincipal principal,
            ApiDbContext db,
            AuditTrail auditTrail,
            ApiClock clock,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            var row = await LoadReviewableNoteAsync(db, actor, noteId, cancellationToken);
            if (row is null)
                return actor.HasSupervisorPermissions ? Results.NotFound() : Results.Forbid();
            var scheduleOwnerId = row.Person.UserId;
            db.ChangeTracker.Clear();
            await using var scheduleWrite = await ServiceTimeWriteScope.BeginAsync(db,
                actor.AgencyId, scheduleOwnerId, cancellationToken);
            if (!await TenantAccess.IsCurrentActorAsync(db, actor, cancellationToken))
                return Results.Unauthorized();
            row = await LoadReviewableNoteAsync(db, actor, noteId, cancellationToken);
            if (row is null || row.Person.UserId != scheduleOwnerId)
                return StaleNoteConflict();
            if (request.ExpectedRevision != row.Note.Revision)
                return StaleNoteConflict();
            if (!NoteWorkflow.CanSupervisorTransition(row.Note.Status, NoteWorkflow.Approved))
                return Results.Conflict(new ApiErrorDto("invalid_note_status", "Only logged notes can be approved.", string.Empty));

            var reviewTimeConflict = await FindReviewServiceTimeProblemAsync(db, row, actor.AgencyId, cancellationToken);
            if (reviewTimeConflict is not null)
                return reviewTimeConflict;

            if (request.MaximumUnits is int limit)
            {
                if (!NoteReviewRules.Eligible(limit, row.Note.Status,
                    ContractMapper.NoteTypeName(row.Note.NoteType), row.Note.Narrative,
                    row.Note.EventDate, row.Note.Minutes, row.Note.StartTime, clock.Today))
                    return Results.Conflict(new ApiErrorDto("batch_ineligible",
                        "This note is not eligible for automatic approval.", string.Empty));
            }

            row.Note.Status = 6;
            row.Note.ApprovedById = actor.UserId;
            row.Note.ApprovedAt = DateTime.UtcNow;
            row.Note.Revision++;
            auditTrail.Record(actor, AuditActions.NoteApproved, "Note", noteId,
                request.MaximumUnits is int threshold ? JsonSerializer.Serialize(new { maximumUnits = threshold, batch = true }) : "{}");
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                await scheduleWrite.CommitAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return StaleNoteConflict();
            }
            return Results.Ok(ContractMapper.ToNote(row.Note, row.Person));
        });

        api.MapPost("/supervisor/notes/{noteId:int}/approve-override", async Task<IResult> (
            int noteId,
            SupervisorNoteActionRequest request,
            ClaimsPrincipal principal,
            ApiDbContext db,
            AuditTrail auditTrail,
            ApiClock clock,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            var row = await LoadReviewableNoteAsync(db, actor, noteId, cancellationToken);
            if (row is null)
                return actor.HasSupervisorPermissions ? Results.NotFound() : Results.Forbid();
            var scheduleOwnerId = row.Person.UserId;
            db.ChangeTracker.Clear();
            await using var scheduleWrite = await ServiceTimeWriteScope.BeginAsync(db,
                actor.AgencyId, scheduleOwnerId, cancellationToken);
            if (!await TenantAccess.IsCurrentActorAsync(db, actor, cancellationToken))
                return Results.Unauthorized();
            row = await LoadReviewableNoteAsync(db, actor, noteId, cancellationToken);
            if (row is null || row.Person.UserId != scheduleOwnerId)
                return StaleNoteConflict();
            if (request.ExpectedRevision != row.Note.Revision)
                return StaleNoteConflict();
            if (!NoteWorkflow.CanSupervisorTransition(row.Note.Status, NoteWorkflow.Approved))
                return Results.Conflict(new ApiErrorDto("invalid_note_status", "Only logged notes can be approved.", string.Empty));

            var forms = await db.Forms.AsNoTracking()
                .Where(form => form.PersonId == row.Person.Id)
                .ToListAsync(cancellationToken);
            var releaseRows = (await LoadReleaseBillingRowsByPersonAsync(
                db, [row.Person.Id], cancellationToken))
                .GetValueOrDefault(row.Person.Id) ?? [];
            var providerLinks = (await LoadReleaseProviderLinksByPersonAsync(
                    db, actor.AgencyId, [row.Person.Id], cancellationToken))
                .GetValueOrDefault(row.Person.Id) ?? [];
            var compliancePolicy = await LoadBillingCompliancePolicyContextAsync(
                db, actor.AgencyId, cancellationToken);
            await PopulateContactHistoryAsync(
                db, actor.AgencyId, [row.Person], cancellationToken);
            var compliance = EvaluateNoteCompliance(
                row.Note, row.Person, forms, releaseRows, compliancePolicy, providerLinks);
            var decision = BillingComplianceExceptionRules.Validate(
                compliance.Blockers ?? [],
                request.BlockingObligationIds,
                request.Reason,
                request.AttestationConfirmed);
            if (!decision.Accepted)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["complianceException"] = decision.Errors.ToArray()
                });
            }

            var reviewTimeConflict = await FindReviewServiceTimeProblemAsync(db, row, actor.AgencyId, cancellationToken);
            if (reviewTimeConflict is not null)
                return reviewTimeConflict;

            var now = clock.UtcNow.UtcDateTime;
            row.Note.Status = 6;
            row.Note.ApprovedById = actor.UserId;
            row.Note.ApprovedAt = now;
            row.Note.ComplianceOverride = true;
            row.Note.OverrideReason = request.Reason!.Trim();
            row.Note.OverrideApprovedById = actor.UserId;
            row.Note.OverrideApprovedAt = now;
            row.Note.OverrideAttestationConfirmed = true;
            row.Note.OverrideObligationIdsJson = JsonSerializer.Serialize(
                decision.SelectedObligationIds);
            row.Note.Revision++;
            auditTrail.Record(actor, AuditActions.NoteApprovalOverridden, "Note", noteId,
                JsonSerializer.Serialize(new
                {
                    attestationConfirmed = true,
                    obligationIds = decision.SelectedObligationIds
                }));
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                await scheduleWrite.CommitAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return StaleNoteConflict();
            }
            return Results.Ok(ContractMapper.ToNote(row.Note, row.Person));
        });

        api.MapPost("/supervisor/notes/{noteId:int}/return", async Task<IResult> (
            int noteId,
            SupervisorNoteActionRequest request,
            ClaimsPrincipal principal,
            ApiDbContext db,
            AuditTrail auditTrail,
            CancellationToken cancellationToken) =>
        {
            var reason = request.Reason?.Trim() ?? string.Empty;
            if (reason.Length is < 1 or > 4_000)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["reason"] = ["A return reason is required and must not exceed 4,000 characters."]
                });
            }

            var actor = Actor.From(principal);
            var row = await LoadReviewableNoteAsync(db, actor, noteId, cancellationToken);
            if (row is null)
                return actor.HasSupervisorPermissions ? Results.NotFound() : Results.Forbid();
            if (request.ExpectedRevision != row.Note.Revision)
                return StaleNoteConflict();
            var hasClaimLine = await db.ClaimLines.AsNoTracking().AnyAsync(
                line => line.NoteId == noteId, cancellationToken);
            if (!NoteWorkflow.CanSupervisorReturnForCorrection(row.Note.Status, hasClaimLine,
                    row.Note.FormId is not null || row.Note.ReleaseObligationId is not null))
                return Results.Conflict(new ApiErrorDto("invalid_note_status",
                    "Only logged or unclaimed approved notes can be returned. A claimed note needs Admin correction.",
                    string.Empty));

            row.Note.Status = 7;
            row.Note.ReturnedById = actor.UserId;
            row.Note.ReturnReason = reason;
            row.Note.ReturnedAt = DateTime.UtcNow;
            row.Note.Revision++;
            auditTrail.Record(actor, AuditActions.NoteReturned, "Note", noteId);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return StaleNoteConflict();
            }
            return Results.Ok(ContractMapper.ToNote(row.Note, row.Person));
        });
    }

    private static void MapCaseload(RouteGroupBuilder api)
    {
        api.MapGet("/caseload", async Task<IResult> (
            int? userId,
            ClaimsPrincipal principal,
            ApiDbContext db,
            AuditTrail audit,
            ApiClock clock,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            var targetUserId = userId ?? actor.UserId;
            if (!await TenantAccess.CanAccessUserAsync(db, actor, targetUserId, cancellationToken))
                return Results.Forbid();

            var people = await db.People.AsNoTracking()
                // 0 == Sati.PersonStatus.Active. Archived people are excluded from the caseload
                // load path entirely — see HANDOFF_CLIENT_DELETION_POLICY.md's archive semantics.
                .Where(x => x.UserId == targetUserId && x.AgencyId == actor.AgencyId && x.Status == 0)
                .OrderBy(x => x.LastName)
                .ThenBy(x => x.FirstName)
                .ToListAsync(cancellationToken);
            var ids = people.Select(x => x.Id).ToList();
            var settings = await GetOrCreateSettingsAsync(
                db, actor.AgencyId, cancellationToken);

            // The API is the authoritative writer for distributed clients. Keep
            // current and next annual obligations supplied here just as the
            // transitional Local Production service does, but under a serializable
            // transaction and the database uniqueness constraints so concurrent
            // caseload loads converge rather than duplicating a cycle.
            var generationStrategy = db.Database.CreateExecutionStrategy();
            await generationStrategy.ExecuteAsync(async () =>
            {
                await using var transaction = await db.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable, cancellationToken);
                var trackedForms = await db.Forms
                    .Where(x => ids.Contains(x.PersonId))
                    .ToListAsync(cancellationToken);
                var formsByIdentity = trackedForms
                    .Select(form => (form.PersonId, form.Type, Target: form.TargetEffectiveDate.Date))
                    .ToHashSet();
                var generatedByPerson = new Dictionary<int, List<string>>();

                foreach (var person in people.Where(person => person.EffectiveDate is not null))
                {
                    foreach (var target in ComplianceScheduleRules.TargetEffectiveDatesThroughNext(
                                 person.EffectiveDate!.Value, clock.Today))
                    {
                        foreach (var typeName in PersonSaveRules.FormTypes)
                        {
                            if (!formsByIdentity.Add((person.Id, typeName, target.Date)))
                                continue;

                            db.Forms.Add(new ServerForm
                            {
                                PersonId = person.Id,
                                Type = typeName,
                                TargetEffectiveDate = target.Date,
                                DueDate = ComplianceScheduleRules.DueDate(
                                    typeName, target, ToComplianceSchedule(settings))
                            });
                            if (!generatedByPerson.TryGetValue(person.Id, out var generated))
                            {
                                generated = [];
                                generatedByPerson.Add(person.Id, generated);
                            }
                            generated.Add($"{typeName}:{target:yyyy-MM-dd}");
                        }
                    }

                    await ReconcileCurrentReleaseCyclesAsync(
                        db, audit, actor, person, clock, "caseload-load", cancellationToken);
                }

                foreach (var pair in generatedByPerson)
                {
                    audit.Record(
                        actor,
                        AuditActions.ComplianceObligationsGenerated,
                        "Person",
                        pair.Key,
                        JsonSerializer.Serialize(new
                        {
                            obligations = pair.Value.Order(StringComparer.Ordinal).ToArray(),
                            source = "caseload-load"
                        }));
                }

                if (db.ChangeTracker.HasChanges())
                    await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            });

            var forms = await db.Forms.AsNoTracking().Where(x => ids.Contains(x.PersonId)).ToListAsync(cancellationToken);
            // Caseload consumers need only the bounded scheduled-work window here.
            // Monthly-contact facts travel separately below; narratives and visit
            // documentation never cross SQL for workspace preparation.
            var noteRows = await CaseloadNoteSummaries(
                    db, ids, actor.AgencyId, clock.Today)
                .ToListAsync(cancellationToken);
            var contactFacts = await LoadContactFactsByPersonAsync(
                db, actor.AgencyId, ids, cancellationToken);
            var releases = await LoadReleaseBillingRowsByPersonAsync(
                db, ids, cancellationToken);
            var formsByPerson = forms.GroupBy(x => x.PersonId).ToDictionary(x => x.Key, x => (IReadOnlyList<ServerForm>)x.ToList());
            var notesByPerson = noteRows
                .GroupBy(x => x.PersonId)
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlyList<ServerNote>)group.Select(x => new ServerNote
                    {
                        Id = x.Id,
                        PersonId = x.PersonId,
                        Status = x.Status,
                        EventDate = x.EventDate,
                        NoteType = x.NoteType,
                        Activities = x.Activities,
                        FormType = x.FormType,
                        ReleaseObligationId = x.ReleaseObligationId
                    }).ToList());

            return Results.Ok(people.Select(person => ContractMapper.ToPerson(
                person,
                formsByPerson.GetValueOrDefault(person.Id) ?? [],
                notesByPerson.GetValueOrDefault(person.Id) ?? [],
                releases.GetValueOrDefault(person.Id)?.Select(item => item.ToComplianceFact()).ToArray()
                    ?? [],
                contactFacts[person.Id].ToArray())).ToList());
        });

        api.MapGet("/people/{personId:int}/journal", async Task<Results<Ok<string?>, NotFound>> (
            int personId,
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            var journal = await TenantAccess.OwnedPeople(db, actor).AsNoTracking()
                .Where(x => x.Id == personId)
                .Select(x => new { x.Journal })
                .SingleOrDefaultAsync(cancellationToken);
            return journal is null ? TypedResults.NotFound() : TypedResults.Ok<string?>(journal.Journal);
        });

        api.MapPut("/people/{personId:int}/journal", async Task<IResult> (
            int personId,
            SaveJournalRequest request,
            ClaimsPrincipal principal,
            ApiDbContext db,
            PersonLifecycle lifecycle,
            AuditTrail auditTrail,
            ApiClock clock,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            var person = await TenantAccess.OwnedPeople(db, actor).SingleOrDefaultAsync(
                x => x.Id == personId,
                cancellationToken);
            if (person is null)
                return Results.NotFound();

            var before = PersonLifecycle.Capture(person);
            await lifecycle.EnsureBaselineAsync(person, cancellationToken);
            person.Journal = request.Journal;
            if (!lifecycle.RecordChanged(actor, person, before, "JournalUpdated"))
                return Results.NoContent();

            auditTrail.Record(actor, AuditActions.PersonJournalUpdated, "Person", personId);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return StalePersonConflict();
            }
            return Results.NoContent();
        });

        // A journal ENTRY is prepended by the server, not composed by the client.
        // The PUT above replaces the whole journal, so a client that read the
        // journal, prepended locally, and wrote it back would erase anything a
        // concurrent session typed in between. The caller sends only the text:
        // the stamp comes from the agency clock so the record cannot claim a
        // moment the caller invented, and JournalEntry owns the placement so the
        // desktop's transitional local path cannot order entries differently.
        api.MapPost("/people/{personId:int}/journal/entries", async Task<IResult> (
            int personId,
            AddJournalReminderRequest request,
            ClaimsPrincipal principal,
            ApiDbContext db,
            PersonLifecycle lifecycle,
            AuditTrail auditTrail,
            ApiClock clock,
            CancellationToken cancellationToken) =>
        {
            var text = request.Text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text))
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["Text"] = ["A reminder needs text."]
                });
            if (text.Length > JournalEntry.MaxTextLength)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["Text"] = [$"A reminder is limited to {JournalEntry.MaxTextLength} characters."]
                });

            // Same scope gate as the journal PUT: the person must be on this
            // caller's caseload AND in this caller's agency.
            var actor = Actor.From(principal);
            var person = await TenantAccess.OwnedPeople(db, actor).SingleOrDefaultAsync(
                x => x.Id == personId,
                cancellationToken);
            if (person is null)
                return Results.NotFound();

            var before = PersonLifecycle.Capture(person);
            await lifecycle.EnsureBaselineAsync(person, cancellationToken);
            person.Journal = JournalEntry.PrependReminder(person.Journal, clock.Now, text);
            lifecycle.RecordChanged(actor, person, before, "JournalReminderAdded");
            auditTrail.Record(actor, AuditActions.PersonJournalReminderAdded, "Person", personId);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return StalePersonConflict();
            }

            // The updated journal goes back so the caller shows what was actually
            // written rather than its own guess at it.
            return Results.Ok<string?>(person.Journal);
        });
    }

    private static void MapPeople(RouteGroupBuilder api)
    {
        api.MapPost("/people", async Task<IResult> (
            SavePersonRequest request,
            ClaimsPrincipal principal,
            ApiDbContext db,
            PersonLifecycle lifecycle,
            AuditTrail auditTrail,
            ApiClock clock,
            CancellationToken cancellationToken) =>
        {
            var validation = ValidatePerson(request, requireNewForms: request.EffectiveDate.HasValue);
            if (validation.Count > 0)
                return Results.ValidationProblem(validation);

            var actor = Actor.From(principal);
            if (!actor.HasCaseManagerPermissions)
                return Results.Forbid();
            if (request.IsTestData && !actor.HasAdminPermissions)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["isTestData"] = ["Only a current Admin can create a consumer marked as Test."]
                });
            }
            ContractMapper.TryParseGender(request.Gender, out var gender);
            ContractMapper.TryParseWaiver(request.Waiver, out var waiver);
            var person = new ServerPerson
            {
                UserId = actor.UserId,
                AgencyId = actor.AgencyId,
                IsTestData = request.IsTestData,
                CreatedAtUtc = DateTime.UtcNow
            };
            ApplyPerson(person, request, gender, waiver);

            if (request.EffectiveDate is DateTime effectiveDate)
            {
                var settings = await GetOrCreateSettingsAsync(db, actor.AgencyId, cancellationToken);
                person.Forms = BuildInitialForms(request.Forms, effectiveDate, settings);
                AddInitialFormAttestations(
                    db, auditTrail, actor, effectiveDate, settings,
                    person.Forms, person.Forms);
            }

            await using var createTransaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
            db.People.Add(person);
            lifecycle.RecordCreated(actor, person);
            auditTrail.Record(actor, AuditActions.PersonCreated, "Person");
            await db.SaveChangesAsync(cancellationToken);

            if (person.EffectiveDate is not null)
            {
                await ReconcileCurrentReleaseCyclesAsync(
                    db, auditTrail, actor, person, clock, "person-create", cancellationToken);
                if (db.ChangeTracker.HasChanges())
                    await db.SaveChangesAsync(cancellationToken);
            }

            var releases = (await LoadReleaseBillingRowsByPersonAsync(
                    db, [person.Id], cancellationToken))
                .GetValueOrDefault(person.Id) ?? [];
            await createTransaction.CommitAsync(cancellationToken);
            return Results.Ok(ContractMapper.ToPerson(
                person, person.Forms, [], releases.Select(item => item.ToComplianceFact()).ToArray()));
        });

        api.MapPut("/people/{personId:int}", async Task<IResult> (
            int personId,
            SavePersonRequest request,
            ClaimsPrincipal principal,
            ApiDbContext db,
            PersonLifecycle lifecycle,
            AuditTrail auditTrail,
            CancellationToken cancellationToken) =>
        {
            var newForms = request.Forms.Where(form => form.Id == 0).ToList();
            var validation = ValidatePerson(request, requireNewForms: newForms.Count > 0);
            if (validation.Count > 0)
                return Results.ValidationProblem(validation);

            var actor = Actor.From(principal);
            await using var signatureChangeTransaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            if (!await TenantAccess.OwnsPersonAsync(db, actor, personId, cancellationToken))
                return Results.NotFound();
            var person = await db.People.SingleOrDefaultAsync(
                x => x.Id == personId && x.UserId == actor.UserId,
                cancellationToken);
            if (person is null)
                return Results.NotFound();
            if (request.ExpectedRevision != person.Revision)
                return StalePersonConflict();
            if (request.IsTestData != person.IsTestData)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["isTestData"] = ["The Test designation is set only when a consumer is created and cannot be changed later."]
                });
            }
            if (request.EffectiveDate?.Date != person.EffectiveDate?.Date &&
                (await db.Forms.AnyAsync(form => form.PersonId == personId, cancellationToken) ||
                 await db.ReleaseObligations.AnyAsync(obligation => obligation.PersonId == personId, cancellationToken)))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["effectiveDate"] = ["This date anchors existing annual forms or releases. Correcting it requires an audited schedule reconciliation; ordinary client edits cannot change it."]
                });
            }

            var before = PersonLifecycle.Capture(person);
            var signingDetailsBefore = (person.FirstName, person.LastName, person.Email);
            await lifecycle.EnsureBaselineAsync(person, cancellationToken);
            ContractMapper.TryParseGender(request.Gender, out var gender);
            ContractMapper.TryParseWaiver(request.Waiver, out var waiver);
            ApplyPerson(person, request, gender, waiver);

            var additionalChanges = new List<PersonFieldChangeDto>();
            if (newForms.Count > 0)
            {
                if (request.EffectiveDate is not DateTime effectiveDate)
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["effectiveDate"] = ["An effective date is required when generating forms."]
                    });

                var settings = await GetOrCreateSettingsAsync(db, actor.AgencyId, cancellationToken);
                var addedForms = BuildInitialForms(newForms, effectiveDate, settings);
                foreach (var form in addedForms)
                {
                    form.PersonId = person.Id;
                    db.Forms.Add(form);
                }
                var allForms = await db.Forms
                    .Where(form => form.PersonId == person.Id)
                    .ToListAsync(cancellationToken);
                allForms.AddRange(addedForms);
                AddInitialFormAttestations(
                    db, auditTrail, actor, effectiveDate, settings,
                    addedForms, allForms);
                additionalChanges.Add(new PersonFieldChangeDto(
                    "forms",
                    "Generated compliance forms",
                    null,
                    $"{newForms.Count} forms"));
            }

            if (lifecycle.RecordChanged(actor, person, before, "Updated", additionalChanges))
                auditTrail.Record(actor, AuditActions.PersonUpdated, "Person", personId);
            if (signingDetailsBefore != (person.FirstName, person.LastName, person.Email))
                await Sati.Data.SignaturePersistenceMutations.RevokeOpenForSignerAsync(db, personId, null, actor.UserId, DateTime.UtcNow, cancellationToken);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                await signatureChangeTransaction.CommitAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return StalePersonConflict();
            }
            return Results.Ok(await LoadPersonDtoAsync(db, person, cancellationToken));
        });

        // Moves a consumer between caseloads. The immediate caller is caseload distribution
        // after a Credible import, where a supervisor holds a batch personally and hands it
        // out; the same route is what staff turnover needs. See CREDIBLE_IMPORT_DESIGN.md.
        //
        // Every fact the decision rests on is read from the database. The request supplies a
        // target id and a revision token and nothing else — in particular it cannot assert
        // who the current owner is, which is the value an attacker would most want to choose.
        api.MapPut("/people/{personId:int}/owner", async Task<IResult> (
            int personId,
            TransferCaseloadRequest request,
            ClaimsPrincipal principal,
            ApiDbContext db,
            PersonLifecycle lifecycle,
            AuditTrail auditTrail,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            var person = await db.People.SingleOrDefaultAsync(
                candidate => candidate.Id == personId && candidate.AgencyId == actor.AgencyId,
                cancellationToken);
            if (person is null)
                return Results.NotFound();

            var currentOwner = await TenantAccess.LoadParticipantAsync(
                db, person.UserId, cancellationToken);
            var target = await TenantAccess.LoadParticipantAsync(
                db, request.TargetUserId, cancellationToken);
            if (currentOwner is null || target is null)
                return Results.NotFound();

            var denial = CaseloadTransferRules.Evaluate(
                actor.ToAgencyActor(), currentOwner.Value, target.Value);
            if (denial is CaseloadTransferDenial.AlreadyOwned)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["targetUserId"] = [CaseloadTransferRules.Describe(denial)]
                });
            }
            if (denial is not CaseloadTransferDenial.None)
                return Results.Forbid();

            if (request.ExpectedRevision != person.Revision)
                return StalePersonConflict();

            var before = PersonLifecycle.Capture(person);
            await lifecycle.EnsureBaselineAsync(person, cancellationToken);
            var previousUserId = person.UserId;
            person.UserId = request.TargetUserId;

            // userId is a tracked lifecycle field, so the move lands in the consumer's own
            // history as a named change and bumps the revision. The audit event is separate
            // and unconditional in intent: history says what the record looks like now, the
            // trail says who moved it.
            if (lifecycle.RecordChanged(actor, person, before, "Reassigned"))
            {
                auditTrail.Record(
                    actor,
                    AuditActions.PersonReassigned,
                    "Person",
                    personId,
                    JsonSerializer.Serialize(new
                    {
                        previousUserId,
                        newUserId = request.TargetUserId
                    }));
            }

            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return StalePersonConflict();
            }

            return Results.Ok(new CaseloadOwnershipDto(person.Id, person.UserId, person.Revision));
        });

        // Archives or restores a consumer. Non-destructive — changes visibility and work
        // generation, never data. PersonStatusRules owns who may set which status; loaded here
        // from the database rather than trusted from the request, the same pattern the /owner
        // route above uses.
        api.MapPut("/people/{personId:int}/status", async Task<IResult> (
            int personId,
            SetPersonStatusRequest request,
            ClaimsPrincipal principal,
            ApiDbContext db,
            PersonLifecycle lifecycle,
            AuditTrail auditTrail,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            var person = await LoadAuditablePersonAsync(db, actor, personId, cancellationToken);
            if (person is null)
                return Results.NotFound();

            if (!actor.HasAdminPermissions &&
                !await TenantAccess.CanAccessUserAsync(db, actor, actor.UserId, cancellationToken))
                return Results.Forbid();

            var refusal = PersonStatusRules.Describe(
                actor.HasAdminPermissions, person.UserId == actor.UserId, request.Status);
            if (refusal is not null)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["status"] = [refusal]
                });
            }

            if (request.ExpectedRevision != person.Revision)
                return StalePersonConflict();

            var before = PersonLifecycle.Capture(person);
            await lifecycle.EnsureBaselineAsync(person, cancellationToken);
            var previousStatus = person.Status;
            person.Status = Array.IndexOf(PersonStatusRules.AllStatuses, request.Status);
            person.StatusNote = request.Note;
            person.StatusChangedAtUtc = DateTime.UtcNow;
            person.StatusChangedByUserId = actor.UserId;

            // RecordChanged bumps Revision and writes history whenever status or its note
            // differ; the audit event is narrower and fires only on an actual status
            // transition, so a note-only edit is not misreported as an archive action.
            if (lifecycle.RecordChanged(actor, person, before, "StatusChanged") &&
                previousStatus != person.Status)
            {
                auditTrail.Record(
                    actor,
                    person.Status == 0 ? AuditActions.ConsumerUnarchived : AuditActions.ConsumerArchived,
                    "Person",
                    personId,
                    JsonSerializer.Serialize(new
                    {
                        previousStatus = PersonStatusRules.AllStatuses[previousStatus],
                        newStatus = request.Status
                    }));
            }

            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return StalePersonConflict();
            }

            return Results.Ok(new PersonStatusDto(
                person.Id, request.Status, person.StatusNote, person.Revision));
        });

        // Which of these Credible ids the agency already holds. The dedupe check behind bulk
        // import: re-running a folder must report rather than duplicate.
        //
        // Scoped to the agency rather than the caller's own caseload, because the duplicate an
        // importing supervisor most needs to catch is a consumer already sitting on one of their
        // case managers' caseloads. The response carries no name and no person id — only the id
        // the caller already had, plus the owner's display name where the caller could already
        // see that caseload. So a plain case manager learns an id is taken without learning
        // whose consumer it is.
        api.MapPost("/people/credible-matches", async Task<IResult> (
            CredibleClientLookupRequest request,
            ClaimsPrincipal principal,
            ApiDbContext db,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!actor.HasCaseManagerPermissions)
                return Results.Forbid();

            var ids = (request.CredibleClientIds ?? [])
                .Select(id => id?.Trim())
                .Where(id => !string.IsNullOrEmpty(id))
                .Distinct(StringComparer.Ordinal)
                .Take(CredibleMatchLookupLimit + 1)
                .Select(id => id!)
                .ToList();
            var mcIds = (request.MaineCareIds ?? [])
                .Select(id => id?.Trim())
                .Where(id => !string.IsNullOrEmpty(id))
                .Distinct(StringComparer.Ordinal)
                .Take(CredibleMatchLookupLimit + 1)
                .Select(id => id!)
                .ToList();
            var names = (request.NameBirthDates ?? [])
                .Distinct()
                .Take(CredibleMatchLookupLimit + 1)
                .ToList();

            if (ids.Count == 0 && mcIds.Count == 0 && names.Count == 0)
                return Results.Ok(CredibleMatchLookupResult.Empty);
            if (ids.Count > CredibleMatchLookupLimit || mcIds.Count > CredibleMatchLookupLimit ||
                names.Count > CredibleMatchLookupLimit)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["credibleClientIds"] =
                        [$"Look up at most {CredibleMatchLookupLimit} identifiers per tier at a time."]
                });
            }

            PreventSensitiveResponseCaching(httpContext);

            var agencyActor = actor.ToAgencyActor();
            bool CanDisclose(int ownerId, int ownerAgencyId, UserPermissions permissions, int? supervisorId) =>
                CaseloadTransferRules.CanReachOwnOrSupervisedCaseload(
                    agencyActor, new CaseloadParticipant(ownerId, ownerAgencyId, permissions, supervisorId));

            var credibleMatches = new List<CredibleClientMatchDto>();
            if (ids.Count > 0)
            {
                var matches = await (
                    from person in db.People.AsNoTracking()
                    join owner in db.Users.AsNoTracking() on person.UserId equals owner.Id
                    where person.AgencyId == actor.AgencyId &&
                          person.CredibleClientId != null &&
                          ids.Contains(person.CredibleClientId)
                    select new
                    {
                        person.CredibleClientId, OwnerId = owner.Id, OwnerName = owner.DisplayName,
                        owner.AgencyId, owner.Permissions, owner.SupervisorId
                    }).ToListAsync(cancellationToken);

                credibleMatches = matches
                    .Select(match => new CredibleClientMatchDto(
                        match.CredibleClientId!,
                        CanDisclose(match.OwnerId, match.AgencyId, match.Permissions, match.SupervisorId)
                            ? match.OwnerName : null))
                    .DistinctBy(match => match.CredibleClientId, StringComparer.Ordinal)
                    .ToList();
            }

            var maineCareMatches = new List<MaineCareIdMatchDto>();
            if (mcIds.Count > 0)
            {
                var matches = await (
                    from person in db.People.AsNoTracking()
                    join owner in db.Users.AsNoTracking() on person.UserId equals owner.Id
                    where person.AgencyId == actor.AgencyId &&
                          person.MaineCareId != null &&
                          mcIds.Contains(person.MaineCareId)
                    select new
                    {
                        person.MaineCareId, OwnerId = owner.Id, OwnerName = owner.DisplayName,
                        owner.AgencyId, owner.Permissions, owner.SupervisorId
                    }).ToListAsync(cancellationToken);

                maineCareMatches = matches
                    .Select(match => new MaineCareIdMatchDto(
                        match.MaineCareId!,
                        CanDisclose(match.OwnerId, match.AgencyId, match.Permissions, match.SupervisorId)
                            ? match.OwnerName : null))
                    .DistinctBy(match => match.MaineCareId, StringComparer.Ordinal)
                    .ToList();
            }

            // Name+DOB cannot be pushed to SQL as a normalized comparison, so it loads the
            // agency's identity columns and matches in memory. Agency scale is 300-400 consumers
            // (CREDIBLE_IMPORT_DESIGN.md), so this only runs once per bulk dry run and is cheap.
            var nameBirthDateMatches = new List<NameBirthDateMatchDto>();
            if (names.Count > 0)
            {
                var matches = await (
                    from person in db.People.AsNoTracking()
                    join owner in db.Users.AsNoTracking() on person.UserId equals owner.Id
                    where person.AgencyId == actor.AgencyId &&
                          person.LastName != null && person.FirstName != null
                    select new
                    {
                        person.LastName, person.FirstName, person.BirthDate,
                        OwnerId = owner.Id, OwnerName = owner.DisplayName,
                        owner.AgencyId, owner.Permissions, owner.SupervisorId
                    }).ToListAsync(cancellationToken);

                nameBirthDateMatches = matches
                    .Where(match => names.Any(candidate =>
                        ProviderDirectoryRules.IsSameName(candidate.LastName, match.LastName) &&
                        ProviderDirectoryRules.IsSameName(candidate.FirstName, match.FirstName) &&
                        candidate.BirthDate.Date == match.BirthDate.Date))
                    .Select(match => new NameBirthDateMatchDto(
                        new PersonNameBirthDate(match.LastName!, match.FirstName!, match.BirthDate),
                        CanDisclose(match.OwnerId, match.AgencyId, match.Permissions, match.SupervisorId)
                            ? match.OwnerName : null))
                    .DistinctBy(match => (match.NameBirthDate.LastName.ToUpperInvariant(),
                        match.NameBirthDate.FirstName.ToUpperInvariant(), match.NameBirthDate.BirthDate.Date))
                    .ToList();
            }

            return Results.Ok(new CredibleMatchLookupResult(
                credibleMatches, maineCareMatches, nameBirthDateMatches));
        });

        api.MapGet("/people/{personId:int}/history", async Task<IResult> (
            int personId,
            ClaimsPrincipal principal,
            HttpContext httpContext,
            ApiDbContext db,
            PersonLifecycle lifecycle,
            AuditTrail auditTrail,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!actor.HasAdminPermissions)
                return Results.Forbid();
            var person = await LoadAuditablePersonAsync(db, actor, personId, cancellationToken);
            if (person is null)
                return Results.NotFound();

            PreventSensitiveResponseCaching(httpContext);
            await lifecycle.EnsureBaselineAsync(person, cancellationToken);
            auditTrail.Record(actor, AuditActions.PersonHistoryViewed, "Person", personId);
            await db.SaveChangesAsync(cancellationToken);
            var versions = await db.PersonVersions.AsNoTracking()
                .Where(version => version.PersonId == personId && version.AgencyId == actor.AgencyId)
                .OrderBy(version => version.Version)
                .ToListAsync(cancellationToken);
            return Results.Ok(versions.Select(PersonLifecycle.ToDto).ToList());
        });

        api.MapGet("/people/{personId:int}/history.pdf", async Task<IResult> (
            int personId,
            ClaimsPrincipal principal,
            HttpContext httpContext,
            ApiDbContext db,
            PersonLifecycle lifecycle,
            PersonAuditPdfGenerator pdfGenerator,
            AuditTrail auditTrail,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!actor.HasAdminPermissions)
                return Results.Forbid();
            var person = await LoadAuditablePersonAsync(db, actor, personId, cancellationToken);
            if (person is null)
                return Results.NotFound();

            PreventSensitiveResponseCaching(httpContext);
            await lifecycle.EnsureBaselineAsync(person, cancellationToken);
            auditTrail.Record(actor, AuditActions.PersonHistoryPdfGenerated, "Person", personId);
            await db.SaveChangesAsync(cancellationToken);
            var versions = await db.PersonVersions.AsNoTracking()
                .Where(version => version.PersonId == personId && version.AgencyId == actor.AgencyId)
                .OrderBy(version => version.Version)
                .ToListAsync(cancellationToken);
            var agency = await db.Agencies.AsNoTracking().SingleAsync(
                candidate => candidate.Id == actor.AgencyId,
                cancellationToken);
            var pdf = pdfGenerator.Generate(person, versions, agency, actor, DateTime.UtcNow);
            var safeName = SafeFileName($"{person.LastName}-{person.FirstName}");
            return Results.File(
                pdf,
                "application/pdf",
                $"person-{person.Id}-{safeName}-lifecycle-audit.pdf");
        });

        // The mask, never the number. There is no route anywhere that returns a
        // plaintext SSN: the only thing that decrypts is the form fill below, and what
        // leaves this process is a PDF, not a string.
        api.MapGet("/people/{personId:int}/ssn", async Task<IResult> (
            int personId,
            ClaimsPrincipal principal,
            HttpContext httpContext,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!await TenantAccess.OwnsPersonAsync(db, actor, personId, cancellationToken))
                return Results.NotFound();

            PreventSensitiveResponseCaching(httpContext);
            var lastFour = await db.People.AsNoTracking()
                .Where(person => person.Id == personId)
                .Select(person => person.SsnLastFour)
                .SingleOrDefaultAsync(cancellationToken);

            return Results.Ok(new SsnStatusDto(
                SsnMask.Format(lastFour),
                !string.IsNullOrEmpty(lastFour)));
        });

        api.MapPut("/people/{personId:int}/ssn", async Task<IResult> (
            int personId,
            SsnUpdateRequest request,
            ClaimsPrincipal principal,
            ApiDbContext db,
            EnvelopeProtector protector,
            AuditTrail auditTrail,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!await TenantAccess.OwnsPersonAsync(db, actor, personId, cancellationToken))
                return Results.NotFound();

            var person = await db.People.SingleOrDefaultAsync(
                candidate => candidate.Id == personId, cancellationToken);
            if (person is null)
                return Results.NotFound();

            var normalized = SsnMask.Normalize(request.Ssn);
            if (normalized is null)
            {
                ClearSsn(person);
            }
            else
            {
                // Shape-checked before it is encrypted. A transposed digit that reaches
                // an official form is a rejected application; catching it here costs
                // nothing, and once encrypted nothing can look at it again to check.
                if (!SsnMask.IsWellFormed(normalized))
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["Ssn"] = ["Enter a valid nine-digit Social Security number."],
                    });
                }

                await ProtectSsnAsync(person, actor.AgencyId, normalized, protector, cancellationToken);
            }

            // The action, never the value. An audit row naming what changed is the
            // point; an audit row containing the number would defeat the column.
            auditTrail.Record(actor, AuditActions.PersonSsnUpdated, "Person", personId);
            await db.SaveChangesAsync(cancellationToken);

            return Results.Ok(new SsnStatusDto(
                SsnMask.Format(person.SsnLastFour),
                !string.IsNullOrEmpty(person.SsnLastFour)));
        });

        // The one operation permitted to decrypt an SSN, and the reason the filler
        // lives on the server at all.
        api.MapPost("/people/{personId:int}/forms.pdf", async Task<IResult> (
            int personId,
            DhhsFormRequest request,
            ClaimsPrincipal principal,
            HttpContext httpContext,
            ApiDbContext db,
            EnvelopeProtector protector,
            DhhsFormFiller filler,
            AuditTrail auditTrail,
            ApiClock clock,
            CancellationToken cancellationToken) =>
        {
            if (!Enum.TryParse<DhhsFormDefinition.FormKey>(request.Form, out var form))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["Form"] = ["Unknown DHHS form."],
                });
            }

            var actor = Actor.From(principal);
            if (!await TenantAccess.OwnsPersonAsync(db, actor, personId, cancellationToken))
                return Results.NotFound();

            var person = await db.People.AsNoTracking().SingleOrDefaultAsync(
                candidate => candidate.Id == personId, cancellationToken);
            if (person is null)
                return Results.NotFound();

            if (form != DhhsFormDefinition.FormKey.AuthorizationToRelease &&
                (request.TargetEffectiveDate is not null || request.ReleaseObligationId is not null))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["targetEffectiveDate"] =
                        ["Annual target identity applies only to the DHHS authorization-to-release form."]
                }, statusCode: StatusCodes.Status422UnprocessableEntity);
            }

            DateTime? dhhsTargetEffectiveDate = null;
            DocumentReleaseLinkResolution? dhhsReleaseLink = null;
            if (form == DhhsFormDefinition.FormKey.AuthorizationToRelease)
            {
                if (person.EffectiveDate is not DateTime effectiveDate)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["person"] = ["The consumer has no effective date."]
                    }, statusCode: StatusCodes.Status422UnprocessableEntity);
                }

                var timing = await GetDhhsReleaseTimingAsync(
                    db, actor.AgencyId, cancellationToken);
                var requestedTarget = request.TargetEffectiveDate?.Date ??
                    (request.ReleaseObligationId is null
                        ? AnnualDocumentCycle.SuggestedStart(
                            effectiveDate,
                            clock.Today,
                            timing.OpenDaysBefore,
                            timing.DueDaysBeforeEffective)
                        : null);
                dhhsReleaseLink = await ResolveDocumentReleaseObligationAsync(
                    db,
                    actor,
                    personId,
                    AnnualDocumentKind.ReleaseDhhs,
                    requestedTarget,
                    request.ReleaseObligationId,
                    clock.Today,
                    cancellationToken);
                if (dhhsReleaseLink.Error is not null)
                    return dhhsReleaseLink.Error;

                dhhsTargetEffectiveDate = dhhsReleaseLink.Obligation?.TargetEffectiveDate.Date ??
                    requestedTarget;
                if (dhhsTargetEffectiveDate is not DateTime target ||
                    target < effectiveDate.Date ||
                    AnnualDocumentCycle.CurrentStart(effectiveDate, target) != target)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["targetEffectiveDate"] =
                            ["Choose an effective-date anniversary on or after enrollment."]
                    }, statusCode: StatusCodes.Status422UnprocessableEntity);
                }
                if (dhhsReleaseLink.Obligation is null &&
                    !AnnualDocumentCycle.IsAvailable(
                        target,
                        clock.Today,
                        timing.OpenDaysBefore,
                        timing.DueDaysBeforeEffective))
                {
                    var availableOn = target.Date
                        .AddDays(-timing.DueDaysBeforeEffective)
                        .AddDays(-timing.OpenDaysBefore);
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["targetEffectiveDate"] =
                            [$"This DHHS release becomes available on {availableOn:yyyy-MM-dd}."]
                    }, statusCode: StatusCodes.Status422UnprocessableEntity);
                }
            }

            var caseManager = await db.Users.AsNoTracking().SingleAsync(
                user => user.Id == actor.UserId, cancellationToken);
            var agency = await db.Agencies.AsNoTracking().SingleAsync(
                candidate => candidate.Id == actor.AgencyId, cancellationToken);

            string? ssn = null;
            if (person.SsnCiphertext is not null && person.SsnKeyId is not null)
            {
                var binding = new FieldBinding(actor.AgencyId, person.Id, "Ssn");
                ssn = await protector.UnprotectAsync(
                    new ProtectedValue(
                        person.SsnCiphertext,
                        person.SsnNonce!,
                        person.SsnTag!,
                        person.SsnWrappedKey!,
                        person.SsnKeyId),
                    binding,
                    cancellationToken);
                auditTrail.Record(actor, AuditActions.PersonSsnDecrypted, "Person", personId);
            }

            var subject = new DhhsFormDefinition.Subject(
                    FullName: $"{person.LastName}, {person.FirstName}".Trim(' ', ','),
                    BirthDate: person.BirthDate,
                    Address: person.Address,
                    PhoneNumber: person.PhoneNumber,
                    SocialSecurityNumber: ssn,
                    RepresentativeName: null,
                    RepresentativeAddress: null,
                    RepresentativePhone: null,
                    RepresentativeEmail: null)
                .WithRepresentative(
                    caseManager.DisplayName,
                    caseManager.Phone,
                    caseManager.Email,
                    agency.Street,
                    agency.City,
                    agency.State,
                    agency.Zip);

            var selections = new DhhsFormDefinition.Selections(request.Checks, request.Text);

            byte[] pdf;
            try
            {
                pdf = filler.Fill(form, subject, selections);
            }
            catch (InvalidOperationException refusal)
            {
                // A selection naming something that is not a consent field of this
                // form. Surfaced rather than ignored: silently dropping it would let a
                // case manager believe they recorded a choice the PDF never received.
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["Selections"] = [refusal.Message],
                });
            }

            var unfilled = DhhsFormDefinition.UnfilledFields(form, subject).ToList();
            var generatedAtUtc = clock.UtcNow.UtcDateTime;
            await using var documentTransaction = await db.Database.BeginTransactionAsync(cancellationToken);
            if (form is DhhsFormDefinition.FormKey.AuthorizationToRelease or
                DhhsFormDefinition.FormKey.AuthorizedRepresentative)
            {
                var isDraft = (request.Checks is null || request.Checks.Count == 0) &&
                    (request.Text is null || request.Text.Count == 0);
                if (isDraft)
                    unfilled.Add(form == DhhsFormDefinition.FormKey.AuthorizationToRelease
                        ? "Consumer authorization choices"
                        : "Representative authority choices");
                // The DHHS release is filed under its exact annual target; the once-only
                // Authorized Representative form keeps the current period as placement.
                var cycleStart = dhhsTargetEffectiveDate ?? AnnualDocumentCycle.CurrentStart(
                    person.EffectiveDate ?? throw new InvalidOperationException("The consumer has no effective date."),
                    generatedAtUtc.ToLocalTime());
                var artifactFileName = $"{form}-{personId}-{SafeFileName($"{person.LastName}-{person.FirstName}")}.pdf";
                var documentKind = form == DhhsFormDefinition.FormKey.AuthorizationToRelease
                    ? AnnualDocumentKind.ReleaseDhhs
                    : AnnualDocumentKind.DhhsAuthorizedRepresentative;
                await DocumentArtifactPersistence.StageGeneratedAsync(
                    db, personId, actor.AgencyId, documentKind, cycleStart,
                    isDraft ? DocumentArtifactOrigin.Draft : DocumentArtifactOrigin.GeneratedInSati,
                    generatedAtUtc, actor.UserId, pdf, artifactFileName, unfilled, cancellationToken,
                    releaseObligationId: dhhsReleaseLink?.Obligation?.Id);
                auditTrail.Record(actor, AuditActions.DocumentGenerated, "Person", personId,
                    JsonSerializer.Serialize(new
                    {
                        kind = documentKind.ToString(),
                        cycleStart = cycleStart.ToString("yyyy-MM-dd"),
                        releaseObligationId = dhhsReleaseLink?.Obligation?.ObligationId,
                        releaseObligationKey = dhhsReleaseLink?.Obligation?.StableKey,
                        origin = isDraft ? DocumentArtifactOrigin.Draft.ToString() : DocumentArtifactOrigin.GeneratedInSati.ToString()
                    }));
            }

            PreventSensitiveResponseCaching(httpContext);
            auditTrail.Record(actor, AuditActions.DhhsFormGenerated, "Person", personId,
                metadataJson: JsonSerializer.Serialize(new { Form = form.ToString() }));
            await db.SaveChangesAsync(cancellationToken);
            await documentTransaction.CommitAsync(cancellationToken);

            // Which boxes could not be filled, as a header rather than a JSON wrapper,
            // so the body stays a PDF the client can hand straight to a save dialog. A
            // blank box is never an error — the form is still correct and usable, it
            // just needs a pen — so this is advisory, not a failure.
            if (unfilled.Count > 0)
                httpContext.Response.Headers["X-Sati-Unfilled-Fields"] = string.Join("|", unfilled);

            var safeName = SafeFileName($"{person.LastName}-{person.FirstName}");
            return Results.File(pdf, "application/pdf", $"{form}-{personId}-{safeName}.pdf");
        });

        // Agency-owned release. Unlike the DHHS route this never decrypts an SSN,
        // but it is still a disclosure artifact: identity is derived from the
        // authorized person/session, generation is audited, and the PDF is no-store.
        api.MapPost("/people/{personId:int}/agency-release.pdf", async Task<IResult> (
            int personId,
            AgencyReleaseRequest request,
            ClaimsPrincipal principal,
            HttpContext httpContext,
            ApiDbContext db,
            AgencyReleasePdfGenerator generator,
            AuditTrail auditTrail,
            ApiClock clock,
            CancellationToken cancellationToken) =>
        {
            var validation = AgencyReleaseRules.Validate(request);
            if (validation.Count > 0)
                return Results.ValidationProblem(validation);

            var actor = Actor.From(principal);
            if (!await TenantAccess.OwnsPersonAsync(db, actor, personId, cancellationToken))
                return Results.NotFound();

            var person = await db.People.AsNoTracking().SingleOrDefaultAsync(
                candidate => candidate.Id == personId,
                cancellationToken);
            if (person is null)
                return Results.NotFound();
            var agency = await db.Agencies.AsNoTracking().SingleAsync(
                candidate => candidate.Id == actor.AgencyId,
                cancellationToken);

            var subject = new AgencyReleaseSubject(
                person.Id,
                $"{person.FirstName} {person.LastName}".Trim(),
                person.BirthDate,
                person.HasGuardian ? person.GuardianName : null,
                agency.Name,
                ComposeAddress(agency.Street, agency.City, agency.State, agency.Zip),
                agency.EdiContactPhone,
                actor.DisplayName,
                actor.Role);
            var generatedAtUtc = clock.UtcNow.UtcDateTime;
            var pdf = generator.Generate(subject, request, generatedAtUtc);
            var cycleStart = AnnualDocumentCycle.CurrentStart(
                person.EffectiveDate ?? throw new InvalidOperationException("The consumer has no effective date."),
                generatedAtUtc.ToLocalTime());
            var safeName = SafeFileName($"{person.LastName}-{person.FirstName}");
            var prefix = request.IsRevocation ? "Agency-Release-Revocation" : "Agency-Release";
            if (request.IsDraft)
                prefix += "-DRAFT";
            var fileName = $"{prefix}-{personId}-{safeName}.pdf";
            await using var documentTransaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await DocumentArtifactPersistence.StageGeneratedAsync(
                db, personId, actor.AgencyId, AnnualDocumentKind.ReleaseAgency, cycleStart,
                request.IsDraft ? DocumentArtifactOrigin.Draft : DocumentArtifactOrigin.GeneratedInSati,
                generatedAtUtc, actor.UserId, pdf, fileName,
                request.IsDraft ? ReleaseDraftBlankFields(request) : [], cancellationToken);

            PreventSensitiveResponseCaching(httpContext);
            auditTrail.Record(
                actor,
                AuditActions.AgencyReleaseGenerated,
                "Person",
                personId,
                metadataJson: JsonSerializer.Serialize(new
                {
                    Scope = request.Scope,
                    StaffAttestation = request.ConfirmedObtainedRoi,
                    Revocation = request.IsRevocation,
                }));
            auditTrail.Record(actor, AuditActions.DocumentGenerated, "Person", personId,
                JsonSerializer.Serialize(new
                {
                    kind = AnnualDocumentKind.ReleaseAgency.ToString(),
                    cycleStart = cycleStart.ToString("yyyy-MM-dd"),
                    origin = request.IsDraft ? DocumentArtifactOrigin.Draft.ToString() : DocumentArtifactOrigin.GeneratedInSati.ToString()
                }));
            await db.SaveChangesAsync(cancellationToken);
            await documentTransaction.CommitAsync(cancellationToken);

            return Results.File(pdf, "application/pdf", fileName);
        });

        api.MapGet("/people/{personId:int}/contacts", async Task<IResult> (
            int personId,
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!await TenantAccess.OwnsPersonAsync(db, actor, personId, cancellationToken))
                return Results.NotFound();

            var contacts = await db.PersonContacts.AsNoTracking()
                .Where(x => x.PersonId == personId && x.IsActive)
                .OrderBy(x => x.LastName)
                .ThenBy(x => x.FirstName)
                .ToListAsync(cancellationToken);
            return Results.Ok(contacts.Select(ContractMapper.ToPersonContact).ToList());
        });

        api.MapPost("/people/{personId:int}/contacts", async Task<IResult> (
            int personId,
            SavePersonContactRequest request,
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var validation = ValidatePersonContact(request);
            if (validation.Count > 0)
                return Results.ValidationProblem(validation);

            var actor = Actor.From(principal);
            if (!await TenantAccess.OwnsPersonAsync(db, actor, personId, cancellationToken))
                return Results.NotFound();

            var contact = new ServerPersonContact { PersonId = personId };
            ApplyPersonContact(contact, request);
            db.PersonContacts.Add(contact);
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(ContractMapper.ToPersonContact(contact));
        });

        api.MapPut("/people/{personId:int}/contacts/{contactId:int}", async Task<IResult> (
            int personId,
            int contactId,
            SavePersonContactRequest request,
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var validation = ValidatePersonContact(request);
            if (validation.Count > 0)
                return Results.ValidationProblem(validation);

            var actor = Actor.From(principal);
            await using var signatureChangeTransaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            if (!await TenantAccess.OwnsPersonAsync(db, actor, personId, cancellationToken))
                return Results.NotFound();
            var contact = await (from candidate in db.PersonContacts
                                 join person in db.People on candidate.PersonId equals person.Id
                                 where candidate.Id == contactId && candidate.PersonId == personId &&
                                       person.UserId == actor.UserId
                                 select candidate).SingleOrDefaultAsync(cancellationToken);
            if (contact is null)
                return Results.NotFound();

            var signingDetailsBefore = (contact.FirstName, contact.LastName, contact.Email, contact.Kind, contact.IsActive);
            ApplyPersonContact(contact, request);
            if (signingDetailsBefore != (contact.FirstName, contact.LastName, contact.Email, contact.Kind, contact.IsActive))
                await Sati.Data.SignaturePersistenceMutations.RevokeOpenForSignerAsync(db, personId, contactId, actor.UserId, DateTime.UtcNow, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await signatureChangeTransaction.CommitAsync(cancellationToken);
            return Results.Ok(ContractMapper.ToPersonContact(contact));
        });

        api.MapDelete("/contacts/{contactId:int}", async Task<IResult> (
            int contactId,
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            await using var signatureChangeTransaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            var contact = await (from candidate in db.PersonContacts
                                 join person in db.People on candidate.PersonId equals person.Id
                                 where candidate.Id == contactId && person.UserId == actor.UserId
                                 select candidate).SingleOrDefaultAsync(cancellationToken);
            if (contact is null)
                return Results.NotFound();

            if (!await TenantAccess.OwnsPersonAsync(db, actor, contact.PersonId, cancellationToken))
                return Results.NotFound();

            contact.IsActive = false;
            await Sati.Data.SignaturePersistenceMutations.RevokeOpenForSignerAsync(db, contact.PersonId, contactId, actor.UserId, DateTime.UtcNow, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await signatureChangeTransaction.CommitAsync(cancellationToken);
            return Results.NoContent();
        });

        MapConsumerProviders(api);
    }

    // A consumer's medical provider list. The response carries the link's own fields and
    // nothing derived: the practice and network are resolved by the caller from the
    // directory it already holds, so a payload cannot disagree with the directory it came
    // from, and correcting a directory entry corrects every profile at once.
    private static void MapConsumerProviders(RouteGroupBuilder api)
    {
        api.MapGet("/people/{personId:int}/providers", async Task<IResult> (
            int personId, ClaimsPrincipal principal, ApiDbContext db, CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!await TenantAccess.OwnsPersonAsync(db, actor, personId, cancellationToken))
                return Results.NotFound();

            // Ended links are returned too. Past providers are part of the record; which
            // of them to show is the caller's decision, not the query's.
            var links = await db.PersonProviders.AsNoTracking()
                .Where(link => link.PersonId == personId)
                .OrderByDescending(link => link.IsPrimaryCare)
                .ThenBy(link => link.SortOrder)
                .ThenBy(link => link.Id)
                .ToListAsync(cancellationToken);
            return Results.Ok(links.Select(ContractMapper.ToConsumerProvider).ToList());
        });

        api.MapPost("/people/{personId:int}/providers", async Task<IResult> (
            int personId, SaveConsumerProviderRequest request, ClaimsPrincipal principal,
            ApiDbContext db, AuditTrail audit, ApiClock clock,
            CancellationToken cancellationToken) =>
        {
            var errors = ConsumerProviderRules.Validate(request);
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var actor = Actor.From(principal);
            if (!await TenantAccess.OwnsPersonAsync(db, actor, personId, cancellationToken))
                return Results.NotFound();

            var conflict = await FindConsumerProviderConflictAsync(
                db, actor.AgencyId, personId, request, editingLinkId: 0, cancellationToken);
            if (conflict is not null) return conflict;

            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
            var link = new ServerPersonProvider
            {
                PersonId = personId,
                AssignmentKnownOn = clock.Today
            };
            ApplyConsumerProvider(link, request);
            db.PersonProviders.Add(link);
            await db.SaveChangesAsync(cancellationToken);
            var person = await LoadReleasePersonAsync(
                db, actor, personId, cancellationToken);
            if (person is null)
                return Results.NotFound();
            await ReconcileCurrentReleaseCyclesAsync(
                db, audit, actor, person, clock, "provider-created", cancellationToken);
            if (db.ChangeTracker.HasChanges())
                await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Results.Ok(ContractMapper.ToConsumerProvider(link));
        });

        api.MapPut("/people/{personId:int}/providers/{linkId:int}", async Task<IResult> (
            int personId, int linkId, SaveConsumerProviderRequest request, ClaimsPrincipal principal,
            ApiDbContext db, AuditTrail audit, ApiClock clock, CancellationToken cancellationToken) =>
        {
            var errors = ConsumerProviderRules.Validate(request);
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var actor = Actor.From(principal);
            if (!await TenantAccess.OwnsPersonAsync(db, actor, personId, cancellationToken))
                return Results.NotFound();

            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
            var link = await db.PersonProviders.SingleOrDefaultAsync(
                candidate => candidate.Id == linkId && candidate.PersonId == personId, cancellationToken);
            if (link is null) return Results.NotFound();

            var conflict = await FindConsumerProviderConflictAsync(
                db, actor.AgencyId, personId, request, linkId, cancellationToken);
            if (conflict is not null) return conflict;

            var priorEndDate = link.EndDate;
            ApplyConsumerProvider(link, request);
            if (request.EndDate is DateTime retiredOn &&
                priorEndDate?.Date != retiredOn.Date)
            {
                var assignmentKey = ReleaseAssignmentResolution.AssignmentKey(link.Id);
                var releaseRows = await db.ReleaseObligations
                    .Where(item => item.PersonId == personId &&
                                   item.AssignmentKey == assignmentKey &&
                                   item.RetiredOn == null)
                    .ToListAsync(cancellationToken);
                foreach (var row in releaseRows)
                    row.Retire(retiredOn, clock.UtcNow.UtcDateTime);
                if (releaseRows.Count != 0)
                {
                    audit.Record(
                        actor,
                        AuditActions.ReleaseObligationsReconciled,
                        "Person",
                        personId,
                        JsonSerializer.Serialize(new
                        {
                            retired = releaseRows.Select(item => item.StableKey)
                                .OrderBy(item => item).ToArray(),
                            retiredOn = retiredOn.Date.ToString("yyyy-MM-dd")
                        }));
                }
            }
            await db.SaveChangesAsync(cancellationToken);
            var person = await LoadReleasePersonAsync(
                db, actor, personId, cancellationToken);
            if (person is null)
                return Results.NotFound();
            await ReconcileCurrentReleaseCyclesAsync(
                db, audit, actor, person, clock, "provider-updated", cancellationToken);
            if (db.ChangeTracker.HasChanges())
                await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Results.Ok(ContractMapper.ToConsumerProvider(link));
        });

        // Removal, for a link recorded against the wrong consumer. Ending a real
        // relationship is a PUT that sets EndDate, which keeps the row.
        api.MapDelete("/people/{personId:int}/providers/{linkId:int}", async Task<IResult> (
            int personId, int linkId, ClaimsPrincipal principal,
            ApiDbContext db, AuditTrail audit, ApiClock clock,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!await TenantAccess.OwnsPersonAsync(db, actor, personId, cancellationToken))
                return Results.NotFound();

            var link = await db.PersonProviders.SingleOrDefaultAsync(
                candidate => candidate.Id == linkId && candidate.PersonId == personId, cancellationToken);
            if (link is null) return Results.NotFound();

            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
            var assignmentKey = ReleaseAssignmentResolution.AssignmentKey(link.Id);
            var releaseRows = await db.ReleaseObligations
                .Where(item => item.PersonId == personId &&
                               item.AssignmentKey == assignmentKey &&
                               item.RetiredOn == null)
                .ToListAsync(cancellationToken);
            foreach (var row in releaseRows)
                row.Retire(clock.Today, clock.UtcNow.UtcDateTime);
            if (releaseRows.Count != 0)
            {
                audit.Record(
                    actor,
                    AuditActions.ReleaseObligationsReconciled,
                    "Person",
                    personId,
                    JsonSerializer.Serialize(new
                    {
                        retired = releaseRows.Select(item => item.StableKey)
                            .Order(StringComparer.Ordinal).ToArray(),
                        retiredOn = clock.Today.ToString("yyyy-MM-dd"),
                        source = "provider-link-corrected"
                    }));
            }
            db.PersonProviders.Remove(link);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Results.NoContent();
        });
    }

    // The provider lookup is scoped to the actor's agency, which is what makes a directory
    // entry from another tenant fail as absent rather than linking across the boundary.
    private static async Task<IResult?> FindConsumerProviderConflictAsync(
        ApiDbContext db,
        int agencyId,
        int personId,
        SaveConsumerProviderRequest request,
        int editingLinkId,
        CancellationToken cancellationToken)
    {
        var provider = await db.Providers.AsNoTracking()
            .Where(candidate => candidate.Id == request.ProviderId && candidate.AgencyId == agencyId)
            .Select(candidate => new { candidate.Id, candidate.Name })
            .SingleOrDefaultAsync(cancellationToken);
        if (provider is null)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["providerId"] = [ConsumerProviderRules.ProviderOutsideAgencyMessage()]
            });

        var existing = await db.PersonProviders.AsNoTracking()
            .Where(candidate => candidate.PersonId == personId && candidate.Id != editingLinkId)
            .Select(candidate => new
            {
                candidate.ProviderId, candidate.IsPrimaryCare, candidate.EndDate
            })
            .ToListAsync(cancellationToken);

        if (editingLinkId == 0 && existing.Count >= ConsumerProviderRules.MaxProvidersPerConsumer)
            return Results.Conflict(new ApiErrorDto(
                "consumer_provider_limit", ConsumerProviderRules.TooManyProvidersMessage(), string.Empty));

        if (!ConsumerProviderRules.IsCurrent(request.EndDate))
            return null;

        if (existing.Any(candidate =>
                candidate.ProviderId == request.ProviderId &&
                ConsumerProviderRules.IsCurrent(candidate.EndDate)))
            return Results.Conflict(new ApiErrorDto(
                "consumer_provider_duplicate",
                ConsumerProviderRules.DuplicateCurrentLinkMessage(provider.Name), string.Empty));

        if (!request.IsPrimaryCare)
            return null;

        var currentPrimaryId = existing
            .Where(candidate => candidate.IsPrimaryCare && ConsumerProviderRules.IsCurrent(candidate.EndDate))
            .Select(candidate => (int?)candidate.ProviderId)
            .FirstOrDefault();
        if (currentPrimaryId is null)
            return null;

        var name = await db.Providers.AsNoTracking()
            .Where(candidate => candidate.Id == currentPrimaryId)
            .Select(candidate => candidate.Name)
            .SingleOrDefaultAsync(cancellationToken) ?? "Another provider";
        return Results.Conflict(new ApiErrorDto(
            "consumer_provider_primary_care",
            ConsumerProviderRules.PrimaryCareConflictMessage(name), string.Empty));
    }

    private static void ApplyConsumerProvider(ServerPersonProvider link, SaveConsumerProviderRequest request)
    {
        link.ProviderId = request.ProviderId;
        link.Role = Normalize(request.Role);
        link.IsPrimaryCare = request.IsPrimaryCare;
        link.StartDate = request.StartDate?.Date;
        link.EndDate = request.EndDate?.Date;
        link.HasActiveRelease = request.HasActiveRelease;
        link.SortOrder = request.SortOrder;
    }

    private static void MapReviews(RouteGroupBuilder api)
    {
        api.MapGet("/reviews", async Task<IResult> (
            int userId, ClaimsPrincipal principal, ApiDbContext db, CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!await TenantAccess.CanAccessUserAsync(db, actor, userId, cancellationToken)) return Results.Forbid();
            var items = await (from review in db.ReviewItems.AsNoTracking().Include(x => x.Appointment)
                               join person in db.People on review.PersonId equals person.Id
                               where person.UserId == userId && person.AgencyId == actor.AgencyId
                               select review)
                .OrderBy(x => x.PersonId).ThenBy(x => x.CycleAnchor).ThenBy(x => x.Quarter)
                .ThenBy(x => x.Category).ThenBy(x => x.SlotIndex).ToListAsync(cancellationToken);
            return Results.Ok(items.Select(ContractMapper.ToReviewItem).ToList());
        });

        api.MapGet("/people/{personId:int}/reviews", async Task<IResult> (
            int personId, ClaimsPrincipal principal, ApiDbContext db, CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            var person = await db.People.AsNoTracking().SingleOrDefaultAsync(
                x => x.Id == personId, cancellationToken);
            if (person is null || !await TenantAccess.CanAccessPersonAsync(db, actor, person, cancellationToken)) return Results.NotFound();
            var items = await db.ReviewItems.AsNoTracking().Include(x => x.Appointment)
                .Where(x => x.PersonId == personId).OrderBy(x => x.CycleAnchor).ThenBy(x => x.Quarter)
                .ThenBy(x => x.Category).ThenBy(x => x.SlotIndex).ToListAsync(cancellationToken);
            return Results.Ok(items.Select(ContractMapper.ToReviewItem).ToList());
        });

        api.MapPost("/reviews/ensure-current", async Task<IResult> (
            EnsureReviewItemsRequest request, ClaimsPrincipal principal, ApiDbContext db, CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if ((!actor.HasCaseManagerPermissions && !actor.HasSupervisorPermissions) ||
                !await TenantAccess.IsCurrentActorAsync(db, actor, cancellationToken))
                return Results.Forbid();
            var ids = request.PersonIds.Where(x => x > 0).Distinct().Take(500).ToList();
            var people = await db.People.Where(x => ids.Contains(x.Id) && x.AgencyId == actor.AgencyId).ToListAsync(cancellationToken);
            var created = 0;
            foreach (var person in people)
            {
                if (!await TenantAccess.CanAccessPersonAsync(db, actor, person, cancellationToken)) continue;
                var anchor = CurrentCycleAnchor(person.EffectiveDate, request.Today);
                if (anchor is null) continue;
                var existing = await db.ReviewItems.Where(x => x.PersonId == person.Id && x.CycleAnchor == anchor.Value)
                    .Select(x => new { x.Quarter, x.Category, x.SlotIndex }).ToListAsync(cancellationToken);
                var present = existing.Select(x => (x.Quarter, x.Category, x.SlotIndex)).ToHashSet();
                foreach (var required in RequiredReviewItems(person))
                {
                    if (present.Contains(required)) continue;
                    db.ReviewItems.Add(new ServerReviewItem { PersonId = person.Id, CycleAnchor = anchor.Value,
                        Quarter = required.Quarter, Category = required.Category, SlotIndex = required.SlotIndex });
                    present.Add(required);
                    created++;
                }
            }
            if (created > 0) await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(new CountDto(created));
        });

        api.MapPut("/reviews/{reviewItemId:int}/stage", async Task<IResult> (
            int reviewItemId, SetReviewStageRequest request, ClaimsPrincipal principal, ApiDbContext db, CancellationToken cancellationToken) =>
        {
            var item = await LoadAccessibleReviewAsync(db, Actor.From(principal), reviewItemId, cancellationToken);
            if (item is null) return Results.NotFound();
            switch (request.Stage)
            {
                case "Requested": item.RequestedDate = request.Date?.Date; break;
                case "Received": item.ReceivedDate = request.Date?.Date; break;
                case "Logged": item.LoggedDate = request.Date?.Date; break;
                default: return Results.ValidationProblem(new Dictionary<string, string[]> { ["stage"] = ["The review stage is invalid."] });
            }
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(ContractMapper.ToReviewItem(item));
        });

        api.MapPut("/reviews/{reviewItemId:int}/appointment", async Task<IResult> (
            int reviewItemId, SetAppointmentRequest request, ClaimsPrincipal principal, ApiDbContext db, CancellationToken cancellationToken) =>
        {
            var item = await LoadAccessibleReviewAsync(db, Actor.From(principal), reviewItemId, cancellationToken);
            if (item is null) return Results.NotFound();
            if (item.Category is not ("Medical" or "Dental"))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["appointment"] = ["Appointments apply only to medical and dental reviews."] });
            var providerName = string.IsNullOrWhiteSpace(request.ProviderName) ? null : request.ProviderName.Trim();
            if (providerName?.Length > 100)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["providerName"] = ["Provider name must not exceed 100 characters."] });
            if (request.Date is null)
            {
                if (item.Appointment is not null) db.Appointments.Remove(item.Appointment);
                item.Appointment = null;
            }
            else if (item.Appointment is null)
                item.Appointment = new ServerAppointment { ReviewItemId = item.Id, Date = request.Date.Value.Date, ProviderName = providerName };
            else
            {
                item.Appointment.Date = request.Date.Value.Date;
                item.Appointment.ProviderName = providerName;
            }
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(ContractMapper.ToReviewItem(item));
        });

        api.MapGet("/people/{personId:int}/appointments/latest", async Task<IResult> (
            int personId, ClaimsPrincipal principal, ApiDbContext db, CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            var person = await db.People.AsNoTracking().SingleOrDefaultAsync(
                x => x.Id == personId, cancellationToken);
            if (person is null || !await TenantAccess.CanAccessPersonAsync(db, actor, person, cancellationToken)) return Results.NotFound();
            var medical = await LatestAppointmentAsync(db, personId, "Medical", cancellationToken);
            var dental = await LatestAppointmentAsync(db, personId, "Dental", cancellationToken);
            return Results.Ok(new LatestAppointmentsDto(medical is null ? null : ContractMapper.ToAppointment(medical),
                dental is null ? null : ContractMapper.ToAppointment(dental)));
        });
    }

    private static void MapAssessments(RouteGroupBuilder api)
    {
        api.MapGet("/people/{personId:int}/assessments/latest", async Task<IResult> (
            int personId, ClaimsPrincipal principal, ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!await IsComprehensiveAssessmentAuthoringEnabledAsync(
                    db, actor.AgencyId, cancellationToken))
                return Results.NotFound();
            if (!await TenantAccess.OwnsPersonAsync(db, actor, personId, cancellationToken))
                return Results.NotFound();

            var assessment = await db.ComprehensiveAssessments.AsNoTracking()
                .Where(item => item.PersonId == personId && item.Status != "Superseded")
                .OrderByDescending(item => item.Version)
                .FirstOrDefaultAsync(cancellationToken);
            return assessment is null
                ? Results.Json<ComprehensiveAssessmentDto?>(null)
                : Results.Ok(ContractMapper.ToAssessment(assessment));
        });

        api.MapPost("/people/{personId:int}/assessments/draft", async Task<IResult> (
            int personId, int authorUserId, ClaimsPrincipal principal, ApiDbContext db,
            AuditTrail auditTrail, CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!await IsComprehensiveAssessmentAuthoringEnabledAsync(
                    db, actor.AgencyId, cancellationToken))
                return Results.NotFound();
            if (authorUserId != actor.UserId ||
                !await TenantAccess.OwnsPersonAsync(db, actor, personId, cancellationToken))
                return Results.NotFound();

            var editable = await db.ComprehensiveAssessments
                .Where(x => x.PersonId == personId && x.AuthorUserId == authorUserId &&
                            (x.Status == "Draft" || x.Status == "Returned"))
                .OrderByDescending(x => x.Version).FirstOrDefaultAsync(cancellationToken);
            if (editable is not null) return Results.Ok(ContractMapper.ToAssessment(editable));

            var approved = await db.ComprehensiveAssessments.AsNoTracking()
                .Where(x => x.PersonId == personId && x.Status == "Approved")
                .OrderByDescending(x => x.Version).FirstOrDefaultAsync(cancellationToken);
            var version = (await db.ComprehensiveAssessments.Where(x => x.PersonId == personId)
                .MaxAsync(x => (int?)x.Version, cancellationToken) ?? 0) + 1;
            var now = DateTime.UtcNow;
            var assessment = new ServerComprehensiveAssessment
            {
                PersonId = personId, AuthorUserId = authorUserId, Status = "Draft", Version = version,
                CreatedAt = now, UpdatedAt = now,
                DocumentJson = approved?.DocumentJson ?? "{\"contributors\":[],\"answers\":{},\"needs\":[]}"
            };
            db.ComprehensiveAssessments.Add(assessment);
            auditTrail.Record(actor, AuditActions.AssessmentCreated, "Person", personId);
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(ContractMapper.ToAssessment(assessment));
        });

        api.MapPut("/assessments/{assessmentId:int}/document", async Task<IResult> (
            int assessmentId, SaveAssessmentDocumentRequest request, ClaimsPrincipal principal,
            ApiDbContext db, AuditTrail auditTrail, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.DocumentJson) || request.DocumentJson.Length > 4_000_000)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["document"] = ["Assessment data is required and must not exceed 4 MB."] });
            try { using var _ = JsonDocument.Parse(request.DocumentJson); }
            catch (JsonException) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["document"] = ["Assessment data is invalid."] }); }
            var actor = Actor.From(principal);
            if (!await IsComprehensiveAssessmentAuthoringEnabledAsync(
                    db, actor.AgencyId, cancellationToken))
                return Results.NotFound();
            var assessment = await db.ComprehensiveAssessments.SingleOrDefaultAsync(x => x.Id == assessmentId, cancellationToken);
            if (assessment is null ||
                !await TenantAccess.CanAuthorAssessmentAsync(db, actor, assessment, cancellationToken))
                return Results.NotFound();
            if (assessment.Status is "Approved" or "Superseded") return Results.Conflict(new ApiErrorDto("assessment_locked", "Approved assessment versions cannot be changed.", string.Empty));
            if (request.ExpectedRevision != assessment.Revision)
                return StaleAssessmentConflict();
            assessment.DocumentJson = request.DocumentJson;
            assessment.UpdatedAt = DateTime.UtcNow;
            assessment.Revision++;
            auditTrail.Record(actor, AuditActions.AssessmentUpdated, "Assessment", assessmentId);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return StaleAssessmentConflict();
            }
            return Results.Ok(ContractMapper.ToAssessment(assessment));
        });

        api.MapPost("/assessments/{assessmentId:int}/submit", async Task<IResult> (
            int assessmentId, int authorUserId, int expectedRevision,
            ClaimsPrincipal principal, ApiDbContext db,
            AuditTrail auditTrail, CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!await IsComprehensiveAssessmentAuthoringEnabledAsync(
                    db, actor.AgencyId, cancellationToken))
                return Results.NotFound();
            var assessment = await db.ComprehensiveAssessments.SingleOrDefaultAsync(x => x.Id == assessmentId, cancellationToken);
            if (assessment is null || authorUserId != actor.UserId ||
                !await TenantAccess.CanAuthorAssessmentAsync(db, actor, assessment, cancellationToken))
                return Results.NotFound();
            if (assessment.Status is not ("Draft" or "Returned"))
                return Results.Conflict(new ApiErrorDto("assessment_locked", "This assessment is not editable.", string.Empty));
            if (expectedRevision != assessment.Revision)
                return StaleAssessmentConflict();
            assessment.Status = "ReadyForReview";
            assessment.SubmittedAt = DateTime.UtcNow;
            assessment.UpdatedAt = assessment.SubmittedAt.Value;
            assessment.Revision++;
            auditTrail.Record(actor, AuditActions.AssessmentSubmitted, "Assessment", assessmentId);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return StaleAssessmentConflict();
            }
            return Results.Ok(ContractMapper.ToAssessment(assessment));
        });

        api.MapGet("/people/{personId:int}/pcp-source", async Task<IResult> (
            int personId, int preferredAuthorUserId, ClaimsPrincipal principal, ApiDbContext db, CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!await IsPersonCenteredPlanAuthoringEnabledAsync(
                    db, actor.AgencyId, cancellationToken))
                return Results.NotFound();
            var person = await db.People.AsNoTracking().SingleOrDefaultAsync(
                x => x.Id == personId, cancellationToken);
            if (person is null || person.UserId != preferredAuthorUserId ||
                !await TenantAccess.CanAccessPersonAsync(db, actor, person, cancellationToken)) return Results.NotFound();
            var assessment = await db.ComprehensiveAssessments.AsNoTracking()
                .Where(x => x.PersonId == personId && x.Status == "Approved")
                .OrderByDescending(x => x.Version).FirstOrDefaultAsync(cancellationToken);
            assessment ??= await db.ComprehensiveAssessments.AsNoTracking()
                .Where(x => x.PersonId == personId && x.AuthorUserId == preferredAuthorUserId &&
                            (x.Status == "Draft" || x.Status == "Returned" || x.Status == "ReadyForReview"))
                .OrderByDescending(x => x.Version).FirstOrDefaultAsync(cancellationToken);
            var dto = assessment is null ? null : new PersonCenteredPlanSourceDto(
                assessment.Id, assessment.Version, assessment.Status, assessment.UpdatedAt, assessment.DocumentJson);
            return Results.Json(dto);
        });
    }


    private static void MapProviders(RouteGroupBuilder api)
    {
        api.MapGet("/providers", async (bool? passthroughOnly, ClaimsPrincipal principal, ApiDbContext db, CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            var query = db.Providers.AsNoTracking().Where(x => x.AgencyId == actor.AgencyId);
            if (passthroughOnly == true) query = query.Where(x => x.ProvidesPassthroughService);
            return (await query.OrderBy(x => x.Name).ToListAsync(cancellationToken)).Select(ContractMapper.ToProvider).ToList();
        });
        api.MapPost("/providers", async Task<IResult> (SaveProviderRequest request, ClaimsPrincipal principal, ApiDbContext db, CancellationToken cancellationToken) =>
        {
            // Anyone working a caseload may add and correct entries: the directory is only useful
            // if the person on the phone with a new specialist can record them straight away.
            // Removing and merging stay Admin-only, below.
            var actor = Actor.From(principal);
            if (!ProviderDirectoryRules.CanCreateOrEdit(actor.Permissions)) return Results.Forbid();
            var errors = ValidateProvider(request); if (errors.Count > 0) return Results.ValidationProblem(errors);
            var affiliationErrors = await ValidateProviderAffiliationAsync(db, actor.AgencyId, request, 0, cancellationToken);
            if (affiliationErrors.Count > 0) return Results.ValidationProblem(affiliationErrors);
            var duplicate = await FindDuplicateProviderAsync(db, actor.AgencyId, request, null, cancellationToken);
            if (duplicate is not null) return duplicate;
            var provider = new ServerProvider { AgencyId = actor.AgencyId }; ApplyProvider(provider, request); db.Providers.Add(provider);
            await db.SaveChangesAsync(cancellationToken); return Results.Ok(ContractMapper.ToProvider(provider));
        });
        api.MapPut("/providers/{id:int}", async Task<IResult> (int id, SaveProviderRequest request, ClaimsPrincipal principal, ApiDbContext db, CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!ProviderDirectoryRules.CanCreateOrEdit(actor.Permissions)) return Results.Forbid();
            var errors = ValidateProvider(request); if (errors.Count > 0) return Results.ValidationProblem(errors);
            var affiliationErrors = await ValidateProviderAffiliationAsync(db, actor.AgencyId, request, id, cancellationToken);
            if (affiliationErrors.Count > 0) return Results.ValidationProblem(affiliationErrors);
            var duplicate = await FindDuplicateProviderAsync(db, actor.AgencyId, request, id, cancellationToken);
            if (duplicate is not null) return duplicate;
            var provider = await db.Providers.SingleOrDefaultAsync(x => x.Id == id && x.AgencyId == actor.AgencyId, cancellationToken); if (provider is null) return Results.NotFound();
            ApplyProvider(provider, request); await db.SaveChangesAsync(cancellationToken); return Results.Ok(ContractMapper.ToProvider(provider));
        });
        // Delete stays Admin-only: the directory is shared, so removing an entry reaches other
        // case managers' consumers and is not undoable by the person who did it.
        api.MapDelete("/providers/{id:int}", async Task<IResult> (int id, ClaimsPrincipal principal, ApiDbContext db, CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!ProviderDirectoryRules.CanDeleteOrMerge(actor.Permissions)) return Results.Forbid();
            var provider = await db.Providers.SingleOrDefaultAsync(x => x.Id == id && x.AgencyId == actor.AgencyId, cancellationToken); if (provider is null) return Results.NotFound();
            // Refused before the settings default is cleared, so a rejected delete leaves
            // nothing changed. Restrict would raise a foreign-key error anyway; this names
            // the affiliated entries instead.
            var affiliated = await db.Providers.AsNoTracking()
                .Where(child => child.AgencyId == actor.AgencyId && child.ParentProviderId == id)
                .OrderBy(child => child.Name).Select(child => child.Name).ToListAsync(cancellationToken);
            if (affiliated.Count > 0)
                return Results.Conflict(new ApiErrorDto(
                    "provider_has_affiliated_entries",
                    ProviderAffiliation.AffiliatedChildrenMessage(provider.Name, affiliated),
                    string.Empty));
            // Also refused while any consumer record references it, ended links included.
            // Without this the foreign key raises a raw constraint error instead, which
            // reaches the Admin as an unexplained failure.
            var onRecords = await db.PersonProviders.AsNoTracking()
                .CountAsync(link => link.ProviderId == id, cancellationToken);
            if (onRecords > 0)
                return Results.Conflict(new ApiErrorDto(
                    "provider_on_consumer_records",
                    ConsumerProviderRules.ProviderOnConsumerRecordsMessage(provider.Name, onRecords),
                    string.Empty));
            var settings = await db.Settings.FirstOrDefaultAsync(x => x.AgencyId == actor.AgencyId && x.DefaultPassthroughProviderId == id, cancellationToken);
            if (settings is not null) settings.DefaultPassthroughProviderId = null;
            db.Providers.Remove(provider); await db.SaveChangesAsync(cancellationToken); return Results.NoContent();
        });

        MapProviderContacts(api);
        MapProviderMerge(api);
    }

    // Named people at a provider. Ordinary editing, so any caseload role may maintain them: a
    // phone number is not an entry other case managers' consumers point at.
    private static void MapProviderContacts(RouteGroupBuilder api)
    {
        api.MapGet("/providers/{providerId:int}/contacts", async Task<IResult> (
            int providerId, ClaimsPrincipal principal, ApiDbContext db, CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!await ProviderIsInAgencyAsync(db, actor.AgencyId, providerId, cancellationToken))
                return Results.NotFound();

            var contacts = await db.ProviderContacts.AsNoTracking()
                .Where(contact => contact.ProviderId == providerId)
                .OrderByDescending(contact => contact.IsPrimary)
                .ThenBy(contact => contact.SortOrder).ThenBy(contact => contact.Id)
                .ToListAsync(cancellationToken);
            return Results.Ok(contacts.Select(ContractMapper.ToProviderContact).ToList());
        });

        api.MapPost("/providers/{providerId:int}/contacts", async Task<IResult> (
            int providerId, SaveProviderContactRequest request, ClaimsPrincipal principal,
            ApiDbContext db, CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!ProviderDirectoryRules.CanCreateOrEdit(actor.Permissions)) return Results.Forbid();
            var errors = ProviderDirectoryRules.ValidateContact(request);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            if (!await ProviderIsInAgencyAsync(db, actor.AgencyId, providerId, cancellationToken))
                return Results.NotFound();

            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
            await DemoteOtherPrimaryContactsAsync(db, providerId, 0, request.IsPrimary, cancellationToken);
            var contact = new ServerProviderContact { ProviderId = providerId };
            ApplyProviderContact(contact, request);
            db.ProviderContacts.Add(contact);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Results.Ok(ContractMapper.ToProviderContact(contact));
        });

        api.MapPut("/providers/{providerId:int}/contacts/{contactId:int}", async Task<IResult> (
            int providerId, int contactId, SaveProviderContactRequest request, ClaimsPrincipal principal,
            ApiDbContext db, CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!ProviderDirectoryRules.CanCreateOrEdit(actor.Permissions)) return Results.Forbid();
            var errors = ProviderDirectoryRules.ValidateContact(request);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            if (!await ProviderIsInAgencyAsync(db, actor.AgencyId, providerId, cancellationToken))
                return Results.NotFound();

            var contact = await db.ProviderContacts.SingleOrDefaultAsync(
                candidate => candidate.Id == contactId && candidate.ProviderId == providerId, cancellationToken);
            if (contact is null) return Results.NotFound();

            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
            await DemoteOtherPrimaryContactsAsync(db, providerId, contactId, request.IsPrimary, cancellationToken);
            ApplyProviderContact(contact, request);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Results.Ok(ContractMapper.ToProviderContact(contact));
        });

        api.MapDelete("/providers/{providerId:int}/contacts/{contactId:int}", async Task<IResult> (
            int providerId, int contactId, ClaimsPrincipal principal,
            ApiDbContext db, CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!ProviderDirectoryRules.CanCreateOrEdit(actor.Permissions)) return Results.Forbid();
            if (!await ProviderIsInAgencyAsync(db, actor.AgencyId, providerId, cancellationToken))
                return Results.NotFound();

            var contact = await db.ProviderContacts.SingleOrDefaultAsync(
                candidate => candidate.Id == contactId && candidate.ProviderId == providerId, cancellationToken);
            if (contact is null) return Results.NotFound();

            db.ProviderContacts.Remove(contact);
            await db.SaveChangesAsync(cancellationToken);
            return Results.NoContent();
        });
    }

    // Folding one directory entry into another. Admin only, and deliberately does NOT repoint
    // AssessmentNeed.ProviderId: a document froze that entry, and rewriting it would change what
    // an approved assessment says.
    private static void MapProviderMerge(RouteGroupBuilder api)
    {
        api.MapPost("/providers/{survivingId:int}/merge", async Task<IResult> (
            int survivingId, MergeProvidersRequest request, ClaimsPrincipal principal,
            ApiDbContext db, AuditTrail auditTrail, CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!ProviderDirectoryRules.CanDeleteOrMerge(actor.Permissions)) return Results.Forbid();

            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

            var surviving = await db.Providers.SingleOrDefaultAsync(
                p => p.Id == survivingId && p.AgencyId == actor.AgencyId, cancellationToken);
            var merged = await db.Providers.SingleOrDefaultAsync(
                p => p.Id == request.MergedProviderId && p.AgencyId == actor.AgencyId, cancellationToken);
            if (surviving is null || merged is null) return Results.NotFound();

            var problem = ProviderDirectoryRules.ValidateMerge(
                ToAffiliationNode(surviving), ToAffiliationNode(merged));
            if (problem is not null)
                return Results.Conflict(new ApiErrorDto("provider_merge_invalid", problem, string.Empty));

            var identifierProblem =
                IdentifierConflict(surviving.Npi, merged.Npi, "National Provider Identifier")
                ?? IdentifierConflict(surviving.MaineCareProviderId, merged.MaineCareProviderId,
                    "MaineCare provider identifier");
            if (identifierProblem is not null)
                return Results.Conflict(new ApiErrorDto(
                    "provider_merge_identifier_conflict", identifierProblem, string.Empty));

            var directory = (await db.Providers.AsNoTracking()
                    .Where(p => p.AgencyId == actor.AgencyId)
                    .Select(p => new { p.Id, p.Name, p.ParentProviderId, p.MedicalKind })
                    .ToListAsync(cancellationToken))
                .Select(p => new ProviderAffiliationNode(p.Id, p.Name, p.ParentProviderId,
                    Enum.TryParse<MedicalProviderKind>(p.MedicalKind, out var kind) ? kind : null))
                .ToList();
            if (ProviderAffiliation.ResolveAncestors(surviving.Id, directory).Any(n => n.Id == merged.Id))
                return Results.Conflict(new ApiErrorDto(
                    "provider_merge_loop", ProviderDirectoryRules.MergeWouldCreateLoopMessage, string.Empty));

            var duplicateCurrentLinks = await (
                from incoming in db.PersonProviders.AsNoTracking()
                join existing in db.PersonProviders.AsNoTracking()
                    on incoming.PersonId equals existing.PersonId
                where incoming.ProviderId == merged.Id && incoming.EndDate == null &&
                      existing.ProviderId == surviving.Id && existing.EndDate == null
                select incoming.PersonId).Distinct().CountAsync(cancellationToken);
            if (duplicateCurrentLinks > 0)
            {
                return Results.Conflict(new ApiErrorDto(
                    "provider_merge_consumer_link_conflict",
                    ProviderDirectoryRules.MergeConsumerLinkConflictMessage(duplicateCurrentLinks),
                    string.Empty));
            }

            var affiliatedMoved = await db.Providers
                .Where(child => child.AgencyId == actor.AgencyId && child.ParentProviderId == merged.Id)
                .ExecuteUpdateAsync(u => u.SetProperty(child => child.ParentProviderId, surviving.Id), cancellationToken);
            var consumerLinksMoved = await db.PersonProviders
                .Where(link => link.ProviderId == merged.Id)
                .ExecuteUpdateAsync(u => u.SetProperty(link => link.ProviderId, surviving.Id), cancellationToken);
            // Release obligations retain their recipient-name snapshot and stable
            // assignment identity, but their directory pointer must follow the same
            // canonical-provider merge. Otherwise the restricted foreign key makes
            // the merge fail after an exact recipient obligation has been created.
            var releaseObligationsMoved = await db.ReleaseObligations
                .Where(obligation => obligation.AgencyId == actor.AgencyId &&
                                     obligation.RecipientProviderId == merged.Id)
                .ExecuteUpdateAsync(u => u.SetProperty(
                    obligation => obligation.RecipientProviderId, surviving.Id), cancellationToken);
            var contactsMoved = await db.ProviderContacts
                .Where(contact => contact.ProviderId == merged.Id)
                .ExecuteUpdateAsync(u => u
                    .SetProperty(contact => contact.ProviderId, surviving.Id)
                    .SetProperty(contact => contact.IsPrimary, false), cancellationToken);
            await db.Settings
                .Where(s => s.AgencyId == actor.AgencyId && s.DefaultPassthroughProviderId == merged.Id)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.DefaultPassthroughProviderId, surviving.Id), cancellationToken);

            // Adopted only where the survivor has none, so a merge never overwrites a fact
            // somebody deliberately recorded on the surviving entry.
            surviving.Npi ??= merged.Npi;
            surviving.MaineCareProviderId ??= merged.MaineCareProviderId;
            surviving.ParentProviderId ??= merged.ParentProviderId == surviving.Id ? null : merged.ParentProviderId;

            db.Providers.Remove(merged);
            auditTrail.Record(
                actor,
                AuditActions.ProviderMerged,
                "Provider",
                surviving.Id,
                JsonSerializer.Serialize(new
                {
                    mergedProviderId = merged.Id,
                    affiliatedMoved,
                    consumerLinksMoved,
                    releaseObligationsMoved,
                    contactsMoved
                }));
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return Results.Ok(new MergeProvidersResultDto(
                surviving.Id,
                ProviderDirectoryRules.MergeSummary(
                    surviving.Name, merged.Name, affiliatedMoved, consumerLinksMoved, contactsMoved)));
        });
    }

    private static ProviderAffiliationNode ToAffiliationNode(ServerProvider provider) =>
        new(provider.Id, provider.Name, provider.ParentProviderId,
            Enum.TryParse<MedicalProviderKind>(provider.MedicalKind, out var kind) ? kind : null);

    private static string? IdentifierConflict(string? surviving, string? merged, string which) =>
        Normalize(surviving) is { } left && Normalize(merged) is { } right &&
        !string.Equals(left, right, StringComparison.OrdinalIgnoreCase)
            ? ProviderDirectoryRules.ConflictingIdentifierMessage(which)
            : null;

    private static Task<bool> ProviderIsInAgencyAsync(
        ApiDbContext db, int agencyId, int providerId, CancellationToken cancellationToken) =>
        db.Providers.AsNoTracking().AnyAsync(p => p.Id == providerId && p.AgencyId == agencyId, cancellationToken);

    private static async Task DemoteOtherPrimaryContactsAsync(
        ApiDbContext db, int providerId, int editingContactId, bool isPrimary, CancellationToken cancellationToken)
    {
        if (!isPrimary) return;
        await db.ProviderContacts
            .Where(other => other.ProviderId == providerId && other.Id != editingContactId && other.IsPrimary)
            .ExecuteUpdateAsync(u => u.SetProperty(other => other.IsPrimary, false), cancellationToken);
    }

    private static void ApplyProviderContact(ServerProviderContact contact, SaveProviderContactRequest request)
    {
        contact.Name = request.Name.Trim();
        contact.Role = Normalize(request.Role);
        contact.Phone = Normalize(request.Phone);
        contact.Extension = Normalize(request.Extension);
        contact.Email = Normalize(request.Email);
        contact.IsPrimary = request.IsPrimary;
        contact.SortOrder = request.SortOrder;
    }

    private static void MapAtRequests(RouteGroupBuilder api)
    {
        api.MapGet("/at-requests", async Task<IResult> (int userId, ClaimsPrincipal principal, ApiDbContext db, CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal); if (!await TenantAccess.CanAccessUserAsync(db, actor, userId, cancellationToken)) return Results.Forbid();
            var rate = (await GetOrCreateSettingsAsync(db, actor.AgencyId, cancellationToken)).PassthroughRate;
            var requests = await (from request in db.AtRequests.AsNoTracking()
                                  join person in db.People on request.PersonId equals person.Id
                                  where person.UserId == userId && person.AgencyId == actor.AgencyId
                                  select new AtRequestRow
                                  {
                                      Id = request.Id, ClientName = request.ClientName, Status = request.Status,
                                      SalesTax = request.SalesTax, SubmittedDate = request.SubmittedDate,
                                      VendorName = request.VendorName, CaseManagerName = request.CaseManagerName,
                                      PassthroughRate = request.PassthroughRate, SignedByName = request.SignedByName,
                                      SignedAtUtc = request.SignedAtUtc, HasSnapshot = request.SnapshotPng != null
                                  })
                .OrderByDescending(x => x.SubmittedDate).ToListAsync(cancellationToken);
            return Results.Ok(await BuildAtRequestRowsAsync(db, requests, rate, cancellationToken));
        });

        // The same list narrowed to one client, for the AT requests section of a
        // client's profile. Gated on the CLIENT's owning user, so reaching another
        // agency's client here fails the same way it does everywhere else.
        api.MapGet("/people/{personId:int}/at-requests", async Task<IResult> (
            int personId, ClaimsPrincipal principal, ApiDbContext db, CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            var person = await db.People.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == personId, cancellationToken);
            if (person is null || !await TenantAccess.CanAccessPersonAsync(db, actor, person, cancellationToken))
                return Results.NotFound();

            var rate = (await GetOrCreateSettingsAsync(db, actor.AgencyId, cancellationToken)).PassthroughRate;
            var requests = await db.AtRequests.AsNoTracking()
                .Where(request => request.PersonId == personId)
                .Select(request => new AtRequestRow
                {
                    Id = request.Id, ClientName = request.ClientName, Status = request.Status,
                    SalesTax = request.SalesTax, SubmittedDate = request.SubmittedDate,
                    VendorName = request.VendorName, CaseManagerName = request.CaseManagerName,
                    PassthroughRate = request.PassthroughRate, SignedByName = request.SignedByName,
                    SignedAtUtc = request.SignedAtUtc, HasSnapshot = request.SnapshotPng != null
                })
                .OrderByDescending(x => x.SubmittedDate).ThenByDescending(x => x.Id)
                .ToListAsync(cancellationToken);
            return Results.Ok(await BuildAtRequestRowsAsync(db, requests, rate, cancellationToken));
        });
        api.MapGet("/at-requests/{id:int}", async Task<IResult> (int id, ClaimsPrincipal principal, ApiDbContext db, CancellationToken cancellationToken) =>
        {
            var request = await LoadAccessibleAtRequestAsync(db, Actor.From(principal), id, cancellationToken);
            return request is null ? Results.NotFound() : Results.Ok(ContractMapper.ToAtRequest(request));
        });
        api.MapGet("/at-requests/{id:int}/snapshot", async Task<IResult> (int id, ClaimsPrincipal principal, ApiDbContext db, CancellationToken cancellationToken) =>
        {
            var request = await LoadAccessibleAtRequestAsync(db, Actor.From(principal), id, cancellationToken);
            return request is null ? Results.NotFound() : Results.Ok(new BinaryPayloadDto(
                request.SnapshotPng is null ? null : Convert.ToBase64String(request.SnapshotPng)));
        });
        api.MapPost("/at-requests", async Task<IResult> (SaveAtRequestRequest input, ClaimsPrincipal principal, ApiDbContext db, CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            var person = await db.People.AsNoTracking().SingleOrDefaultAsync(x => x.Id == input.PersonId, cancellationToken);
            if (person is null || !await TenantAccess.CanAccessPersonAsync(db, actor, person, cancellationToken)) return Results.NotFound();
            var owner = await db.Users.AsNoTracking().SingleAsync(x => x.Id == person.UserId, cancellationToken);
            var agency = await db.Agencies.AsNoTracking().SingleOrDefaultAsync(x => x.Id == actor.AgencyId, cancellationToken);
            var errors = ValidateAtRequest(input); if (errors.Count > 0) return Results.ValidationProblem(errors);
            var request = new ServerAtRequest { PersonId = person.Id,
                ClientName = $"{person.FirstName} {person.LastName}".Trim(), ClientEvergreenId = person.EvergreenId,
                CaseManagerName = owner.DisplayName, CaseManagerEmail = owner.Email, CaseManagerPhone = owner.Phone,
                CaseManagerAgency = agency?.Name };
            ApplyAtRequest(request, input); db.AtRequests.Add(request); await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(ContractMapper.ToAtRequest(request));
        });
        api.MapPut("/at-requests/{id:int}", async Task<IResult> (int id, SaveAtRequestRequest input, ClaimsPrincipal principal, ApiDbContext db, CancellationToken cancellationToken) =>
        {
            var request = await LoadAccessibleAtRequestAsync(db, Actor.From(principal), id, cancellationToken);
            if (request is null || request.PersonId != input.PersonId) return Results.NotFound();
            if (request.Revision != input.ExpectedRevision) return StaleAtRequestConflict();
            // The publication lock, checked against the stored row. Publishing does
            // not come through here; it has its own route.
            if (IsAtRequestPublished(request)) return PublishedAtRequestConflict();
            var errors = ValidateAtRequest(input); if (errors.Count > 0) return Results.ValidationProblem(errors);
            ApplyAtRequest(request, input); request.Revision++;
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return StaleAtRequestConflict();
            }
            return Results.Ok(ContractMapper.ToAtRequest(request));
        });
        // Publish. The attestation is derived HERE, from the validated actor, and
        // there is deliberately no way for a caller to supply a signer name: the
        // server records who published, not who the client says published.
        //
        // ValidatedActorFilter has already re-confirmed the claimed identity, role,
        // and agency against the database, and LoadAccessibleAtRequestAsync gates
        // on TenantAccess, so by this point the actor is a real user who may reach
        // this client's request.
        api.MapPost("/at-requests/{id:int}/publish", async Task<IResult> (
            int id, PublishAtRequestRequest input, ClaimsPrincipal principal,
            ApiDbContext db, AuditTrail audit, CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            var request = await LoadAccessibleAtRequestAsync(db, actor, id, cancellationToken);
            if (request is null) return Results.NotFound();
            if (request.Revision != input.ExpectedRevision) return StaleAtRequestConflict();
            if (IsAtRequestPublished(request)) return PublishedAtRequestConflict();

            // Completeness is decided by the shared rule owner, not re-expressed
            // here. A second copy of these checks would be a rule enforced two ways.
            var blockers = AtRequestPublication.FindBlockers(
                request.VendorName, request.VendorBillingLocation,
                request.Items.Select(item => new AtRequestLine(item.Name, item.ItemCost, item.Quantity)),
                alreadyPublished: false);
            if (blockers.Count > 0)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["publish"] = [.. blockers] });

            var signedAtUtc = DateTime.UtcNow;

            // Frozen from agency settings, never from the payload. Regenerating
            // this document next year must reproduce the money it was filed at,
            // not recompute it against whatever rate the agency has by then.
            request.PassthroughRate =
                (await GetOrCreateSettingsAsync(db, actor.AgencyId, cancellationToken)).PassthroughRate;

            request.SignedByName = actor.DisplayName;
            request.SignedByRole = actor.Role;
            request.SignedByUserId = actor.UserId;
            request.SignedAtUtc = signedAtUtc;
            request.AttestationStatement = AtRequestPublication.AttestationStatement;
            request.SubmittedDate = signedAtUtc.Date;
            request.Status = "Review";
            request.Revision++;

            audit.Record(actor, AuditActions.AtRequestPublished, "AtRequest", request.Id);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return StaleAtRequestConflict();
            }
            return Results.Ok(ContractMapper.ToAtRequest(request));
        });

        // Reopen for correction. Audited on its own action because discarding an
        // attestation is a materially different event from making one, and a
        // reviewer reading the trail should not have to infer it from a gap.
        api.MapPost("/at-requests/{id:int}/reopen", async Task<IResult> (
            int id, ReopenAtRequestRequest input, ClaimsPrincipal principal,
            ApiDbContext db, AuditTrail audit, CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            var request = await LoadAccessibleAtRequestAsync(db, actor, id, cancellationToken);
            if (request is null) return Results.NotFound();
            if (request.Revision != input.ExpectedRevision) return StaleAtRequestConflict();
            if (!IsAtRequestPublished(request))
                return Results.Ok(ContractMapper.ToAtRequest(request));

            audit.Record(actor, AuditActions.AtRequestReopened, "AtRequest", request.Id,
                JsonSerializer.Serialize(new
                {
                    discardedSigner = request.SignedByName,
                    discardedSignedAtUtc = request.SignedAtUtc
                }));

            request.SignedByName = null;
            request.SignedByRole = null;
            request.SignedByUserId = null;
            request.SignedAtUtc = null;
            request.AttestationStatement = null;
            request.SubmittedDate = null;
            // A draft again, so the live agency rate governs.
            request.PassthroughRate = null;
            request.Status = "Development";
            request.Revision++;

            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return StaleAtRequestConflict();
            }
            return Results.Ok(ContractMapper.ToAtRequest(request));
        });
        api.MapDelete("/at-requests/{id:int}", async Task<IResult> (int id, int? expectedRevision, ClaimsPrincipal principal, ApiDbContext db, CancellationToken cancellationToken) =>
        {
            var request = await LoadAccessibleAtRequestAsync(db, Actor.From(principal), id, cancellationToken);
            if (request is null) return Results.NotFound();
            if (request.Revision != expectedRevision) return StaleAtRequestConflict();
            // A published request is a document someone attested to. Deleting it
            // outright is not a correction; reopening it is.
            if (IsAtRequestPublished(request)) return PublishedAtRequestConflict();
            db.AtRequests.Remove(request);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return StaleAtRequestConflict();
            }
            return Results.NoContent();
        });
    }

    private static void MapCheckRequests(RouteGroupBuilder api)
    {
        api.MapGet("/people/{personId:int}/check-request-template", async Task<IResult> (
            int personId, ClaimsPrincipal principal, ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!await TenantAccess.OwnsPersonAsync(db, actor, personId, cancellationToken))
                return Results.NotFound();
            var template = await db.CheckRequestTemplates.AsNoTracking()
                .SingleOrDefaultAsync(item => item.PersonId == personId, cancellationToken);
            return Results.Ok(template is null ? null : ToCheckRequestTemplateDto(template));
        });

        api.MapPut("/people/{personId:int}/check-request-template", async Task<IResult> (
            int personId, SaveCheckRequestTemplateRequest input, ClaimsPrincipal principal,
            ApiDbContext db, AuditTrail audit, ApiClock clock,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!await TenantAccess.OwnsPersonAsync(db, actor, personId, cancellationToken))
                return Results.NotFound();
            var person = await db.People.AsNoTracking().SingleAsync(
                item => item.Id == personId, cancellationToken);
            if (!person.CaseManagerIsRepPayee)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["personId"] = ["Representative Payee must be enabled for this consumer before saving a weekly default."]
                });
            var errors = CheckRequestTemplateRules.Validate(
                input.GenerateOn, input.NeededByDaysAfterRequest, input.PayableTo,
                input.MailingAddress, input.Amount, input.Reason);
            if (errors.Count > 0)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["template"] = [.. errors] });

            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
            var template = await db.CheckRequestTemplates.SingleOrDefaultAsync(
                item => item.PersonId == personId, cancellationToken);
            var now = clock.UtcNow.UtcDateTime;
            if (template is null)
            {
                if (input.ExpectedRevision != 0) return StaleCheckRequestTemplateConflict();
                template = new ServerCheckRequestTemplate
                {
                    PersonId = personId,
                    Revision = 1,
                    EffectiveFrom = clock.Today,
                    CreatedAtUtc = now
                };
                db.CheckRequestTemplates.Add(template);
            }
            else
            {
                if (template.Revision != input.ExpectedRevision) return StaleCheckRequestTemplateConflict();
                template.Revision++;
            }

            template.IsEnabled = input.IsEnabled;
            template.GenerateOn = input.GenerateOn;
            template.NeededByDaysAfterRequest = input.NeededByDaysAfterRequest;
            template.PayableTo = input.PayableTo!.Trim();
            template.MailingAddress = input.MailingAddress!.Trim();
            template.Amount = input.Amount;
            template.Reason = input.Reason!.Trim();
            template.UpdatedAtUtc = now;
            audit.Record(actor, AuditActions.CheckRequestTemplateUpdated, "Person", personId);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return StaleCheckRequestTemplateConflict();
            }
            catch (DbUpdateException)
            {
                return StaleCheckRequestTemplateConflict();
            }
            return Results.Ok(ToCheckRequestTemplateDto(template));
        });

        api.MapGet("/check-requests/generated-drafts/pending", async Task<IResult> (
            ClaimsPrincipal principal, ApiDbContext db, CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!actor.HasCaseManagerPermissions) return Results.Forbid();
            return Results.Ok(await LoadPendingGeneratedCheckRequestDraftsAsync(
                db, actor, cancellationToken));
        });

        api.MapPost("/check-requests/weekly-drafts/ensure", async Task<IResult> (
            ClaimsPrincipal principal, ApiDbContext db, AuditTrail audit, ApiClock clock,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!actor.HasCaseManagerPermissions) return Results.Forbid();
            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
            var templates = await (from template in db.CheckRequestTemplates
                join person in db.People on template.PersonId equals person.Id
                where template.IsEnabled && person.UserId == actor.UserId &&
                      person.AgencyId == actor.AgencyId && person.CaseManagerIsRepPayee
                select new { Template = template, Person = person }).ToListAsync(cancellationToken);
            var templateIds = templates.Select(item => item.Template.Id).ToList();
            var pendingTemplateIds = templateIds.Count == 0
                ? new List<int>()
                : await db.CheckRequests.AsNoTracking()
                    .Where(request => request.TemplateId != null &&
                        templateIds.Contains(request.TemplateId.Value) &&
                        !db.CheckRequestWorkflowEvents.Any(workflow =>
                            workflow.CheckRequestId == request.Id &&
                            workflow.Action == CheckRequestWorkflowAction.Submitted))
                    .Select(request => request.TemplateId!.Value)
                    .Distinct()
                    .ToListAsync(cancellationToken);
            var pendingSet = pendingTemplateIds.ToHashSet();
            var owner = await db.Users.AsNoTracking().SingleAsync(
                user => user.Id == actor.UserId && user.AgencyId == actor.AgencyId,
                cancellationToken);
            var agencyName = await db.Agencies.AsNoTracking()
                .Where(agency => agency.Id == actor.AgencyId).Select(agency => agency.Name)
                .SingleAsync(cancellationToken);
            var supervisorName = owner.SupervisorId is int supervisorId
                ? await db.Users.AsNoTracking().Where(user => user.Id == supervisorId)
                    .Select(user => user.DisplayName).SingleOrDefaultAsync(cancellationToken) ?? string.Empty
                : string.Empty;
            var now = clock.UtcNow.UtcDateTime;
            var created = 0;

            foreach (var item in templates)
            {
                if (pendingSet.Contains(item.Template.Id)) continue;
                var scheduledFor = WeeklyCheckRequestSchedule.MostRecentOccurrence(
                    item.Template.GenerateOn, item.Template.EffectiveFrom, clock.Today);
                if (scheduledFor is null) continue;
                if (await db.CheckRequests.AsNoTracking().AnyAsync(request =>
                        request.TemplateId == item.Template.Id &&
                        request.ScheduledForDate == scheduledFor.Value, cancellationToken))
                    continue;

                db.CheckRequests.Add(new ServerCheckRequest
                {
                    PersonId = item.Person.Id,
                    ConsumerName = $"{item.Person.FirstName} {item.Person.LastName}".Trim(),
                    AgencyName = agencyName,
                    CaseManagerName = owner.DisplayName,
                    SupervisorName = supervisorName,
                    RequestDate = scheduledFor.Value,
                    PayableTo = item.Template.PayableTo,
                    MailingAddress = item.Template.MailingAddress,
                    Amount = item.Template.Amount,
                    NeededByDate = scheduledFor.Value.AddDays(item.Template.NeededByDaysAfterRequest),
                    Reason = item.Template.Reason,
                    TemplateId = item.Template.Id,
                    ScheduledForDate = scheduledFor.Value,
                    CreatedAtUtc = now
                });
                audit.Record(actor, AuditActions.CheckRequestDraftGenerated, "Person", item.Person.Id,
                    JsonSerializer.Serialize(new
                    {
                        templateId = item.Template.Id,
                        scheduledForDate = scheduledFor.Value
                    }));
                created++;
            }

            try
            {
                if (created > 0) await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                return Results.Conflict(new ApiErrorDto(
                    "weekly_check_request_generation_conflict",
                    "Weekly drafts changed while Sati was generating them. Reload and try again.",
                    string.Empty));
            }
            var pending = await LoadPendingGeneratedCheckRequestDraftsAsync(
                db, actor, cancellationToken);
            return Results.Ok(new WeeklyCheckRequestDraftResultDto(created, pending));
        });

        api.MapGet("/check-requests/time-off-collisions", async Task<IResult> (
            DateTime date, ClaimsPrincipal principal, ApiDbContext db, ApiClock clock,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!actor.HasCaseManagerPermissions) return Results.Forbid();
            return Results.Ok(await LoadTimeOffCheckRequestCollisionsAsync(
                db, actor, date.Date, clock.Today, cancellationToken));
        });

        api.MapPost("/check-requests/time-off-drafts/ensure", async Task<IResult> (
            EnsureTimeOffCheckRequestDraftsRequest input, ClaimsPrincipal principal,
            ApiDbContext db, AuditTrail audit, ApiClock clock,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!actor.HasCaseManagerPermissions) return Results.Forbid();
            var date = input.TimeOffDate.Date;
            if (date < clock.Today)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["timeOffDate"] = ["Choose today or a future scheduled day off."]
                });
            if (!await db.ExemptDates.AsNoTracking().AnyAsync(item =>
                    item.UserId == actor.UserId && item.Date == date, cancellationToken))
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["timeOffDate"] = ["That date is not currently marked as time off on your calendar."]
                });

            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
            var templates = await (from template in db.CheckRequestTemplates
                join person in db.People on template.PersonId equals person.Id
                where template.IsEnabled && template.GenerateOn == date.DayOfWeek &&
                      template.EffectiveFrom <= date && person.UserId == actor.UserId &&
                      person.AgencyId == actor.AgencyId && person.CaseManagerIsRepPayee
                select new { Template = template, Person = person }).ToListAsync(cancellationToken);
            var templateIds = templates.Select(item => item.Template.Id).ToList();
            var pendingTemplateIds = templateIds.Count == 0
                ? new List<int>()
                : await db.CheckRequests.AsNoTracking()
                    .Where(request => request.TemplateId != null &&
                        templateIds.Contains(request.TemplateId.Value) &&
                        !db.CheckRequestWorkflowEvents.Any(workflow =>
                            workflow.CheckRequestId == request.Id &&
                            workflow.Action == CheckRequestWorkflowAction.Submitted))
                    .Select(request => request.TemplateId!.Value).Distinct()
                    .ToListAsync(cancellationToken);
            var pendingSet = pendingTemplateIds.ToHashSet();
            var owner = await db.Users.AsNoTracking().SingleAsync(
                user => user.Id == actor.UserId && user.AgencyId == actor.AgencyId,
                cancellationToken);
            var agencyName = await db.Agencies.AsNoTracking()
                .Where(agency => agency.Id == actor.AgencyId).Select(agency => agency.Name)
                .SingleAsync(cancellationToken);
            var supervisorName = owner.SupervisorId is int supervisorId
                ? await db.Users.AsNoTracking().Where(user => user.Id == supervisorId)
                    .Select(user => user.DisplayName).SingleOrDefaultAsync(cancellationToken) ?? string.Empty
                : string.Empty;
            var now = clock.UtcNow.UtcDateTime;
            var created = 0;

            foreach (var item in templates)
            {
                if (pendingSet.Contains(item.Template.Id)) continue;
                if (await db.CheckRequests.AsNoTracking().AnyAsync(request =>
                        request.TemplateId == item.Template.Id &&
                        request.ScheduledForDate == date, cancellationToken))
                    continue;

                db.CheckRequests.Add(new ServerCheckRequest
                {
                    PersonId = item.Person.Id,
                    ConsumerName = $"{item.Person.FirstName} {item.Person.LastName}".Trim(),
                    AgencyName = agencyName,
                    CaseManagerName = owner.DisplayName,
                    SupervisorName = supervisorName,
                    RequestDate = clock.Today,
                    PayableTo = item.Template.PayableTo,
                    MailingAddress = item.Template.MailingAddress,
                    Amount = item.Template.Amount,
                    NeededByDate = date.AddDays(item.Template.NeededByDaysAfterRequest),
                    Reason = item.Template.Reason,
                    TemplateId = item.Template.Id,
                    ScheduledForDate = date,
                    CreatedAtUtc = now
                });
                audit.Record(actor, AuditActions.CheckRequestDraftGenerated,
                    "Person", item.Person.Id, JsonSerializer.Serialize(new
                    {
                        templateId = item.Template.Id,
                        scheduledForDate = date,
                        trigger = "time-off"
                    }));
                created++;
            }

            try
            {
                if (created > 0) await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                return Results.Conflict(new ApiErrorDto(
                    "time_off_check_request_generation_conflict",
                    "Check-request drafts changed while Sati was preparing for time off. Reload and try again.",
                    string.Empty));
            }

            var pending = await LoadPendingGeneratedCheckRequestDraftsAsync(
                db, actor, cancellationToken);
            return Results.Ok(new WeeklyCheckRequestDraftResultDto(
                created, pending.Where(item => templateIds.Contains(item.TemplateId)).ToList()));
        });

        api.MapGet("/check-requests/supervisor-queue", async Task<IResult> (
            ClaimsPrincipal principal, ApiDbContext db, CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!actor.HasSupervisorPermissions) return Results.Forbid();
            var agencyWide = actor.HasAgencyWideSupervisionPermissions;
            var requests = await (from request in db.CheckRequests.AsNoTracking()
                join person in db.People.AsNoTracking() on request.PersonId equals person.Id
                join owner in db.Users.AsNoTracking() on person.UserId equals owner.Id
                where person.AgencyId == actor.AgencyId &&
                      (agencyWide || owner.SupervisorId == actor.UserId)
                select request).ToListAsync(cancellationToken);
            return Results.Ok(await BuildCheckRequestQueueAsync(db, requests,
                status => status == CheckRequestWorkflowStatus.Submitted, cancellationToken));
        });

        api.MapGet("/representative-payee/check-requests", async Task<IResult> (
            ClaimsPrincipal principal, ApiDbContext db, CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!actor.HasRepresentativePayeePermissions) return Results.Forbid();
            var requests = await (from request in db.CheckRequests.AsNoTracking()
                join person in db.People.AsNoTracking() on request.PersonId equals person.Id
                where person.AgencyId == actor.AgencyId && person.CaseManagerIsRepPayee
                select request).ToListAsync(cancellationToken);
            return Results.Ok(await BuildCheckRequestQueueAsync(db, requests,
                status => status is CheckRequestWorkflowStatus.Approved or
                    CheckRequestWorkflowStatus.Released or CheckRequestWorkflowStatus.ReceiptAcknowledged,
                cancellationToken));
        });

        api.MapGet("/representative-payee/consumers", async Task<IResult> (
            ClaimsPrincipal principal, ApiDbContext db, CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!actor.HasRepresentativePayeePermissions) return Results.Forbid();
            var consumers = await db.People.AsNoTracking()
                .Where(person => person.AgencyId == actor.AgencyId && person.CaseManagerIsRepPayee)
                .OrderBy(person => person.LastName).ThenBy(person => person.FirstName)
                .Select(person => new RepresentativePayeeConsumerDto(
                    person.Id, (person.FirstName + " " + person.LastName).Trim(), person.RepPayeeMonthlyIncome))
                .ToListAsync(cancellationToken);
            return Results.Ok(consumers);
        });

        api.MapGet("/representative-payee/consumers/{personId:int}/ledger", async Task<IResult> (
            int personId, ClaimsPrincipal principal, ApiDbContext db, CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!actor.HasRepresentativePayeePermissions) return Results.Forbid();
            var consumer = await db.People.AsNoTracking()
                .Where(person => person.Id == personId && person.AgencyId == actor.AgencyId &&
                                 person.CaseManagerIsRepPayee)
                .Select(person => new RepresentativePayeeConsumerDto(
                    person.Id, (person.FirstName + " " + person.LastName).Trim(), person.RepPayeeMonthlyIncome))
                .SingleOrDefaultAsync(cancellationToken);
            if (consumer is null) return Results.NotFound();
            var entries = await db.RepresentativePayeeLedgerEntries.AsNoTracking()
                .Where(entry => entry.PersonId == personId)
                .OrderByDescending(entry => entry.EntryDate).ThenByDescending(entry => entry.Id)
                .Select(entry => new RepresentativePayeeLedgerEntryDto(
                    entry.Id, entry.PersonId, entry.CheckRequestId, entry.EntryDate, entry.Kind,
                    entry.Amount, entry.Description, entry.RecordedAtUtc, entry.RecordedByUserId,
                    entry.RecordedByName))
                .ToListAsync(cancellationToken);
            return Results.Ok(new RepresentativePayeeWorkspaceDto(
                consumer, entries, entries.Sum(entry => entry.Amount)));
        });

        api.MapPost("/representative-payee/ledger-entries", async Task<IResult> (
            AddRepresentativePayeeLedgerEntryRequest input, ClaimsPrincipal principal,
            ApiDbContext db, AuditTrail audit, CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!actor.HasRepresentativePayeePermissions) return Results.Forbid();
            var errors = RepresentativePayeeLedgerRules.ValidateManualEntry(
                input.EntryDate, input.Amount, input.Description);
            if (errors.Count > 0)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["entry"] = [.. errors] });
            if (!await db.People.AsNoTracking().AnyAsync(person =>
                    person.Id == input.PersonId && person.AgencyId == actor.AgencyId &&
                    person.CaseManagerIsRepPayee, cancellationToken))
                return Results.NotFound();
            var entry = new ServerRepresentativePayeeLedgerEntry
            {
                PersonId = input.PersonId,
                EntryDate = input.EntryDate!.Value.Date,
                Kind = input.Amount > 0 ? RepresentativePayeeLedgerEntryKind.Deposit :
                    RepresentativePayeeLedgerEntryKind.Expense,
                Amount = input.Amount,
                Description = input.Description!.Trim(),
                RecordedAtUtc = DateTime.UtcNow,
                RecordedByUserId = actor.UserId,
                RecordedByName = actor.DisplayName
            };
            await using var transaction = await db.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable, cancellationToken);
            db.RepresentativePayeeLedgerEntries.Add(entry);
            await db.SaveChangesAsync(cancellationToken);
            audit.Record(actor, AuditActions.RepresentativePayeeLedgerEntryAdded,
                "Person", input.PersonId,
                JsonSerializer.Serialize(new { ledgerEntryId = entry.Id, entry.Kind }));
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Results.Ok(ToLedgerDto(entry));
        });

        api.MapPost("/check-requests/{id:int}/workflow", async Task<IResult> (
            int id, ApplyCheckRequestWorkflowActionRequest input, ClaimsPrincipal principal,
            ApiDbContext db, AuditTrail audit, CancellationToken cancellationToken) =>
            await ApplyCheckRequestWorkflowActionAsync(
                id, input, principal, db, audit, cancellationToken));

        api.MapGet("/people/{personId:int}/check-requests", async Task<IResult> (
            int personId, ClaimsPrincipal principal, ApiDbContext db, CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            var person = await db.People.AsNoTracking().SingleOrDefaultAsync(x => x.Id == personId, cancellationToken);
            if (person is null || !await TenantAccess.CanAccessPersonAsync(db, actor, person, cancellationToken))
                return Results.NotFound();

            var requests = await db.CheckRequests.AsNoTracking()
                .Where(x => x.PersonId == personId)
                .OrderByDescending(x => x.RequestDate)
                .ThenByDescending(x => x.Id)
                .ToListAsync(cancellationToken);
            var requestIds = requests.Select(x => x.Id).ToList();
            var actions = await db.CheckRequestWorkflowEvents.AsNoTracking()
                .Where(x => requestIds.Contains(x.CheckRequestId))
                .Select(x => new { x.CheckRequestId, x.Action })
                .ToListAsync(cancellationToken);
            var rows = requests.Select(x => new CheckRequestListItemDto(
                x.Id, x.Revision, x.RequestDate, x.PayableTo, x.Amount, x.NeededByDate,
                x.PublishedAtUtc, CheckRequestWorkflowRules.Resolve(x.PublishedAtUtc is not null,
                    actions.Where(e => e.CheckRequestId == x.Id).Select(e => e.Action)),
                x.TemplateId, x.ScheduledForDate))
                .ToList();
            return Results.Ok(rows);
        });

        api.MapGet("/check-requests/{id:int}", async Task<IResult> (
            int id, ClaimsPrincipal principal, ApiDbContext db, CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            var request = await db.CheckRequests.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
            if (request is null) return Results.NotFound();
            var person = await db.People.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.PersonId, cancellationToken);
            if (person is null || !await TenantAccess.CanAccessPersonAsync(db, actor, person, cancellationToken))
                return Results.NotFound();
            var actions = await db.CheckRequestWorkflowEvents.AsNoTracking()
                .Where(x => x.CheckRequestId == id).Select(x => x.Action).ToListAsync(cancellationToken);
            return Results.Ok(ContractMapper.ToCheckRequest(request,
                CheckRequestWorkflowRules.Resolve(request.PublishedAtUtc is not null, actions)));
        });

        api.MapPost("/check-requests", async Task<IResult> (
            CreateCheckRequestRequest input, ClaimsPrincipal principal, ApiDbContext db, CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!await TenantAccess.OwnsPersonAsync(db, actor, input.PersonId, cancellationToken))
                return Results.NotFound();

            var person = await db.People.AsNoTracking().SingleAsync(x => x.Id == input.PersonId, cancellationToken);
            if (!person.CaseManagerIsRepPayee)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["personId"] = ["Representative Payee must be enabled for this consumer before creating a check request."]
                });
            var owner = await db.Users.AsNoTracking().SingleAsync(x => x.Id == person.UserId, cancellationToken);
            var supervisorName = owner.SupervisorId is int supervisorId
                ? await db.Users.AsNoTracking().Where(x => x.Id == supervisorId).Select(x => x.DisplayName)
                    .SingleOrDefaultAsync(cancellationToken) ?? string.Empty
                : string.Empty;
            var agencyName = await db.Agencies.AsNoTracking().Where(x => x.Id == actor.AgencyId)
                .Select(x => x.Name).SingleOrDefaultAsync(cancellationToken) ?? string.Empty;
            var now = DateTime.UtcNow;
            var request = new ServerCheckRequest
            {
                PersonId = person.Id,
                ConsumerName = $"{person.FirstName} {person.LastName}".Trim(),
                AgencyName = agencyName,
                CaseManagerName = owner.DisplayName,
                SupervisorName = supervisorName,
                RequestDate = now.Date,
                CreatedAtUtc = now
            };
            db.CheckRequests.Add(request);
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(ContractMapper.ToCheckRequest(request));
        });

        api.MapPut("/check-requests/{id:int}", async Task<IResult> (
            int id, SaveCheckRequestRequest input, ClaimsPrincipal principal,
            ApiDbContext db, CancellationToken cancellationToken) =>
            await SaveCheckRequestAsync(id, input, publish: false, principal, db, null, cancellationToken));

        api.MapPost("/check-requests/{id:int}/publish", async Task<IResult> (
            int id, SaveCheckRequestRequest input, ClaimsPrincipal principal,
            ApiDbContext db, AuditTrail audit, CancellationToken cancellationToken) =>
            await SaveCheckRequestAsync(id, input, publish: true, principal, db, audit, cancellationToken));
    }

    private static async Task<IResult> SaveCheckRequestAsync(
        int id,
        SaveCheckRequestRequest input,
        bool publish,
        ClaimsPrincipal principal,
        ApiDbContext db,
        AuditTrail? audit,
        CancellationToken cancellationToken)
    {
        var actor = Actor.From(principal);
        var request = await db.CheckRequests.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (request is null || !await TenantAccess.OwnsPersonAsync(db, actor, request.PersonId, cancellationToken))
            return Results.NotFound();
        if (request.Revision != input.ExpectedRevision)
            return Results.Conflict(new ApiErrorDto("stale_check_request",
                "This check request changed elsewhere. Reload it before trying again.", string.Empty));
        if (request.PublishedAtUtc is not null)
            return Results.Conflict(new ApiErrorDto("published_check_request",
                "A PDF has already been prepared from this check request, so it is read-only. Create a new request for a correction.", string.Empty));

        var errors = publish
            ? CheckRequestPublication.FindPublicationBlockers(input.RequestDate, input.PayableTo,
                input.MailingAddress, input.Amount, input.NeededByDate, input.Reason, false)
            : CheckRequestPublication.FindDraftErrors(input.RequestDate, input.PayableTo,
                input.MailingAddress, input.Amount, input.NeededByDate, input.Reason);
        if (errors.Count > 0)
            return Results.ValidationProblem(new Dictionary<string, string[]> { [publish ? "publish" : "request"] = [.. errors] });

        request.RequestDate = input.RequestDate?.Date;
        request.PayableTo = Normalize(input.PayableTo);
        request.MailingAddress = Normalize(input.MailingAddress);
        request.Amount = input.Amount;
        request.NeededByDate = input.NeededByDate?.Date;
        request.Reason = Normalize(input.Reason);
        if (publish)
        {
            request.PublishedAtUtc = DateTime.UtcNow;
            request.PublishedByUserId = actor.UserId;
            request.PublishedByName = actor.DisplayName;
            audit!.Record(actor, AuditActions.CheckRequestPublished, "CheckRequest", request.Id);
        }
        request.Revision++;
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException)
        {
            return Results.Conflict(new ApiErrorDto("stale_check_request",
                "This check request changed elsewhere. Reload it before trying again.", string.Empty));
        }
        return Results.Ok(ContractMapper.ToCheckRequest(request));
    }

    private static void MapAiContext(RouteGroupBuilder api)
    {
        api.MapGet("/people/{personId:int}/ai-context", async Task<IResult> (
            int personId, ClaimsPrincipal principal, ApiDbContext db, CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!await TenantAccess.OwnsPersonAsync(db, actor, personId, cancellationToken))
                return Results.Forbid();

            var selected = await db.People
                .AsNoTracking()
                .Where(person => person.Id == personId && person.AgencyId == actor.AgencyId)
                .Select(person => new { person.Id, person.FirstName })
                .SingleOrDefaultAsync(cancellationToken);
            if (selected is null)
                return Results.Forbid();

            return Results.Ok(new ClientAiContextDto(
                selected.Id,
                selected.FirstName,
                [new ClientAiContextSourceDto("Scope", "Selected client identity only; no prior records")]));
        });
    }

    private static void MapNotes(RouteGroupBuilder api)
    {
        api.MapPost("/notes", async (
            SaveNoteRequest request,
            ClaimsPrincipal principal,
            ApiDbContext db,
            ApiClock clock,
            AuditTrail auditTrail,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            request = NoteSchedulingPolicy.Normalize(request, clock.Today);
            var validation = ValidateNote(request);
            if (validation is not null)
                return Results.ValidationProblem(validation);
            await using var scheduleWrite = await ServiceTimeWriteScope.BeginAsync(db,
                actor.AgencyId, actor.UserId, cancellationToken);
            if (!await TenantAccess.OwnsPersonAsync(db, actor, request.PersonId, cancellationToken))
                return Results.NotFound();

            var formLinkProblem = await FindFormLinkProblemAsync(db, actor, request, cancellationToken);
            if (formLinkProblem is not null)
                return formLinkProblem;

            var attestsFormOnLog = IsNonReleaseLoggedFormNote(request);
            if (!attestsFormOnLog)
            {
                var submissionProblem = await FindNoteSubmissionProblemAsync(
                    db, actor, request, clock.Today, cancellationToken);
                if (submissionProblem is not null)
                    return submissionProblem;
            }

            var timeConflict = await FindServiceTimeProblemAsync(db, actor, request, null, cancellationToken);
            if (timeConflict is not null)
                return timeConflict;

            ContractMapper.TryParseNoteStatus(request.Status, out var status);
            ContractMapper.TryParseFormType(request.FormType, out var formType);
            ContractMapper.TryParseNoteType(request.NoteType, out var noteType);
            ContractMapper.TryParseGoalProgress(request.GoalProgress, out var goalProgress);
            var note = new ServerNote
            {
                Narrative = request.Narrative,
                EventDate = request.EventDate,
                Status = status,
                Minutes = request.Minutes,
                StartTime = request.StartTime,
                PersonId = request.PersonId,
                FormType = formType,
                FormId = request.FormId,
                ReleaseObligationId = request.ReleaseObligationId,
                FormDateCorrectionReason = request.FormDateCorrectionReason,
                NoteType = noteType,
                Activities = request.Activities,
                GoalProgress = goalProgress,
                AgencyId = actor.AgencyId,
                CaseManagerJustification = request.CaseManagerJustification,
                VisitDocumentationJson = request.VisitDocumentationJson
            };
            db.Notes.Add(note);
            await db.SaveChangesAsync(cancellationToken);
            var formAttestationProblem = await AttestFormFromLoggedNoteAsync(
                db, note, actor, clock, auditTrail, cancellationToken);
            if (formAttestationProblem is not null)
                return formAttestationProblem;
            if (attestsFormOnLog)
            {
                var submissionProblem = await FindNoteSubmissionProblemAsync(
                    db, actor, request, clock.Today, cancellationToken,
                    noteId: note.Id, auditTrail: auditTrail);
                if (submissionProblem is not null)
                    return submissionProblem;
            }
            await scheduleWrite.CommitAsync(cancellationToken);
            return Results.Ok(ContractMapper.ToNote(note));
        });

        api.MapPut("/notes/{id:int}", async (
            int id,
            SaveNoteRequest request,
            ClaimsPrincipal principal,
            ApiDbContext db,
            ApiClock clock,
            AuditTrail auditTrail,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            request = NoteSchedulingPolicy.Normalize(request, clock.Today);
            var validation = ValidateNote(request);
            if (validation is not null)
                return Results.ValidationProblem(validation);
            await using var scheduleWrite = await ServiceTimeWriteScope.BeginAsync(db,
                actor.AgencyId, actor.UserId, cancellationToken);
            var row = await (from note in db.Notes
                             join person in TenantAccess.OwnedPeople(db, actor) on note.PersonId equals person.Id
                             where person.UserId == actor.UserId &&
                                   person.AgencyId == actor.AgencyId &&
                                   note.AgencyId == actor.AgencyId &&
                                   note.Id == id
                             select new { Note = note, Person = person })
                .SingleOrDefaultAsync(cancellationToken);
            if (row is null)
                return Results.NotFound();
            if (request.ExpectedRevision != row.Note.Revision)
                return StaleNoteConflict();
            if (!NoteWorkflow.CanCaseManagerEdit(row.Note.Status))
                return Results.Conflict(new ApiErrorDto(
                    "note_locked",
                    "Logged and approved notes cannot be edited. A supervisor must return a logged note before it can be corrected.",
                    string.Empty));
            ContractMapper.TryParseNoteStatus(request.Status, out var requestedStatus);
            if (!NoteWorkflow.CanCaseManagerTransition(row.Note.Status, requestedStatus))
                return Results.Conflict(new ApiErrorDto(
                    "invalid_note_transition",
                    NoteWorkflow.DescribeRejectedTransition(row.Note.Status, requestedStatus),
                    string.Empty));

            var previousPersonId = row.Note.PersonId;
            var responsePerson = row.Person;
            if (request.PersonId != previousPersonId)
            {
                responsePerson = await TenantAccess.OwnedPeople(db, actor).SingleOrDefaultAsync(person =>
                    person.Id == request.PersonId &&
                    person.UserId == actor.UserId &&
                    person.AgencyId == actor.AgencyId,
                    cancellationToken);
                if (responsePerson is null)
                    return Results.NotFound();
            }

            var formLinkProblem = await FindFormLinkProblemAsync(db, actor, request, cancellationToken);
            if (formLinkProblem is not null)
                return formLinkProblem;

            var attestsFormOnLog = IsNonReleaseLoggedFormNote(request);
            if (!attestsFormOnLog)
            {
                var submissionProblem = await FindNoteSubmissionProblemAsync(
                    db, actor, request, clock.Today, cancellationToken, noteId: id);
                if (submissionProblem is not null)
                    return submissionProblem;
            }

            var timeConflict = await FindServiceTimeProblemAsync(db, actor, request, id, cancellationToken);
            if (timeConflict is not null)
                return timeConflict;

            ContractMapper.TryParseNoteStatus(request.Status, out var status);
            ContractMapper.TryParseFormType(request.FormType, out var formType);
            ContractMapper.TryParseNoteType(request.NoteType, out var noteType);
            ContractMapper.TryParseGoalProgress(request.GoalProgress, out var goalProgress);
            row.Note.Narrative = request.Narrative;
            row.Note.EventDate = request.EventDate;
            row.Note.Status = status;
            row.Note.Minutes = request.Minutes;
            row.Note.StartTime = request.StartTime;
            row.Note.PersonId = request.PersonId;
            row.Note.FormType = formType;
            row.Note.FormId = request.FormId;
            row.Note.ReleaseObligationId = request.ReleaseObligationId;
            row.Note.FormDateCorrectionReason = request.FormDateCorrectionReason;
            row.Note.NoteType = noteType;
            row.Note.Activities = request.Activities;
            row.Note.GoalProgress = goalProgress;
            row.Note.CaseManagerJustification = request.CaseManagerJustification;
            row.Note.VisitDocumentationJson = request.VisitDocumentationJson;
            row.Note.Revision++;
            if (previousPersonId != row.Note.PersonId)
            {
                auditTrail.Record(
                    actor,
                    AuditActions.NoteReassigned,
                    "Note",
                    row.Note.Id,
                    JsonSerializer.Serialize(new
                    {
                        previousPersonId,
                        newPersonId = row.Note.PersonId
                    }));
            }
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                var formAttestationProblem = await AttestFormFromLoggedNoteAsync(
                    db, row.Note, actor, clock, auditTrail, cancellationToken);
                if (formAttestationProblem is not null)
                    return formAttestationProblem;
                if (attestsFormOnLog)
                {
                    var submissionProblem = await FindNoteSubmissionProblemAsync(
                        db, actor, request, clock.Today, cancellationToken,
                        noteId: id, auditTrail: auditTrail);
                    if (submissionProblem is not null)
                        return submissionProblem;
                }
                await scheduleWrite.CommitAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return StaleNoteConflict();
            }
            return Results.Ok(ContractMapper.ToNote(row.Note, responsePerson));
        });

        api.MapDelete("/notes/{id:int}", async (
            int id,
            int? expectedRevision,
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            var note = await (from candidate in db.Notes
                              join person in TenantAccess.OwnedPeople(db, actor) on candidate.PersonId equals person.Id
                              where candidate.Id == id && candidate.AgencyId == actor.AgencyId
                              select candidate).SingleOrDefaultAsync(cancellationToken);
            if (note is null)
                return Results.NotFound();
            if (expectedRevision != note.Revision)
                return StaleNoteConflict();
            if (!NoteWorkflow.CanCaseManagerDelete(note.Status))
                return Results.Conflict(new ApiErrorDto(
                    "note_retained",
                    "Submitted and workflow-controlled notes are retained as part of the clinical record.",
                    string.Empty));

            db.Notes.Remove(note);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return StaleNoteConflict();
            }
            return Results.NoContent();
        });

        api.MapGet("/people/{personId:int}/notes", async Task<Results<Ok<List<NoteDto>>, NotFound>> (
            int personId,
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!await TenantAccess.OwnsPersonAsync(db, actor, personId, cancellationToken))
                return TypedResults.NotFound();
            var notes = await db.Notes.AsNoTracking().Where(x => x.PersonId == personId && x.AgencyId == actor.AgencyId).ToListAsync(cancellationToken);
            return TypedResults.Ok(notes.Select(x => ContractMapper.ToNote(x)).ToList());
        });

        api.MapGet("/notes/monthly", async Task<IResult> (
            int? userId,
            ClaimsPrincipal principal,
            ApiDbContext db,
            ApiClock clock,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            var targetUserId = userId ?? actor.UserId;
            if (!await TenantAccess.CanAccessUserAsync(db, actor, targetUserId, cancellationToken))
                return Results.Forbid();

            var first = new DateTime(clock.Today.Year, clock.Today.Month, 1);
            var end = first.AddMonths(1);
            var rows = await (from note in db.Notes.AsNoTracking()
                              join person in db.People.AsNoTracking() on note.PersonId equals person.Id
                              where person.UserId == targetUserId &&
                                    person.AgencyId == actor.AgencyId && note.AgencyId == actor.AgencyId &&
                                    note.EventDate >= first && note.EventDate < end
                              select new { Note = note, Person = person })
                .ToListAsync(cancellationToken);
            return Results.Ok(rows.Select(x => ContractMapper.ToNote(x.Note, x.Person)).ToList());
        });

        // The case manager's whole day, across their whole caseload. Overlapping
        // service time is a property of one person's day, so this is scoped by
        // user and date and never by client.
        api.MapGet("/notes/day", async Task<IResult> (
            int? userId,
            DateTime date,
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            var targetUserId = userId ?? actor.UserId;
            if (!await TenantAccess.CanAccessUserAsync(db, actor, targetUserId, cancellationToken))
                return Results.Forbid();

            var rows = await LoadDayNotesAsync(db, targetUserId, actor.AgencyId, date, cancellationToken);
            return Results.Ok(rows.Select(x => ContractMapper.ToNote(x.Note, x.Person)).ToList());
        });

        api.MapGet("/notes/year/{year:int}", async (
            int year,
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            if (year is < 2000 or > 2200)
                return Results.BadRequest();
            var actor = Actor.From(principal);
            if (!await TenantAccess.CanAccessUserAsync(db, actor, actor.UserId, cancellationToken))
                return Results.Forbid();
            var first = new DateTime(year, 1, 1);
            var end = first.AddYears(1);
            var rows = await (from note in db.Notes.AsNoTracking()
                              join person in TenantAccess.OwnedPeople(db, actor).AsNoTracking() on note.PersonId equals person.Id
                              where note.AgencyId == actor.AgencyId &&
                                    note.EventDate >= first && note.EventDate < end
                              select new { Note = note, Person = person })
                .ToListAsync(cancellationToken);
            return Results.Ok(rows.Select(x => ContractMapper.ToNote(x.Note, x.Person)).ToList());
        });

        api.MapPost("/notes/abandon-overdue", async (
            ClaimsPrincipal principal,
            ApiDbContext db,
            ApiClock clock,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            // Permission is checked before even lazily creating agency settings.
            if (!await TenantAccess.CanAccessUserAsync(db, actor, actor.UserId, cancellationToken))
                return Results.Forbid();
            var abandonedAfterDays = ProductivityForecast.NormalizeDocumentationWindowDays(
                (await GetOrCreateSettingsAsync(db, actor.AgencyId, cancellationToken)).AbandonedAfterDays);
            var threshold = clock.Today.AddDays(-abandonedAfterDays);
            var personIds = TenantAccess.OwnedPeople(db, actor).Select(x => x.Id);
            var count = await db.Notes
                .Where(x => x.AgencyId == actor.AgencyId && personIds.Contains(x.PersonId) && x.Status == 1 && x.EventDate < threshold)
                .ExecuteUpdateAsync(x => x
                    .SetProperty(n => n.Status, 8)
                    .SetProperty(n => n.Revision, n => n.Revision + 1), cancellationToken);
            return TypedResults.Ok(new CountDto(count));
        });
    }

    private static void MapSettings(RouteGroupBuilder api)
    {
        api.MapGet("/settings", async (
            ClaimsPrincipal principal,
            ApiDbContext db,
            ApiClock clock,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            var settings = await GetOrCreateSettingsAsync(db, actor.AgencyId, cancellationToken);
            var activeRequirements = await ResolveBillingComplianceRequirementsAsync(
                db, actor.AgencyId, clock.Today, settings.BillingComplianceRequirements, cancellationToken);
            return ContractMapper.ToSettings(settings) with
            {
                BillingComplianceRequirements = activeRequirements
            };
        });

        api.MapGet("/settings/billing-compliance-requirements", async (
            DateTime serviceDate,
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            var fallbackRequirements = await db.Settings.AsNoTracking()
                .Where(settings => settings.AgencyId == actor.AgencyId)
                .Select(settings =>
                    (BillingComplianceRequirements?)settings.BillingComplianceRequirements)
                .SingleOrDefaultAsync(cancellationToken)
                ?? BillingComplianceGate.DefaultRequirements;
            var requirements = await ResolveBillingComplianceRequirementsAsync(
                db,
                actor.AgencyId,
                serviceDate.Date,
                fallbackRequirements,
                cancellationToken);
            return Results.Ok(new BillingComplianceRequirementsAtDateDto(
                serviceDate.Date,
                requirements));
        });

        api.MapGet("/settings/billing-compliance-policies", async Task<IResult> (
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!actor.HasAdminPermissions)
                return Results.Forbid();

            var rows = await db.BillingCompliancePolicyVersions.AsNoTracking()
                .Where(version => version.AgencyId == actor.AgencyId)
                .OrderByDescending(version => version.EffectiveOn)
                .ThenByDescending(version => version.Id)
                .ToListAsync(cancellationToken);
            return Results.Ok(rows.Select(ContractMapper.ToBillingCompliancePolicyVersion).ToList());
        });

        api.MapPost("/settings/billing-compliance-policies/preview", async Task<IResult> (
            PreviewBillingCompliancePolicyRequest request,
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!actor.HasAdminPermissions)
                return Results.Forbid();
            if (request.EffectiveOn is null)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["effectiveOn"] = ["An enforcement date is required before impact can be previewed."]
                });
            }
            if (!BillingComplianceGate.IsSupported(request.Requirements))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["requirements"] = ["The billing-compliance policy contains unsupported requirements."]
                });
            }

            var evaluation = await BuildBillingCompliancePolicyImpactPreviewAsync(
                db,
                actor.AgencyId,
                request.EffectiveOn.Value,
                request.Requirements,
                cancellationToken);
            return Results.Ok(evaluation.Preview);
        });

        api.MapGet("/billing/compliance-policy-review-flags", async Task<IResult> (
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!actor.HasAdminPermissions && !actor.HasBillingPermissions)
                return Results.Forbid();

            var flags = await db.BillingCompliancePolicyReviewFlags.AsNoTracking()
                .Include(flag => flag.PolicyVersion)
                .Where(flag => flag.AgencyId == actor.AgencyId)
                .OrderByDescending(flag => flag.CreatedAtUtc)
                .ThenByDescending(flag => flag.Id)
                .ToListAsync(cancellationToken);
            return Results.Ok(flags.Select(flag => flag.ToContract()).ToArray());
        });

        api.MapPost("/settings/billing-compliance-policies", async Task<IResult> (
            AppendBillingCompliancePolicyRequest request,
            ClaimsPrincipal principal,
            ApiDbContext db,
            ApiClock clock,
            AuditTrail auditTrail,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!actor.HasAdminPermissions)
                return Results.Forbid();
            if (request.ChangeId == Guid.Empty)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["changeId"] = ["A policy change id is required."]
                });
            }

            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
            var settings = await GetOrCreateSettingsAsync(db, actor.AgencyId, cancellationToken);
            var existing = await db.BillingCompliancePolicyVersions.AsNoTracking()
                .SingleOrDefaultAsync(version => version.VersionId == request.ChangeId, cancellationToken);
            if (existing is not null)
            {
                var normalizedExplanation = string.IsNullOrWhiteSpace(request.Explanation)
                    ? null
                    : request.Explanation.Trim();
                if (existing.AgencyId == actor.AgencyId &&
                    existing.EffectiveOn.Date == request.EffectiveOn?.Date &&
                    existing.Requirements == request.Requirements &&
                    string.Equals(existing.Explanation, normalizedExplanation, StringComparison.Ordinal))
                {
                    return Results.Ok(ContractMapper.ToBillingCompliancePolicyVersion(existing));
                }

                return Results.Conflict(new ApiErrorDto(
                    "billing_policy_change_id_reused",
                    "That billing-policy change id was already used for different values.",
                    string.Empty));
            }

            var decision = BillingCompliancePolicyRules.ValidateChange(
                request.Requirements,
                request.EffectiveOn,
                clock.Today,
                new BillingCompliancePolicyOptions(settings.AllowPastBillingPolicyEffectiveDates),
                request.Explanation);
            if (!decision.Accepted)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["billingCompliancePolicy"] = decision.Errors.ToArray()
                });
            }

            var impact = await BuildBillingCompliancePolicyImpactPreviewAsync(
                db,
                actor.AgencyId,
                request.EffectiveOn!.Value,
                request.Requirements,
                cancellationToken);
            var recordedAtUtc = clock.UtcNow.UtcDateTime;
            var version = BillingCompliancePolicyVersion.Create(
                actor.AgencyId,
                request.Requirements,
                request.EffectiveOn!.Value,
                clock.Today,
                actor.UserId,
                recordedAtUtc,
                settings.AllowPastBillingPolicyEffectiveDates,
                request.Explanation,
                request.ChangeId);
            db.BillingCompliancePolicyVersions.Add(version);
            var reviewFlags = CreateBillingCompliancePolicyReviewFlags(
                version, impact.Impacts, recordedAtUtc);
            db.BillingCompliancePolicyReviewFlags.AddRange(reviewFlags);
            await RecalculateUnsubmittedNoteStatesAsync(
                db, impact.Impacts, cancellationToken);
            auditTrail.Record(
                actor,
                AuditActions.BillingCompliancePolicyAppended,
                "BillingCompliancePolicyVersion",
                metadataJson: JsonSerializer.Serialize(new
                {
                    changeId = version.VersionId,
                    effectiveOn = version.EffectiveOn.ToString("yyyy-MM-dd"),
                    requirements = (int)version.Requirements,
                    isPastCorrection = version.EffectiveOn.Date < clock.Today,
                    unresolvedReviewFlags = reviewFlags.Count
                }));
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Results.Ok(ContractMapper.ToBillingCompliancePolicyVersion(version));
        });

        api.MapPut("/settings", async Task<IResult> (
            SettingsDto request, ClaimsPrincipal principal, ApiDbContext db,
            ApiClock clock, AuditTrail auditTrail, CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!actor.HasAdminPermissions) return Results.Forbid();
            if (request.AbandonedAfterDays <= 0 || request.ProductivityThreshold < 0 ||
                request.BaseIncentive < 0 || request.PerUnitIncentive < 0 ||
                request.PassthroughRate is < 0 or > 1 || request.SalesTaxRate is < 0 or > 1)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["settings"] = ["The documentation window must be at least one day, monetary values cannot be negative, and percentages must be between zero and one."] });
            if (!BillingComplianceGate.IsSupported(request.BillingComplianceRequirements))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["billingComplianceRequirements"] = ["The compliance requirement selection is invalid."] });
            if (request.AnnualPacketOpenDaysBefore is < 0 or > 180)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["annualPacketOpenDaysBefore"] = ["Choose 0–180 days."] });
            if (string.IsNullOrWhiteSpace(request.VrAssistantTitle) ||
                request.VrAssistantTitle.Trim().Length > VocationalRehabilitationProfile.AssistantTitleMaxLength)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["vrAssistantTitle"] = [$"The VR assistant title is required and must not exceed {VocationalRehabilitationProfile.AssistantTitleMaxLength} characters."] });
            if (request.DefaultPassthroughProviderId is int providerId &&
                !await db.Providers.AsNoTracking().AnyAsync(
                    x => x.Id == providerId && x.AgencyId == actor.AgencyId && x.ProvidesPassthroughService,
                    cancellationToken))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["defaultPassthroughProviderId"] = ["The default provider is outside your agency or does not provide passthrough service."] });
            var settings = await GetOrCreateSettingsAsync(db, actor.AgencyId, cancellationToken);
            if (request.Revision != settings.Revision)
                return StaleSettingsConflict();

            var activeRequirements = await ResolveBillingComplianceRequirementsAsync(
                db, actor.AgencyId, clock.Today, settings.BillingComplianceRequirements, cancellationToken);
            if (request.BillingComplianceRequirements != activeRequirements)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["billingComplianceRequirements"] =
                        ["Billing-compliance checkboxes are applied separately and require an enforcement date."]
                });
            }

            var id = settings.Id;
            var agencyId = settings.AgencyId;
            var storedFallbackRequirements = settings.BillingComplianceRequirements;
            db.Entry(settings).CurrentValues.SetValues(request);
            settings.Id = id;
            settings.AgencyId = agencyId;
            settings.BillingComplianceRequirements = storedFallbackRequirements;
            settings.VrAssistantTitle = VocationalRehabilitationProfile.NormalizeAssistantTitle(
                request.VrAssistantTitle);
            settings.Revision++;
            auditTrail.Record(actor, AuditActions.SettingsUpdated, "Settings", settings.Id);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return StaleSettingsConflict();
            }
            return Results.Ok(ContractMapper.ToSettings(settings) with
            {
                BillingComplianceRequirements = activeRequirements
            });
        });
    }

    private static void MapScratchpads(RouteGroupBuilder api)
    {
        api.MapGet("/scratchpad/today", async (
            ClaimsPrincipal principal,
            ApiDbContext db,
            ApiClock clock,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            var today = clock.Today;
            var scratchpad = await GetOrCreateScratchpadAsync(
                db, actor.UserId, today, cancellationToken);
            return ContractMapper.ToScratchpad(scratchpad, []);
        });

        api.MapGet("/scratchpad/tomorrow", async (
            ClaimsPrincipal principal,
            ApiDbContext db,
            ApiClock clock,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            var agendaDate = WorkAgendaDates.NextWorkday(clock.Today);
            var scratchpad = await GetOrCreateScratchpadAsync(
                db, actor.UserId, agendaDate, cancellationToken);
            return ContractMapper.ToScratchpad(scratchpad, []);
        });

        api.MapGet("/scratchpad/history", async (
            ClaimsPrincipal principal,
            ApiDbContext db,
            ApiClock clock,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            var pads = await db.Scratchpads.AsNoTracking()
                .Where(x => x.UserId == actor.UserId && x.Date < clock.Today)
                .OrderByDescending(x => x.Date)
                .ToListAsync(cancellationToken);
            var ids = pads.Select(x => x.Id).ToList();
            var comments = await db.ScratchpadComments.AsNoTracking()
                .Where(x => ids.Contains(x.ScratchpadId))
                .OrderBy(x => x.CreatedAtUtc)
                .ToListAsync(cancellationToken);
            var byPad = comments.GroupBy(x => x.ScratchpadId).ToDictionary(x => x.Key, x => (IReadOnlyList<ServerScratchpadComment>)x.ToList());
            return pads.Select(x => ContractMapper.ToScratchpad(x, byPad.GetValueOrDefault(x.Id) ?? [])).ToList();
        });

        api.MapPut("/scratchpad", async Task<IResult> (
            SaveScratchpadRequest request,
            ClaimsPrincipal principal,
            ApiDbContext db,
            AuditTrail auditTrail,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            var content = request.Content ?? string.Empty;
            if (content.Length > 1_000_000)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["content"] = ["Scratchpad content must not exceed 1,000,000 characters."]
                });

            var scratchpad = await db.Scratchpads.SingleOrDefaultAsync(
                x => x.Id == request.Id && x.UserId == actor.UserId,
                cancellationToken);
            if (scratchpad is null)
                return Results.NotFound();
            if (request.ExpectedRevision != scratchpad.Revision)
                return StaleScratchpadConflict();

            if (scratchpad.Content == content)
                return Results.Ok(ContractMapper.ToScratchpad(scratchpad, []));

            scratchpad.Content = content;
            scratchpad.Revision++;
            auditTrail.Record(actor, AuditActions.ScratchpadUpdated, "Scratchpad", scratchpad.Id);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return StaleScratchpadConflict();
            }

            return Results.Ok(ContractMapper.ToScratchpad(scratchpad, []));
        });

        api.MapPost("/scratchpad/{scratchpadId:int}/comments", async Task<Results<Ok<ScratchpadCommentDto>, NotFound, ValidationProblem>> (
            int scratchpadId,
            AddScratchpadCommentRequest request,
            ClaimsPrincipal principal,
            ApiDbContext db,
            ApiClock clock,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            var content = request.Content?.Trim() ?? string.Empty;
            if (content.Length is < 1 or > 10_000)
                return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["content"] = ["Comment content is required and must not exceed 10,000 characters."] });
            var exists = await db.Scratchpads.AnyAsync(x => x.Id == scratchpadId && x.UserId == actor.UserId && x.Date < clock.Today, cancellationToken);
            if (!exists)
                return TypedResults.NotFound();
            var comment = new ServerScratchpadComment
            {
                ScratchpadId = scratchpadId,
                AuthorUserId = actor.UserId,
                AuthorDisplayName = actor.DisplayName,
                CreatedAtUtc = DateTime.UtcNow,
                Content = content
            };
            db.ScratchpadComments.Add(comment);
            await db.SaveChangesAsync(cancellationToken);
            return TypedResults.Ok(ContractMapper.ToScratchpadComment(comment));
        });
    }

    private static async Task<ServerScratchpad> GetOrCreateScratchpadAsync(
        ApiDbContext db,
        int userId,
        DateTime date,
        CancellationToken cancellationToken)
    {
        var scratchpad = await db.Scratchpads.SingleOrDefaultAsync(
            candidate => candidate.UserId == userId && candidate.Date == date,
            cancellationToken);
        if (scratchpad is not null)
            return scratchpad;

        scratchpad = new ServerScratchpad { UserId = userId, Date = date };
        db.Scratchpads.Add(scratchpad);
        await db.SaveChangesAsync(cancellationToken);
        return scratchpad;
    }

    private static void MapExemptDates(RouteGroupBuilder api)
    {
        api.MapGet("/exempt-dates/{year:int}", async (
            int year,
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            var first = new DateTime(year, 1, 1);
            var end = first.AddYears(1);
            return await db.ExemptDates.AsNoTracking()
                .Where(x => x.UserId == actor.UserId && x.Date >= first && x.Date < end)
                .OrderBy(x => x.Date)
                .Select(x => new ExemptDateDto(x.Id, x.Date, x.Reason))
                .ToListAsync(cancellationToken);
        });

        api.MapPost("/exempt-dates", async (
            AddExemptDateRequest request,
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            var entity = new ServerExemptDate { UserId = actor.UserId, Date = request.Date.Date, Reason = request.Reason?.Trim() };
            db.ExemptDates.Add(entity);
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(new ExemptDateDto(entity.Id, entity.Date, entity.Reason));
        });

        api.MapDelete("/exempt-dates/{id:int}", async Task<Results<NoContent, NotFound>> (
            int id,
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            var count = await db.ExemptDates.Where(x => x.Id == id && x.UserId == actor.UserId).ExecuteDeleteAsync(cancellationToken);
            return count == 0 ? TypedResults.NotFound() : TypedResults.NoContent();
        });

        // Whether the documented daily average divides by one of the actor's own days. Never
        // another user's: the row is keyed to the validated actor server-side, and the request
        // carries no user id to trust.
        // A reviewer may read the days of a case manager they can reach, so a settled day marked
        // as having produced nothing billable can be asked about. The id is never trusted: it is
        // gated before it reaches the query, and writing stays the case manager's own.
        api.MapGet("/service-day-inclusions/{year:int}", async Task<Results<Ok<List<ServiceDayInclusionDto>>, ForbidHttpResult>> (
            int year,
            int? userId,
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            var targetUserId = userId ?? actor.UserId;
            if (!await TenantAccess.CanAccessUserAsync(db, actor, targetUserId, cancellationToken))
                return TypedResults.Forbid();

            var first = new DateTime(year, 1, 1);
            var end = first.AddYears(1);
            return TypedResults.Ok(await db.ServiceDayInclusions.AsNoTracking()
                .Where(x => x.UserId == targetUserId && x.Date >= first && x.Date < end)
                .OrderBy(x => x.Date)
                .Select(x => new ServiceDayInclusionDto(x.Id, x.Date, x.IsIncluded))
                .ToListAsync(cancellationToken));
        });

        api.MapPut("/service-day-inclusions", async (
            SetServiceDayInclusionRequest request,
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            var day = request.Date.Date;
            var existing = await db.ServiceDayInclusions
                .SingleOrDefaultAsync(x => x.UserId == actor.UserId && x.Date == day, cancellationToken);
            if (existing is null)
            {
                existing = new ServerServiceDayInclusion
                {
                    UserId = actor.UserId,
                    Date = day,
                    IsIncluded = request.IsIncluded
                };
                db.ServiceDayInclusions.Add(existing);
            }
            else
            {
                existing.IsIncluded = request.IsIncluded;
            }

            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(new ServiceDayInclusionDto(existing.Id, existing.Date, existing.IsIncluded));
        });

        api.MapDelete("/service-day-inclusions/{date}", async Task<Results<NoContent, BadRequest<string>>> (
            string date,
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            if (!DateTime.TryParse(date, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var parsed))
                return TypedResults.BadRequest("A calendar date is required.");

            var actor = Actor.From(principal);
            var day = parsed.Date;
            await db.ServiceDayInclusions
                .Where(x => x.UserId == actor.UserId && x.Date == day)
                .ExecuteDeleteAsync(cancellationToken);
            return TypedResults.NoContent();
        });
    }

    private static void MapIncentives(RouteGroupBuilder api)
    {
        api.MapGet("/incentives/{year:int}/{month:int}", async Task<IResult> (
            int year,
            int month,
            int? userId,
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            var targetUserId = userId ?? actor.UserId;
            if (!await TenantAccess.CanAccessUserAsync(db, actor, targetUserId, cancellationToken))
                return Results.Forbid();

            var incentive = await db.Incentives.SingleOrDefaultAsync(x => x.UserId == targetUserId && x.Month == month && x.Year == year, cancellationToken);
            var created = false;
            var settings = await GetOrCreateSettingsAsync(db, actor.AgencyId, cancellationToken);
            var correctDays = WorkdayCalculator.Count(new DateTime(year, month, 1), new DateTime(year, month, DateTime.DaysInMonth(year, month)), settings);
            if (incentive is null)
            {
                incentive = new ServerIncentive
                {
                    UserId = targetUserId,
                    Month = month,
                    Year = year,
                    DaysScheduled = correctDays,
                    BaseIncentive = settings.BaseIncentive,
                    PerUnitIncentive = settings.PerUnitIncentive,
                    UnitsPerDay = settings.ProductivityThreshold
                };
                db.Incentives.Add(incentive);
                created = true;
            }
            else
            {
                incentive.DaysScheduled = correctDays;
                if (incentive.UnitsPerDay == 0)
                    incentive.UnitsPerDay = settings.ProductivityThreshold;
            }
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(new IncentiveEnvelopeDto(ContractMapper.ToIncentive(incentive), created));
        });

        api.MapGet("/incentives/history", async (
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            var rows = await db.Incentives.AsNoTracking().Where(x => x.UserId == actor.UserId).OrderBy(x => x.Year).ThenBy(x => x.Month).ToListAsync(cancellationToken);
            return rows.Select(ContractMapper.ToIncentive).ToList();
        });

        api.MapPut("/incentives/{id:int}", async (
            int id,
            IncentiveDto request,
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            var incentive = await db.Incentives.SingleOrDefaultAsync(
                x => x.Id == id && x.UserId == actor.UserId,
                cancellationToken);
            if (incentive is null || request.Id != id)
                return Results.NotFound();
            if (request.DaysScheduled is < 0 or > 31 || request.UnitsPerDay is < 0 or > 1000 || request.ExcludedDatesJson.Length > 100_000)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["incentive"] = ["The incentive values are invalid."] });

            incentive.DaysScheduled = request.DaysScheduled;
            incentive.BaseIncentive = request.BaseIncentive;
            incentive.PerUnitIncentive = request.PerUnitIncentive;
            incentive.UnitsPerDay = request.UnitsPerDay;
            incentive.ExcludedDatesJson = request.ExcludedDatesJson;
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(ContractMapper.ToIncentive(incentive));
        });

        api.MapPost("/incentives/eligible-days", async (
            DateWindowRequest request,
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            var settings = await GetOrCreateSettingsAsync(db, actor.AgencyId, cancellationToken);
            return new CountDto(WorkdayCalculator.Count(request.StartInclusive, request.EndInclusive, settings));
        });

        api.MapPost("/incentives/remaining-days", async (
            RemainingEligibleDaysRequest request,
            ClaimsPrincipal principal,
            ApiDbContext db,
            ApiClock clock,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            var settings = await GetOrCreateSettingsAsync(db, actor.AgencyId, cancellationToken);
            var exempt = request.ExemptDates.Select(x => x.Date).ToHashSet();
            var monthStart = new DateTime(request.Year, request.Month, 1);
            var monthEnd = monthStart.AddMonths(1).AddDays(-1);
            var start = clock.Today > monthStart ? clock.Today : monthStart;
            var count = 0;
            for (var date = start; date <= monthEnd; date = date.AddDays(1))
            {
                if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday || WorkdayCalculator.IsExcluded(date, settings) || exempt.Contains(date))
                    continue;
                count++;
            }
            return new CountDto(count);
        });
    }

    private static void MapReports(RouteGroupBuilder api)
    {
        api.MapGet("/reports/productivity-units", async Task<IResult> (
            DateTime start,
            DateTime end,
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            start = start.Date;
            end = end.Date;
            if (end < start || start.Year < 2000 || end.Year > 2200 ||
                (end - start).TotalDays > 3_660)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["window"] = ["The report window must be valid, within 2000-2200, and no longer than 10 years."]
                });
            }

            var actor = Actor.From(principal);
            var endExclusive = end.AddDays(1);
            if (!await TenantAccess.CanAccessUserAsync(db, actor, actor.UserId, cancellationToken))
                return Results.Forbid();
            var rows = await (from note in db.Notes.AsNoTracking()
                              join person in TenantAccess.OwnedPeople(db, actor).AsNoTracking()
                                  on note.PersonId equals person.Id
                              where person.UserId == actor.UserId &&
                                    person.AgencyId == actor.AgencyId &&
                                    note.AgencyId == actor.AgencyId &&
                                    note.EventDate.HasValue &&
                                    note.EventDate.Value >= start &&
                                    note.EventDate.Value < endExclusive &&
                                    (note.Status == (int)NoteStatus.Logged ||
                                     note.Status == (int)NoteStatus.Approved)
                              select new
                              {
                                  EventDate = note.EventDate!.Value,
                                  note.Minutes
                              })
                .ToListAsync(cancellationToken);

            var months = rows
                .GroupBy(row => (row.EventDate.Year, row.EventDate.Month))
                .OrderBy(group => group.Key.Year)
                .ThenBy(group => group.Key.Month)
                .Select(group => new ProductivityMonthUnitsDto(
                    group.Key.Year,
                    group.Key.Month,
                    group.Sum(row => CalculateUnits(row.Minutes))))
                .ToList();
            return Results.Ok(months);
        });

        api.MapGet("/reports/consumer-billing-loss", async Task<IResult> (
            DateTime start,
            DateTime end,
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            start = start.Date;
            end = end.Date;
            if (end < start || start.Year < 2000 || end.Year > 2200 || (end - start).TotalDays > 3_660)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["window"] = ["The report window must be valid, within 2000-2200, and no longer than 10 years."]
                });
            }

            var actor = Actor.From(principal);
            if (!await TenantAccess.CanAccessUserAsync(db, actor, actor.UserId, cancellationToken))
                return Results.Forbid();
            var compliancePolicy = await LoadBillingCompliancePolicyContextAsync(
                db, actor.AgencyId, cancellationToken);
            var people = await TenantAccess.OwnedPeople(db, actor).AsNoTracking()
                .OrderBy(x => x.LastName)
                .ThenBy(x => x.FirstName)
                .Select(x => new BillingLossPersonRow(x.Id, x.FirstName, x.LastName, x.EffectiveDate))
                .ToListAsync(cancellationToken);
            var personIds = people.Select(x => x.Id).ToList();
            if (personIds.Count == 0)
                return Results.Ok(new ConsumerBillingLossReportDto([], 0, 0, null));

            var forms = await db.Forms.AsNoTracking()
                .Where(x => personIds.Contains(x.PersonId))
                .Select(x => new BillingLossFormRow(
                    x.Id,
                    x.PersonId,
                    x.Type,
                    x.DueDate,
                    x.CompletedDate,
                    x.OpenedDate,
                    x.TargetEffectiveDate))
                .ToListAsync(cancellationToken);
            var releasesByPerson = await LoadReleaseBillingRowsByPersonAsync(
                db, personIds, cancellationToken);
            var providerLinksByPerson = await LoadReleaseProviderLinksByPersonAsync(
                db, actor.AgencyId, personIds, cancellationToken);
            var contactsByPerson = await LoadContactFactsByPersonAsync(
                db, actor.AgencyId, personIds, cancellationToken);
            var endExclusive = end.AddDays(1);
            var notes = await db.Notes.AsNoTracking()
                .Where(x => personIds.Contains(x.PersonId) && x.AgencyId == actor.AgencyId &&
                            x.EventDate.HasValue &&
                            x.EventDate.Value >= start &&
                            x.EventDate.Value < endExclusive &&
                            (x.Status == 1 || x.Status == 2 || x.Status == 3 ||
                             x.Status == 6 || x.Status == 7 || x.Status == 9))
                .Select(x => new BillingLossNoteRow(x.PersonId, x.EventDate!.Value, x.Minutes))
                .ToListAsync(cancellationToken);

            var formsByPerson = forms.GroupBy(x => x.PersonId).ToDictionary(x => x.Key, x => x.ToList());
            var notesByPerson = notes.GroupBy(x => x.PersonId).ToDictionary(x => x.Key, x => x.ToList());
            var consumers = new List<ConsumerBillingLossRowDto>(people.Count);
            foreach (var person in people)
            {
                var activeStart = person.EffectiveDate is DateTime effectiveDate && effectiveDate.Date > start
                    ? effectiveDate.Date
                    : start;
                var totalDays = activeStart <= end ? (end - activeStart).Days + 1 : 0;
                var blockedDates = new HashSet<DateTime>();
                if (totalDays > 0)
                {
                    var personForms = formsByPerson.GetValueOrDefault(person.Id) ?? [];
                    var personReleases = releasesByPerson.GetValueOrDefault(person.Id) ?? [];
                    var personProviderLinks = providerLinksByPerson
                        .GetValueOrDefault(person.Id) ?? [];
                    var contactObligations = MonthlyContactRules.BuildObligations(
                        person.EffectiveDate, contactsByPerson[person.Id]);
                    for (var date = activeStart; date <= end; date = date.AddDays(1))
                    {
                        var releaseFacts = ExpectedBillingComplianceObligations.IncludeMissingReleases(
                            person.EffectiveDate,
                            personReleases.Select(item => item.ToComplianceFact()),
                            date,
                            personProviderLinks);
                        var reconciledCycles = releaseFacts
                            .Where(item => item.TargetEffectiveDate is not null)
                            .Select(item => item.TargetEffectiveDate!.Value.Date)
                            .ToHashSet();
                        var storedForms = personForms
                            .Where(form => !IsLegacyReleaseFormType(form.Type) ||
                                !reconciledCycles.Contains(
                                    (form.TargetEffectiveDate == default
                                        ? form.DueDate
                                        : form.TargetEffectiveDate).Date))
                            .Select(form => new ComplianceFormSnapshot(
                                form.Type, form.DueDate, form.CompletedDate,
                                form.OpenedDate, $"form:{form.Id}",
                                TargetEffectiveDate: form.TargetEffectiveDate == default
                                    ? null
                                    : form.TargetEffectiveDate));
                        var formObligations = BillingComplianceGate.IncludeOpeningObligations(
                            ExpectedBillingComplianceObligations.IncludeMissingForms(
                                person.EffectiveDate, storedForms, date,
                                compliancePolicy.Schedule));
                        if (BillingComplianceGate.EvaluateBillingWindow(
                                formObligations.Concat(
                                    ReleaseBillingRules.BuildComplianceSnapshots(
                                        releaseFacts,
                                        date))
                                    .Concat(contactObligations),
                                date,
                                compliancePolicy.Resolve(date)).Count > 0)
                            blockedDates.Add(date);
                    }
                }

                var billableUnits = 0;
                var nonBillableUnits = 0;
                if (notesByPerson.TryGetValue(person.Id, out var personNotes))
                {
                    foreach (var note in personNotes.Where(x => x.EventDate.Date >= activeStart))
                    {
                        var units = CalculateUnits(note.Minutes);
                        if (blockedDates.Contains(note.EventDate.Date))
                            nonBillableUnits += units;
                        else
                            billableUnits += units;
                    }
                }

                var totalUnits = billableUnits + nonBillableUnits;
                var name = $"{person.FirstName ?? string.Empty} {person.LastName ?? string.Empty}".Trim();
                consumers.Add(new ConsumerBillingLossRowDto(
                    person.Id,
                    string.IsNullOrWhiteSpace(name) ? $"Consumer #{person.Id}" : name,
                    totalDays - blockedDates.Count,
                    blockedDates.Count,
                    billableUnits,
                    nonBillableUnits,
                    totalUnits > 0 ? Math.Round(100m * nonBillableUnits / totalUnits, 1) : null));
            }

            var totalBillableUnits = consumers.Sum(x => x.BillableUnits);
            var totalNonBillableUnits = consumers.Sum(x => x.NonBillableUnits);
            var totalWorkUnits = totalBillableUnits + totalNonBillableUnits;
            return Results.Ok(new ConsumerBillingLossReportDto(
                consumers,
                totalBillableUnits,
                totalNonBillableUnits,
                totalWorkUnits > 0
                    ? Math.Round(100m * totalNonBillableUnits / totalWorkUnits, 1)
                    : null));
        });
    }

    private static void MapBilling(RouteGroupBuilder api)
    {
        api.MapGet("/billing/configuration", async Task<IResult> (
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!actor.HasBillingPermissions)
                return Results.Forbid();
            var agency = await db.Agencies.AsNoTracking()
                .SingleAsync(candidate => candidate.Id == actor.AgencyId, cancellationToken);
            return Results.Ok(ContractMapper.ToBillingConfiguration(agency));
        });

        api.MapPut("/billing/configuration", async Task<IResult> (
            SaveBillingConfigurationRequest request,
            ClaimsPrincipal principal,
            ApiDbContext db,
            AuditTrail auditTrail,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!actor.HasBillingPermissions)
                return Results.Forbid();

            var procedureCode = Normalize(request.ProcedureCode)?.ToUpperInvariant() ?? string.Empty;
            var modifier = Normalize(request.Modifier)?.ToUpperInvariant();
            var submitterId = Normalize(request.EdiSubmitterId) ?? string.Empty;
            var payerName = Normalize(request.PayerName) ?? string.Empty;
            var payerId = Normalize(request.PayerId) ?? string.Empty;
            var contactName = Normalize(request.ContactName) ?? string.Empty;
            var contactPhone = new string((request.ContactPhone ?? string.Empty).Where(char.IsDigit).ToArray());
            var errors = new Dictionary<string, string[]>();
            if (!BillingRules.IsValidProcedureCode(procedureCode))
                errors["procedureCode"] = ["Procedure code must contain four or five letters or digits."];
            if (!BillingRules.IsValidModifier(modifier))
                errors["modifier"] = ["Modifier must be blank or contain exactly two letters or digits."];
            if (request.UnitRate is null or <= 0 or > 100_000)
                errors["unitRate"] = ["Unit rate must be greater than zero and no more than 100,000."];
            if (!BillingRules.IsSafeX12Element(submitterId, 15))
                errors["ediSubmitterId"] = ["Submitter ID is required, must be X12-safe, and cannot exceed 15 characters."];
            if (!BillingRules.IsSafeX12Element(payerName, 60) || !BillingRules.IsSafeX12Element(payerId, 80))
                errors["payer"] = ["Payer name and ID are required and must be safe X12 values."];
            if (!BillingRules.IsSafeX12Element(contactName, 60) || contactPhone.Length is < 10 or > 15)
                errors["contact"] = ["Contact name and a 10-to-15 digit telephone number are required."];
            if (errors.Count > 0)
                return Results.ValidationProblem(errors);

            var agency = await db.Agencies.SingleAsync(
                candidate => candidate.Id == actor.AgencyId, cancellationToken);
            agency.BillingProcedureCode = procedureCode;
            agency.BillingModifier = modifier;
            agency.BillingUnitRate = request.UnitRate;
            agency.EdiSubmitterId = submitterId;
            agency.EdiPayerName = payerName;
            agency.EdiPayerId = payerId;
            agency.EdiContactName = contactName;
            agency.EdiContactPhone = contactPhone;
            auditTrail.Record(actor, AuditActions.BillingConfigurationUpdated, "Agency", agency.Id);
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(ContractMapper.ToBillingConfiguration(agency));
        });

        api.MapPost("/billing/periods/{year:int}/{month:int}", async Task<IResult> (
            int year,
            int month,
            int? userId,
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!actor.HasBillingPermissions)
                return Results.Forbid();
            var targetUserId = userId ?? actor.UserId;
            var ownerName = month is >= 1 and <= 12 && year is >= 2000 and <= 2200
                ? await db.Users.AsNoTracking()
                    .Where(user => user.Id == targetUserId && user.AgencyId == actor.AgencyId)
                    .Select(user => user.DisplayName)
                    .SingleOrDefaultAsync(cancellationToken)
                : null;
            if (ownerName is null)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["period"] = ["The billing period is invalid."] });

            var period = await db.BillingPeriods.Include(candidate => candidate.Lines)
                .SingleOrDefaultAsync(candidate => candidate.UserId == targetUserId &&
                                                   candidate.Month == month && candidate.Year == year,
                    cancellationToken);
            if (period is null)
            {
                period = new ServerBillingPeriod
                {
                    UserId = targetUserId,
                    Month = month,
                    Year = year,
                    Status = 0
                };
                db.BillingPeriods.Add(period);
                await db.SaveChangesAsync(cancellationToken);
            }
            return Results.Ok(ContractMapper.ToBillingPeriod(period, ownerName));
        });

        api.MapGet("/billing/periods", async Task<IResult> (
            int? userId,
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!actor.HasBillingPermissions)
                return Results.Forbid();

            var query = from period in db.BillingPeriods.AsNoTracking().Include(candidate => candidate.Lines)
                        join owner in db.Users.AsNoTracking() on period.UserId equals owner.Id
                        where owner.AgencyId == actor.AgencyId && (!userId.HasValue || period.UserId == userId.Value)
                        orderby period.Year descending, period.Month descending
                        select new { Period = period, owner.DisplayName };
            return Results.Ok((await query.ToListAsync(cancellationToken))
                .Select(row => ContractMapper.ToBillingPeriod(row.Period, row.DisplayName))
                .ToList());
        });

        api.MapGet("/billing/overview-periods/{year:int}/{month:int}", async Task<IResult> (
            int year,
            int month,
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!actor.HasBillingPermissions)
                return Results.Forbid();
            if (month is < 1 or > 12 || year is < 2000 or > 2200)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["period"] = ["The billing overview month is invalid."]
                });
            }

            var asOf = new DateTime(year, month, 1);
            var firstMonth = asOf.AddMonths(-5);
            var firstMonthKey = firstMonth.Year * 100 + firstMonth.Month;
            var lastMonthKey = year * 100 + month;
            var scopedLines =
                from line in db.ClaimLines.AsNoTracking()
                join period in db.BillingPeriods.AsNoTracking()
                    on line.BillingPeriodId equals period.Id
                join owner in db.Users.AsNoTracking()
                    on period.UserId equals owner.Id
                where owner.AgencyId == actor.AgencyId
                select new
                {
                    period.Year,
                    period.Month,
                    period.Status,
                    line.ChargeAmount
                };

            var draftRevenue = await scopedLines
                .Where(row => row.Status == 0)
                .SumAsync(row => (decimal?)row.ChargeAmount, cancellationToken) ?? 0m;
            var totals = await scopedLines
                .Where(row => row.Year * 100 + row.Month >= firstMonthKey &&
                              row.Year * 100 + row.Month <= lastMonthKey)
                .GroupBy(row => new { row.Year, row.Month })
                .Select(group => new BillingMonthChargeDto(
                    group.Key.Year,
                    group.Key.Month,
                    group.Sum(row => row.ChargeAmount)))
                .ToListAsync(cancellationToken);
            var totalsByMonth = totals.ToDictionary(
                row => (row.Year, row.Month),
                row => row.BilledAmount);
            var months = Enumerable.Range(0, 6)
                .Select(offset => firstMonth.AddMonths(offset))
                .Select(date => new BillingMonthChargeDto(
                    date.Year,
                    date.Month,
                    totalsByMonth.GetValueOrDefault((date.Year, date.Month))))
                .ToList();
            return Results.Ok(new BillingPeriodOverviewDto(draftRevenue, months));
        });

        api.MapGet("/billing/submissions", async Task<IResult> (
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!actor.HasBillingPermissions)
                return Results.Forbid();

            var rows = await (from item in db.BillingSubmissionEvents.AsNoTracking()
                              join period in db.BillingPeriods.AsNoTracking() on item.BillingPeriodId equals period.Id
                              join owner in db.Users.AsNoTracking() on period.UserId equals owner.Id
                              where item.AgencyId == actor.AgencyId && owner.AgencyId == actor.AgencyId
                              orderby item.OccurredAtUtc descending
                              select new BillingSubmissionHistoryDto(
                                  item.Id, period.Id, period.Year, period.Month, owner.DisplayName,
                                  period.Lines.Count, item.OccurredAtUtc, item.Stage.ToString(),
                                  item.Reference, item.ResponseType, item.ResponseCode,
                                  item.Explanation, item.IsSynthetic)
                              { EdiGenerationId = item.EdiGenerationId, ResponseId = item.ResponseId }).ToListAsync(cancellationToken);
            rows.AddRange(await DeriveReconciledRowsAsync(db, actor.AgencyId, rows, cancellationToken));
            return Results.Ok(rows);
        });

        api.MapGet("/billing/remittances", async Task<IResult> (
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!actor.HasBillingPermissions)
                return Results.Forbid();

            var rows = await db.RemittanceClaimOutcomes.AsNoTracking()
                .Where(item => item.AgencyId == actor.AgencyId)
                .OrderByDescending(item => item.ReceivedAtUtc)
                .Select(item => new RemittanceClaimOutcomeDto(
                    item.Id, item.BillingPeriodId, item.ClaimReference, item.PayerName,
                    item.ReceivedAtUtc, item.PaymentDate, item.Status.ToString(),
                    item.BilledAmount, item.AllowedAmount, item.PaidAmount,
                    item.AdjustmentAmount, item.PatientResponsibilityAmount,
                    item.ReasonCode, item.Explanation, item.PaymentReference,
                    item.IsSynthetic))
                .ToListAsync(cancellationToken);
            return Results.Ok(rows);
        });

        api.MapGet("/billing/remittance-deposits", async Task<IResult> (
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!actor.HasBillingPermissions)
                return Results.Forbid();

            var deposits = await db.RemittanceDeposits.AsNoTracking()
                .Where(item => item.AgencyId == actor.AgencyId)
                .OrderByDescending(item => item.ReceivedAtUtc)
                .ToListAsync(cancellationToken);
            var records = await db.EftDepositRecords.AsNoTracking()
                .Where(item => item.AgencyId == actor.AgencyId)
                .ToListAsync(cancellationToken);
            return Results.Ok(deposits.Select(item =>
            {
                var entries = records.Where(record => record.RemittanceDepositId == item.Id).ToList();
                return ToDepositDto(item, entries.MaxBy(record => record.Id), entries.Count);
            }).ToList());
        });

        MapClaimResponseIntake(api);
        MapBillingCorrections(api);

        // Drive the mock clearinghouse. Scaffolding: it fabricates responses and then hands
        // them to the same ingestion path above, rather than writing rows directly, so the
        // permanent path is what gets exercised.
        api.MapPost("/billing/periods/{periodId:int}/mock-clearinghouse", async Task<IResult> (
            int periodId,
            MockClearinghouseRequest request,
            ClaimsPrincipal principal,
            ApiDbContext db,
            AuditTrail auditTrail,
            IOptions<SatiApiOptions> options,
            IHostEnvironment hostEnvironment,
            ClaimResponseIngestion ingestion,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!actor.HasBillingPermissions)
                return Results.Forbid();

            // Same gate as the Demo seed command, and NotFound rather than Forbid so the
            // route is absent in effect on Production rather than merely denied.
            var isValidatedDemo =
                string.Equals(options.Value.ExpectedDatabaseName, "SatiDemo", StringComparison.Ordinal) &&
                string.Equals(options.Value.ExpectedEnvironment, "Demo", StringComparison.Ordinal);
            var isIsolatedTestHost =
                hostEnvironment.IsEnvironment("Testing") &&
                string.Equals(options.Value.ExpectedDatabaseName, "SatiApiTests", StringComparison.Ordinal) &&
                string.Equals(options.Value.ExpectedEnvironment, "Testing", StringComparison.Ordinal);
            if (!isValidatedDemo && !isIsolatedTestHost)
                return Results.NotFound();

            var period = await (from candidate in db.BillingPeriods.AsNoTracking().Include(value => value.Lines)
                                join owner in db.Users.AsNoTracking() on candidate.UserId equals owner.Id
                                where candidate.Id == periodId && owner.AgencyId == actor.AgencyId
                                select candidate).SingleOrDefaultAsync(cancellationToken);
            if (period is null)
                return Results.NotFound();
            if (period.Lines.Count == 0)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["period"] = ["The billing period has no claim lines to submit."]
                });
            }
            if (period.Status != 1)
            {
                return Results.Conflict(new ApiErrorDto(
                    "billing_period_not_submitted",
                    "Submit and lock the billing period before sending its 837P file.",
                    string.Empty));
            }

            // Submit the exact immutable test file the user generated. Regenerating an 837
            // here would only resemble the file on disk; it would not prove that the thing
            // being acknowledged is the thing the user chose to send.
            var generation = await db.EdiGenerations.AsNoTracking()
                .Where(item => item.AgencyId == actor.AgencyId &&
                               item.BillingPeriodId == periodId && item.IsTest)
                .OrderByDescending(item => item.CreatedAtUtc)
                .ThenByDescending(item => item.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (generation is null)
            {
                return Results.Conflict(new ApiErrorDto(
                    "test_edi_not_generated",
                    "Generate this billing period's test 837P file before submitting it to the mock clearinghouse.",
                    string.Empty));
            }

            var alreadySubmitted = await db.BillingSubmissionEvents.AsNoTracking().AnyAsync(item =>
                item.AgencyId == actor.AgencyId && item.BillingPeriodId == periodId &&
                item.Stage >= BillingSubmissionStage.Transmitted &&
                item.OccurredAtUtc >= generation.CreatedAtUtc,
                cancellationToken);
            if (alreadySubmitted)
            {
                return Results.Conflict(new ApiErrorDto(
                    "test_edi_already_submitted",
                    "That generated test 837P has already been submitted. Generate a new test file before simulating another response.",
                    string.Empty));
            }

            var receivedAt = DateTime.UtcNow;
            MockClearinghouseDocuments documents;
            try
            {
                documents = MockClearinghouse.Respond(generation.Content, request.Scenario, receivedAt);
            }
            catch (InvalidOperationException failure)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["period"] = [failure.Message]
                });
            }

            var stages = new List<string> { BillingSubmissionStage.Transmitted.ToString() };
            var claimOutcomes = 0;
            var depositRecorded = false;

            db.BillingSubmissionEvents.Add(new ServerBillingSubmissionEvent
            {
                AgencyId = actor.AgencyId,
                BillingPeriodId = periodId,
                EdiGenerationId = generation.Id,
                OccurredAtUtc = receivedAt,
                Stage = BillingSubmissionStage.Transmitted,
                Reference = generation.FileName,
                ResponseType = "837P",
                Explanation = "Test 837P submitted to the mock clearinghouse.",
                IsSynthetic = true
            });
            auditTrail.Record(actor, AuditActions.BillingEdiTransmitted, "BillingPeriod", periodId);
            await db.SaveChangesAsync(cancellationToken);

            foreach (var document in new[]
                     {
                         documents.FunctionalAcknowledgement,
                         documents.ClaimAcknowledgement,
                         documents.RemittanceAdvice
                     })
            {
                if (document is null)
                    continue;

                ClaimResponseIngestResultDto outcome;
                try { outcome = await ingestion.ImportAsync(document, actor, periodId, cancellationToken); }
                catch (ClaimResponseRejected rejected) { return ClaimResponseFailure(rejected); }
                if (outcome.StageRecorded is { } stage)
                    stages.Add(stage);
                claimOutcomes += outcome.ClaimOutcomesRecorded;
                depositRecorded |= outcome.DepositRecorded;
            }

            return Results.Ok(new MockClearinghouseResultDto(
                request.Scenario.ToString(),
                documents.FunctionalAcknowledgement,
                documents.ClaimAcknowledgement,
                documents.RemittanceAdvice,
                stages,
                claimOutcomes,
                depositRecorded));
        });

        api.MapGet("/billing/compliance-recovery/{personId:int}", async Task<IResult> (
            int personId,
            ClaimsPrincipal principal,
            ApiDbContext db,
            ApiClock clock,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!actor.HasAdminPermissions)
                return Results.Forbid();

            var inputs = await LoadRecoveryInputsAsync(
                db, actor.AgencyId, personId, cancellationToken);
            if (inputs is null)
                return Results.NotFound();

            return Results.Ok(PrepareRecoveryPlan(inputs, clock.Today));
        });

        api.MapPost("/billing/compliance-recovery/{personId:int}", async Task<IResult> (
            int personId,
            CreateBillingComplianceRecoveryRequest request,
            ClaimsPrincipal principal,
            ApiDbContext db,
            ApiClock clock,
            AuditTrail auditTrail,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!actor.HasAdminPermissions)
                return Results.Forbid();

            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
            var inputs = await LoadRecoveryInputsAsync(
                db, actor.AgencyId, personId, cancellationToken);
            if (inputs is null)
                return Results.NotFound();

            var plan = PrepareRecoveryPlan(inputs, clock.Today);
            var result = BillingComplianceRecoveryRules.CreateDecision(
                plan,
                request.SelectedNoteIds ?? [],
                actor.UserId,
                clock.UtcNow.UtcDateTime,
                request.Explanation,
                request.AttestationConfirmed);
            if (!result.Accepted || result.Decision is null)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["recovery"] = result.Errors.ToArray()
                });
            }

            db.BillingComplianceRecoveryDecisions.Add(
                Sati.Models.BillingComplianceRecoveryDecision.FromContract(result.Decision));
            auditTrail.Record(
                actor,
                AuditActions.BillingComplianceRecoveryRecorded,
                "Person",
                personId,
                JsonSerializer.Serialize(new
                {
                    result.Decision.DecisionId,
                    result.Decision.PersonId,
                    result.Decision.NoteIds,
                    obligations = result.Decision.Obligations.Select(item => new
                    {
                        item.ObligationId,
                        item.DueDate,
                        item.CompletedDate,
                        item.EvidenceId
                    })
                }));
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Results.Conflict(new ApiErrorDto(
                    "billing_recovery_stale",
                    "A selected note was already recovered or changed. Refresh the checklist.",
                    string.Empty));
            }

            return Results.Ok(result.Decision);
        });

        api.MapGet("/billing/candidates", async Task<IResult> (
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!actor.HasBillingPermissions)
                return Results.Forbid();

            var rows = (await BillingCandidateBaseQuery(db, actor.AgencyId)
                    .ToListAsync(cancellationToken))
                .Select(ToReviewableNote)
                .ToList();
            if (rows.Count == 0)
                return Results.Ok(Array.Empty<BillingCandidateDto>());

            var agency = await db.Agencies.AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.Id == actor.AgencyId, cancellationToken);
            var personIds = rows.Select(row => row.Person.Id).Distinct().ToList();
            var formsByPerson = (await db.Forms.AsNoTracking()
                    .Include(form => form.Attestations)
                    .Where(form => personIds.Contains(form.PersonId))
                    .ToListAsync(cancellationToken))
                .GroupBy(form => form.PersonId)
                .ToDictionary(group => group.Key, group => (IReadOnlyList<ServerForm>)group.ToList());
            var releasesByPerson = await LoadReleaseBillingRowsByPersonAsync(
                db, personIds, cancellationToken);
            var providerLinksByPerson = await LoadReleaseProviderLinksByPersonAsync(
                db, actor.AgencyId, personIds, cancellationToken);
            await PopulateContactHistoryAsync(
                db, actor.AgencyId, rows.Select(row => row.Person), cancellationToken);
            var compliancePolicy = await LoadBillingCompliancePolicyContextAsync(
                db, actor.AgencyId, cancellationToken);
            var recoveryByNote = await LoadRecoveryDecisionsByNoteAsync(
                db, actor.AgencyId, rows.Select(row => row.Note.Id), cancellationToken);
            var candidates = rows.Select(row => new BillingCandidateDto(
                row.Note.Id,
                row.Note.EventDate,
                row.Note.Minutes,
                row.Person.Id,
                row.Person.UserId,
                row.Note.ComplianceOverride,
                ValidateBillingCandidate(row.Note, row.Person, agency,
                    formsByPerson.GetValueOrDefault(row.Person.Id) ?? [],
                    releasesByPerson.GetValueOrDefault(row.Person.Id) ?? [],
                    compliancePolicy,
                    recoveryByNote.GetValueOrDefault(row.Note.Id) ?? [],
                    providerLinksByPerson.GetValueOrDefault(row.Person.Id) ?? []))).ToList();
            return Results.Ok(candidates);
        });

        api.MapPost("/billing/claim-lines", async Task<IResult> (
            CreateClaimLineRequest request,
            ClaimsPrincipal principal,
            ApiDbContext db,
            AuditTrail auditTrail,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!actor.HasBillingPermissions)
                return Results.Forbid();

            var row = await (from note in db.Notes
                             join person in db.People on note.PersonId equals person.Id
                             join owner in db.Users on person.UserId equals owner.Id
                             where note.Id == request.NoteId && note.Status == 6 &&
                                   owner.AgencyId == actor.AgencyId &&
                                   person.AgencyId == actor.AgencyId && note.AgencyId == actor.AgencyId
                             select new ReviewableNote(note, person)).SingleOrDefaultAsync(cancellationToken);
            if (row is null)
                return Results.NotFound();
            if (row.Note.EventDate is not DateTime sourceDate)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["note"] = ["No service date."]
                });
            }
            var serviceDate = sourceDate.Date;
            var ownerId = row.Person.UserId;
            // Lock the period before reading the form completion used to approve
            // this financial write. A concurrent revocation must either commit
            // first and be seen here, or wait for this transaction to finish.
            await using var periodWrite = await BillingPeriodWriteScope.BeginAsync(
                db, actor.AgencyId, ownerId, serviceDate.Year, serviceDate.Month,
                cancellationToken);
            db.ChangeTracker.Clear();
            row = await (from note in db.Notes
                         join person in db.People on note.PersonId equals person.Id
                         join owner in db.Users on person.UserId equals owner.Id
                         where note.Id == request.NoteId && note.Status == 6 &&
                               owner.AgencyId == actor.AgencyId &&
                               person.AgencyId == actor.AgencyId && note.AgencyId == actor.AgencyId
                         select new ReviewableNote(note, person)).SingleOrDefaultAsync(cancellationToken);
            if (row is null || row.Note.EventDate?.Date != serviceDate ||
                row.Person.UserId != ownerId)
            {
                return Results.Conflict(new ApiErrorDto("billing_source_changed",
                    "The note's service date, status, or owner changed while billing was being prepared. Refresh and try again.",
                    string.Empty));
            }
            if (await db.ClaimLines.AnyAsync(line => line.NoteId == request.NoteId, cancellationToken))
                return DuplicateClaimLineConflict();
            var agency = await db.Agencies.AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.Id == actor.AgencyId, cancellationToken);
            var forms = await db.Forms.AsNoTracking()
                .Include(form => form.Attestations)
                .Where(form => form.PersonId == row.Person.Id)
                .ToListAsync(cancellationToken);
            var releaseRows = (await LoadReleaseBillingRowsByPersonAsync(
                db, [row.Person.Id], cancellationToken))
                .GetValueOrDefault(row.Person.Id) ?? [];
            var providerLinks = (await LoadReleaseProviderLinksByPersonAsync(
                    db, actor.AgencyId, [row.Person.Id], cancellationToken))
                .GetValueOrDefault(row.Person.Id) ?? [];
            var compliancePolicy = await LoadBillingCompliancePolicyContextAsync(
                db, actor.AgencyId, cancellationToken);
            var recoveryByNote = await LoadRecoveryDecisionsByNoteAsync(
                db, actor.AgencyId, [row.Note.Id], cancellationToken);
            await PopulateContactHistoryAsync(
                db, actor.AgencyId, [row.Person], cancellationToken);
            var errors = ValidateBillingCandidate(
                row.Note, row.Person, agency, forms, releaseRows, compliancePolicy,
                recoveryByNote.GetValueOrDefault(row.Note.Id) ?? [], providerLinks);
            if (errors.Count > 0)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["note"] = errors.ToArray() });

            // Older database rows can predate the service-time reservation guard.
            // Approval normally rechecks this rule, but claim creation is the final
            // financial boundary and must not turn a legacy overlap into billing.
            var serviceTimeConflict = await FindReviewServiceTimeProblemAsync(
                db, row, actor.AgencyId, cancellationToken);
            if (serviceTimeConflict is not null)
                return serviceTimeConflict;

            var period = await db.BillingPeriods
                .Include(candidate => candidate.Lines)
                .SingleOrDefaultAsync(candidate =>
                    candidate.UserId == row.Person.UserId && candidate.Month == serviceDate.Month &&
                    candidate.Year == serviceDate.Year, cancellationToken);
            if (period is null)
            {
                period = new ServerBillingPeriod
                {
                    UserId = row.Person.UserId,
                    Month = serviceDate.Month,
                    Year = serviceDate.Year,
                    Status = 0
                };
                db.BillingPeriods.Add(period);
            }
            if (period.Status != 0)
                return Results.Conflict(new ApiErrorDto("period_submitted", "This billing period is no longer a draft.", string.Empty));

            var line = new ServerClaimLine
            {
                NoteId = row.Note.Id,
                BillingPeriodId = period.Id,
                DateOfService = serviceDate,
                ProcedureCode = agency!.BillingProcedureCode!,
                ProcedureModifier = agency.BillingModifier,
                Units = BillingRules.CalculateSection13Units(row.Note.Minutes),
                ChargeAmount = BillingRules.CalculateCharge(
                    BillingRules.CalculateSection13Units(row.Note.Minutes),
                    agency.BillingUnitRate!.Value),
                ClientMaineCareId = row.Person.MaineCareId!,
                RenderingProviderNpi = agency!.Npi!,
                DiagnosisCode = row.Person.DiagnosisCode!,
                PlaceOfService = row.Person.PlaceOfService!.Value,
                ClaimSnapshotJson = ProfessionalClaimSnapshotCodec.Serialize(
                    CreateClaimSnapshot(row.Person, agency)),
                // The approved note is the sole authority for a compliance
                // exception. A billing request must not be able to add, remove,
                // or rewrite this regulated financial-record fact.
                IsComplianceException = row.Note.ComplianceOverride,
                ComplianceExceptionReason = row.Note.ComplianceOverride
                    ? Normalize(row.Note.OverrideReason)
                    : null
            };
            period.Lines.Add(line);
            var exactClaimReadiness = ProfessionalClaimReadiness.EvaluatePeriod(
                period.Year,
                period.Month,
                period.Lines.Select(ContractMapper.ToReadinessFacts));
            if (!exactClaimReadiness.IsReady)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["claimLine"] = [exactClaimReadiness.ExplainFailure()]
                });
            }
            auditTrail.Record(actor, AuditActions.BillingClaimLineCreated, "Note", request.NoteId);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                await periodWrite.CommitAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (IsDuplicateClaimLine(exception))
            {
                return DuplicateClaimLineConflict();
            }
            return Results.Ok(ContractMapper.ToClaimLine(line));
        });

        api.MapGet("/billing/claim-lines/draft", async Task<IResult> (
            int userId,
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!actor.HasBillingPermissions)
                return Results.Forbid();
            var lines = await (from line in db.ClaimLines.AsNoTracking()
                               join period in db.BillingPeriods.AsNoTracking() on line.BillingPeriodId equals period.Id
                               join owner in db.Users.AsNoTracking() on period.UserId equals owner.Id
                               where period.UserId == userId && period.Status == 0 && owner.AgencyId == actor.AgencyId
                               orderby line.DateOfService
                               select line).ToListAsync(cancellationToken);
            return Results.Ok(lines.Select(ContractMapper.ToClaimLine).ToList());
        });

        api.MapPost("/billing/periods/{periodId:int}/submit", async Task<IResult> (
            int periodId,
            ClaimsPrincipal principal,
            ApiDbContext db,
            AuditTrail auditTrail,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!actor.HasBillingPermissions)
                return Results.Forbid();
            var periodKey = await (from candidate in db.BillingPeriods.AsNoTracking()
                                   join owner in db.Users.AsNoTracking() on candidate.UserId equals owner.Id
                                   where candidate.Id == periodId && owner.AgencyId == actor.AgencyId
                                   select new { candidate.UserId, candidate.Year, candidate.Month })
                .SingleOrDefaultAsync(cancellationToken);
            if (periodKey is null)
                return Results.NotFound();
            await using var periodWrite = await BillingPeriodWriteScope.BeginAsync(
                db, actor.AgencyId, periodKey.UserId, periodKey.Year, periodKey.Month,
                cancellationToken);
            var selected = await (from candidate in db.BillingPeriods.Include(value => value.Lines)
                                  join owner in db.Users on candidate.UserId equals owner.Id
                                  where candidate.Id == periodId && owner.AgencyId == actor.AgencyId
                                  select new { Period = candidate, owner.DisplayName })
                .SingleOrDefaultAsync(cancellationToken);
            if (selected is null)
                return Results.NotFound();
            var period = selected.Period;
            if (period.Status == 1)
                return Results.Ok(ContractMapper.ToBillingPeriod(period, selected.DisplayName));
            if (period.Status != 0)
                return Results.Conflict(new ApiErrorDto("invalid_period_status", "Only draft billing periods can be submitted.", string.Empty));
            if (period.Lines.Count == 0)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["period"] = ["A billing period with no claim lines cannot be submitted."]
                });
            }
            if (EdiReadinessConflict(period) is { } readinessConflict)
                return readinessConflict;
            var complianceErrors = await RevalidateDraftPeriodComplianceAsync(
                db, actor.AgencyId, period, cancellationToken);
            if (complianceErrors.Count > 0)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["compliance"] = complianceErrors.ToArray()
                });
            }
            period.Status = 1;
            period.SubmittedAt = DateTime.UtcNow;
            auditTrail.Record(actor, AuditActions.BillingPeriodSubmitted, "BillingPeriod", periodId);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                await periodWrite.CommitAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                await periodWrite.RollbackAsync(cancellationToken);
                await periodWrite.DisposeAsync();
                db.ChangeTracker.Clear();
                var completed = await (from candidate in db.BillingPeriods.AsNoTracking().Include(value => value.Lines)
                                       join owner in db.Users.AsNoTracking() on candidate.UserId equals owner.Id
                                       where candidate.Id == periodId && owner.AgencyId == actor.AgencyId
                                       select new { Period = candidate, owner.DisplayName })
                    .SingleOrDefaultAsync(cancellationToken);
                if (completed?.Period.Status == 1)
                    return Results.Ok(ContractMapper.ToBillingPeriod(completed.Period, completed.DisplayName));
                return Results.Conflict(new ApiErrorDto("billing_period_changed", "The billing period changed while it was being submitted.", string.Empty));
            }
            return Results.Ok(ContractMapper.ToBillingPeriod(period, selected.DisplayName));
        });

        api.MapPost("/billing/periods/{periodId:int}/return-to-draft", async Task<IResult> (
            int periodId,
            ClaimsPrincipal principal,
            ApiDbContext db,
            AuditTrail auditTrail,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!actor.HasBillingPermissions)
                return Results.Forbid();
            var periodKey = await (from candidate in db.BillingPeriods.AsNoTracking()
                                   join owner in db.Users.AsNoTracking() on candidate.UserId equals owner.Id
                                   where candidate.Id == periodId && owner.AgencyId == actor.AgencyId
                                   select new { candidate.UserId, candidate.Year, candidate.Month })
                .SingleOrDefaultAsync(cancellationToken);
            if (periodKey is null)
                return Results.NotFound();
            await using var transaction = await BillingPeriodWriteScope.BeginAsync(
                db, actor.AgencyId, periodKey.UserId, periodKey.Year, periodKey.Month,
                cancellationToken);
            var selected = await (from candidate in db.BillingPeriods
                                  join owner in db.Users on candidate.UserId equals owner.Id
                                  where candidate.Id == periodId && owner.AgencyId == actor.AgencyId
                                  select new { Period = candidate, owner.DisplayName })
                .SingleOrDefaultAsync(cancellationToken);
            if (selected is null)
                return Results.NotFound();

            var hasExchangeHistory = await db.EdiGenerations.AsNoTracking().AnyAsync(item =>
                    item.AgencyId == actor.AgencyId && item.BillingPeriodId == periodId,
                    cancellationToken) ||
                await db.BillingSubmissionEvents.AsNoTracking().AnyAsync(item =>
                    item.AgencyId == actor.AgencyId && item.BillingPeriodId == periodId,
                    cancellationToken);
            var errors = BillingPeriodWorkflow.ValidateReturnToDraft(
                selected.Period.Status == 1,
                hasExchangeHistory);
            if (errors.Count > 0)
            {
                return Results.Conflict(new ApiErrorDto(
                    "billing_period_cannot_return_to_draft",
                    string.Join(" ", errors),
                    string.Empty));
            }

            selected.Period.Status = 0;
            selected.Period.SubmittedAt = null;
            auditTrail.Record(actor, AuditActions.BillingPeriodReturnedToDraft,
                "BillingPeriod", periodId);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Results.Conflict(new ApiErrorDto(
                    "billing_period_changed",
                    "The billing period changed while it was being returned to draft.",
                    string.Empty));
            }
            return Results.Ok(ContractMapper.ToBillingPeriod(selected.Period, selected.DisplayName));
        });

        api.MapPost("/billing/periods/{periodId:int}/edi", async Task<IResult> (
            int periodId,
            GenerateEdiRequest request,
            ClaimsPrincipal principal,
            ApiDbContext db,
            AuditTrail auditTrail,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!actor.HasBillingPermissions)
                return Results.Forbid();
            if (!Guid.TryParse(request.IdempotencyKey, out var parsedKey))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["idempotencyKey"] = ["A valid idempotency key is required."]
                });
            }
            var normalizedKey = parsedKey.ToString("N");
            var periodKey = await (from candidate in db.BillingPeriods.AsNoTracking()
                                   join owner in db.Users.AsNoTracking() on candidate.UserId equals owner.Id
                                   where candidate.Id == periodId && owner.AgencyId == actor.AgencyId
                                   select new { candidate.UserId, candidate.Year, candidate.Month })
                .SingleOrDefaultAsync(cancellationToken);
            if (periodKey is null)
                return Results.NotFound();
            await using var transaction = await BillingPeriodWriteScope.BeginAsync(
                db, actor.AgencyId, periodKey.UserId, periodKey.Year, periodKey.Month,
                cancellationToken);
            if (!await TenantAccess.IsCurrentActorAsync(db, actor, cancellationToken))
                return Results.Unauthorized();
            var previous = await db.EdiGenerations.AsNoTracking().SingleOrDefaultAsync(generation =>
                generation.AgencyId == actor.AgencyId && generation.ActorUserId == actor.UserId &&
                generation.IdempotencyKey == normalizedKey, cancellationToken);
            if (previous is not null && (previous.BillingPeriodId != periodId || previous.IsTest != request.IsTest))
                return ReplayEdiOrConflict(previous, periodId, request.IsTest);

            var export = await LoadExportablePeriodAsync(db, actor, periodId, cancellationToken);
            if (export.Failure is not null) return export.Failure;
            var period = export.Period!;
            if (previous is not null)
                return ReplayEdiOrConflict(previous, periodId, request.IsTest);

            var generatedAt = DateTime.Now;
            var controlNumber = CreateEdiControlNumber(normalizedKey);
            if (await db.EdiGenerations.AnyAsync(item => item.AgencyId == actor.AgencyId &&
                    item.IsTest == request.IsTest && item.ControlNumber == controlNumber, cancellationToken))
                return Results.Conflict(new ApiErrorDto("edi_control_conflict",
                    "This submission identity has already been used. Start a new generation attempt.", string.Empty));
            var content = ServerEdiGenerator.Generate(
                period, request.IsTest, generatedAt, controlNumber);
            var timestamp = generatedAt.ToString("yyyyMMdd_HHmmss", System.Globalization.CultureInfo.InvariantCulture);
            var testMarker = request.IsTest ? ".OATEST" : string.Empty;
            var file = new EdiFileDto($"837P{testMarker}_{period.Year}{period.Month:D2}_{timestamp}_{normalizedKey[..8]}.txt", content);
            var retainedGeneration = new ServerEdiGeneration
            {
                AgencyId = actor.AgencyId,
                ActorUserId = actor.UserId,
                BillingPeriodId = periodId,
                IdempotencyKey = normalizedKey,
                IsTest = request.IsTest,
                ControlNumber = controlNumber,
                FileName = file.FileName,
                Content = file.Content,
                CreatedAtUtc = DateTime.UtcNow
            };
            db.EdiGenerations.Add(retainedGeneration);
            db.BillingSubmissionEvents.Add(new ServerBillingSubmissionEvent
            {
                AgencyId = actor.AgencyId,
                BillingPeriodId = periodId,
                EdiGeneration = retainedGeneration,
                OccurredAtUtc = DateTime.UtcNow,
                Stage = BillingSubmissionStage.Generated,
                Reference = file.FileName,
                Explanation = request.IsTest
                    ? "Test 837P generated; no external transmission is implied."
                    : "Production 837P generated; transmission status has not been recorded.",
                IsSynthetic = request.IsTest
            });
            auditTrail.Record(actor, AuditActions.BillingEdiGenerated, "BillingPeriod", periodId);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (IsDuplicateEdiGeneration(exception))
            {
                await transaction.RollbackAsync(cancellationToken);
                await transaction.DisposeAsync();
                db.ChangeTracker.Clear();
                // Rollback ended the first authorization/source read boundary.
                // A competing completed file is not an exemption from the gate.
                await using var replayTransaction = await BillingPeriodWriteScope.BeginAsync(
                    db, actor.AgencyId, periodKey.UserId, periodKey.Year, periodKey.Month,
                    cancellationToken);
                var replay = await LoadExportablePeriodAsync(db, actor, periodId, cancellationToken);
                if (replay.Failure is not null) return replay.Failure;
                var completed = await db.EdiGenerations.AsNoTracking().SingleAsync(generation =>
                    generation.AgencyId == actor.AgencyId && generation.ActorUserId == actor.UserId &&
                    generation.IdempotencyKey == normalizedKey, cancellationToken);
                return ReplayEdiOrConflict(completed, periodId, request.IsTest);
            }
            return Results.Ok(file);
        });
    }

    private static void MapDocumentTemplates(RouteGroupBuilder api)
    {
        api.MapGet("/agencies/{agencyId:int}/templates/{kind}", async Task<IResult> (
            int agencyId, string kind, ClaimsPrincipal principal, ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!actor.HasAdminPermissions)
                return Results.Forbid();
            if (agencyId != actor.AgencyId)
                return Results.NotFound();
            if (!Enum.TryParse<AnnualDocumentKind>(kind, true, out var documentKind) ||
                DocumentTemplateRules.AllowedTokens(documentKind).Count == 0)
                return Results.NotFound();
            var templates = await db.DocumentTemplates.AsNoTracking()
                .Where(template => template.Kind == documentKind.ToString() &&
                    (template.AgencyId == agencyId || template.AgencyId == null))
                .OrderByDescending(template => template.Version)
                .ToListAsync(cancellationToken);
            return Results.Ok(templates.Select(ToDocumentTemplateDto).ToList());
        });

        api.MapPost("/agencies/{agencyId:int}/templates/{kind}", async Task<IResult> (
            int agencyId, string kind, PublishDocumentTemplateRequest request,
            ClaimsPrincipal principal, ApiDbContext db, AuditTrail auditTrail, ApiClock clock,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!actor.HasAdminPermissions)
                return Results.Forbid();
            if (agencyId != actor.AgencyId)
                return Results.NotFound();
            if (!Enum.TryParse<AnnualDocumentKind>(kind, true, out var documentKind) ||
                DocumentTemplateRules.AllowedTokens(documentKind).Count == 0)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["kind"] = ["This document kind does not use a published template."]
                }, statusCode: StatusCodes.Status422UnprocessableEntity);
            var validation = DocumentTemplateRules.Validate(documentKind, request.Body);
            if (validation.Count > 0)
                return Results.ValidationProblem(validation.ToDictionary(item => item.Key, item => item.Value),
                    statusCode: StatusCodes.Status422UnprocessableEntity);

            var version = (await db.DocumentTemplates
                .Where(template => template.AgencyId == agencyId && template.Kind == documentKind.ToString())
                .MaxAsync(template => (int?)template.Version, cancellationToken) ?? 0) + 1;
            var template = new ServerDocumentTemplate
            {
                AgencyId = agencyId,
                Kind = documentKind.ToString(),
                Version = version,
                Body = request.Body.Trim(),
                PublishedAtUtc = clock.UtcNow.UtcDateTime,
                PublishedByUserId = actor.UserId
            };
            db.DocumentTemplates.Add(template);
            auditTrail.Record(actor, AuditActions.DocumentTemplatePublished, "Agency", agencyId,
                JsonSerializer.Serialize(new { kind = template.Kind, version }));
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (IsDuplicateTemplateVersion(exception))
            {
                return Results.Conflict(new ApiErrorDto("template_version_changed",
                    "Another template version was published. Refresh and try again.", string.Empty));
            }
            return Results.Ok(ToDocumentTemplateDto(template));
        });
    }

    private static DocumentTemplateDto ToDocumentTemplateDto(ServerDocumentTemplate template) => new(
        template.Id, template.AgencyId, template.Kind, template.Version, template.Body,
        template.PublishedAtUtc, template.PublishedByUserId, template.RetiredAtUtc,
        DocumentTemplateRules.OwnerName(template.AgencyId));

    private static bool IsDuplicateTemplateVersion(DbUpdateException exception) =>
        exception.InnerException is SqlException sqlException &&
        sqlException.Number is 2601 or 2627 &&
        sqlException.Message.Contains("IX_DocumentTemplates_AgencyKindVersion", StringComparison.Ordinal)
        || exception.InnerException?.Message.Contains(
            "DocumentTemplates.AgencyId, DocumentTemplates.Kind, DocumentTemplates.Version",
            StringComparison.Ordinal) == true;

    private static void MapDocuments(RouteGroupBuilder api)
    {
        api.MapPost("/people/{personId:int}/documents/{kind}", async Task<IResult> (
            int personId,
            string kind,
            RenderAnnualDocumentRequest request,
            ClaimsPrincipal principal,
            HttpContext httpContext,
            ApiDbContext db,
            AgencyReleasePdfGenerator agencyGenerator,
            MedicalReleasePdfGenerator medicalGenerator,
            SafetyPlanPdfGenerator safetyPlanGenerator,
            DocumentTemplatePdfComposer templateComposer,
            AuditTrail auditTrail,
            ApiClock clock,
            CancellationToken cancellationToken) =>
        {
            if (!Enum.TryParse<AnnualDocumentKind>(kind, true, out var documentKind) ||
                !Enum.IsDefined(documentKind) ||
                documentKind is not (AnnualDocumentKind.ReleaseAgency or
                    AnnualDocumentKind.ReleaseMedical or AnnualDocumentKind.PrivacyPractices or AnnualDocumentKind.SafetyPlan))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["kind"] = ["This document kind is not rendered by the release generator."]
                }, statusCode: StatusCodes.Status422UnprocessableEntity);
            }
            var isRelease = documentKind is AnnualDocumentKind.ReleaseAgency or AnnualDocumentKind.ReleaseMedical;
            var release = request.Release;
            if (isRelease && release is null)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["release"] = ["Release details are required."]
                }, statusCode: StatusCodes.Status422UnprocessableEntity);
            }
            if (release is not null)
            {
                var validation = AgencyReleaseRules.Validate(release);
                if (validation.Count > 0)
                    return Results.ValidationProblem(validation);
                if (release.IsRevocation && request.ReleaseObligationId is not null)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["releaseObligationId"] =
                        ["A revocation cannot replace the document linked to a release obligation. Record the withdrawal separately so the historical authorization is preserved."]
                    }, statusCode: StatusCodes.Status422UnprocessableEntity);
                }
            }

            var actor = Actor.From(principal);
            var person = await AccessibleSafetyPerson(db, actor, personId, cancellationToken);
            if (person is null || (documentKind is not (AnnualDocumentKind.SafetyPlan or AnnualDocumentKind.PrivacyPractices) &&
                !await TenantAccess.OwnsPersonAsync(db, actor, personId, cancellationToken)))
                return Results.NotFound();
            if (person.EffectiveDate is not DateTime effectiveDate)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["person"] = ["The consumer has no effective date."]
                }, statusCode: StatusCodes.Status422UnprocessableEntity);
            }
            var agency = await db.Agencies.AsNoTracking().SingleAsync(
                candidate => candidate.Id == actor.AgencyId, cancellationToken);
            var generatedAtUtc = clock.UtcNow.UtcDateTime;
            var requestedCycleStart = request.CycleStart?.Date;
            var cycleStart = requestedCycleStart ??
                AnnualDocumentCycle.CurrentStart(effectiveDate, generatedAtUtc.ToLocalTime());
            var releaseLink = await ResolveDocumentReleaseObligationAsync(
                db, actor, personId, documentKind, requestedCycleStart,
                request.ReleaseObligationId, clock.Today, cancellationToken);
            if (releaseLink.Error is not null)
                return releaseLink.Error;
            // The durable obligation owns its annual-cycle identity. This matters while the
            // next cycle is already available: an exact link must not be silently forced into
            // whichever cycle happens to be current on the server today.
            cycleStart = releaseLink.Obligation?.TargetEffectiveDate.Date ?? cycleStart;
            if (AnnualDocumentCycle.CurrentStart(effectiveDate, cycleStart) != cycleStart)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["cycleStart"] = ["The document cycle must begin on the consumer's effective-date anniversary."]
                }, statusCode: StatusCodes.Status422UnprocessableEntity);
            }
            if (documentKind == AnnualDocumentKind.SafetyPlan)
            {
                var safetyTiming = await GetSafetyTimingAsync(
                    db, actor.AgencyId, cancellationToken);
                if (!AnnualDocumentCycle.IsAvailable(
                        cycleStart,
                        clock.Today,
                        safetyTiming.OpenDaysBefore,
                        safetyTiming.DueDaysBeforeEffective))
                    return SafetyCycleNotAvailable(cycleStart, safetyTiming);
            }

            var subject = new AgencyReleaseSubject(
                person.Id,
                $"{person.FirstName} {person.LastName}".Trim(),
                person.BirthDate,
                person.HasGuardian ? person.GuardianName : null,
                agency.Name,
                ComposeAddress(agency.Street, agency.City, agency.State, agency.Zip),
                agency.EdiContactPhone,
                actor.DisplayName,
                actor.Role);
            byte[] pdf;
            IReadOnlyCollection<string> blankFields;
            string? templateOwner = null;
            string? templateKey = null;
            int? templateVersion = null;
            int? sourceContentId = null;
            int? sourceContentVersion = null;
            var origin = release?.IsDraft == true && isRelease
                ? DocumentArtifactOrigin.Draft
                : DocumentArtifactOrigin.GeneratedInSati;
            if (documentKind == AnnualDocumentKind.SafetyPlan)
            {
                var plan = await db.SafetyPlans.AsNoTracking().Where(x => x.PersonId == personId && x.CycleStart == cycleStart && x.Status != "Superseded")
                    .OrderByDescending(x => x.Version).FirstOrDefaultAsync(cancellationToken);
                if (plan is null) return Results.ValidationProblem(new Dictionary<string, string[]> { ["safetyPlan"] = ["Create the safety plan before rendering it."] }, statusCode: StatusCodes.Status422UnprocessableEntity);
                var validation = SafetyPlanRules.Validate(plan.DocumentJson, plan.Status == "Approved");
                if (validation.Count > 0) return Results.ValidationProblem(validation, statusCode: StatusCodes.Status422UnprocessableEntity);
                var content = JsonSerializer.Deserialize<SafetyPlanDocument>(plan.DocumentJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
                pdf = safetyPlanGenerator.Generate(subject.ConsumerName ?? string.Empty, cycleStart, content, plan.Status, generatedAtUtc);
                blankFields = content.Sections.Where(x => string.IsNullOrWhiteSpace(x.Text)).Select(x => x.Id).ToArray();
                origin = plan.Status == "Approved" ? DocumentArtifactOrigin.GeneratedInSati : DocumentArtifactOrigin.Draft;
                sourceContentId = plan.Id;
                sourceContentVersion = plan.Version;
            }
            else if (documentKind == AnnualDocumentKind.PrivacyPractices)
            {
                var templates = await db.DocumentTemplates.AsNoTracking()
                    .Where(template => template.Kind == documentKind.ToString() &&
                        (template.AgencyId == actor.AgencyId || template.AgencyId == null))
                    .ToListAsync(cancellationToken);
                var selected = DocumentTemplateResolution.Resolve(actor.AgencyId, documentKind,
                    templates.Select(template => new DocumentTemplateFact(
                        template.Id, template.AgencyId, template.Kind, template.Version,
                        template.PublishedAtUtc, template.RetiredAtUtc)));
                if (selected is null)
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["template"] = ["No published template is available for this document."]
                    }, statusCode: StatusCodes.Status422UnprocessableEntity);
                var template = templates.Single(candidate => candidate.Id == selected.Id);
                var rendered = templateComposer.Generate(documentKind, template.Body,
                    new DocumentTemplateRenderContext(
                        subject.AgencyName, subject.AgencyAddress, subject.AgencyPhone,
                        subject.ConsumerName, subject.BirthDate,
                        cycleStart, AnnualDocumentCycle.EndInclusive(effectiveDate, cycleStart),
                        subject.CaseManagerName, subject.CaseManagerRole), generatedAtUtc);
                pdf = rendered.Pdf;
                blankFields = rendered.BlankFields;
                templateOwner = DocumentTemplateRules.OwnerName(template.AgencyId);
                templateKey = template.Kind;
                templateVersion = template.Version;
            }
            else
            {
                pdf = documentKind == AnnualDocumentKind.ReleaseMedical
                    ? medicalGenerator.Generate(subject, release!, generatedAtUtc)
                    : agencyGenerator.Generate(subject, release!, generatedAtUtc);
                blankFields = release!.IsDraft ? ReleaseDraftBlankFields(release) : [];
            }
            var safeName = SafeFileName($"{person.LastName}-{person.FirstName}");
            var prefix = documentKind switch
            {
                AnnualDocumentKind.ReleaseMedical => "Medical-Release",
                AnnualDocumentKind.PrivacyPractices => "Privacy-Practices",
                AnnualDocumentKind.SafetyPlan => "Safety-Plan",
                _ => "Agency-Release"
            };
            if (isRelease && release!.IsRevocation)
                prefix += "-Revocation";
            if (origin == DocumentArtifactOrigin.Draft)
                prefix += "-DRAFT";
            var fileName = $"{prefix}-{personId}-{safeName}.pdf";

            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await DocumentArtifactPersistence.StageGeneratedAsync(
                db, personId, actor.AgencyId, documentKind, cycleStart,
                origin,
                generatedAtUtc, actor.UserId, pdf, fileName,
                blankFields, cancellationToken, templateOwner, templateKey, templateVersion,
                sourceContentId, sourceContentVersion, releaseLink.Obligation?.Id);
            auditTrail.Record(actor, AuditActions.DocumentGenerated, "Person", personId,
                JsonSerializer.Serialize(new
                {
                    kind = documentKind.ToString(),
                    cycleStart = cycleStart.ToString("yyyy-MM-dd"),
                    origin = origin.ToString(),
                    templateOwner,
                    templateKey,
                    templateVersion,
                    sourceContentId,
                    sourceContentVersion,
                    releaseObligationId = releaseLink.Obligation?.ObligationId,
                    releaseObligationKey = releaseLink.Obligation?.StableKey
                }));
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            PreventSensitiveResponseCaching(httpContext);
            return Results.File(pdf, "application/pdf", fileName);
        });

        api.MapPost("/people/{personId:int}/documents/{kind}/external", async Task<IResult> (
            int personId,
            string kind,
            RecordExternalDocumentRequest request,
            ClaimsPrincipal principal,
            ApiDbContext db,
            AuditTrail auditTrail,
            ApiClock clock,
            CancellationToken cancellationToken) =>
        {
            if (!Enum.TryParse<AnnualDocumentKind>(kind, true, out var documentKind) ||
                !Enum.IsDefined(documentKind) ||
                (AnnualDocumentCatalog.ForKind(documentKind).SatisfiesFormType is null &&
                 documentKind != AnnualDocumentKind.DhhsAuthorizedRepresentative))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["kind"] = ["Unknown prerequisite document kind."] });
            var noteError = AnnualDocumentRules.ValidateExternalNote(request.Note);
            if (noteError is not null)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["note"] = [noteError] });

            var actor = Actor.From(principal);
            var person = await db.People.AsNoTracking().SingleOrDefaultAsync(candidate =>
                candidate.Id == personId && candidate.AgencyId == actor.AgencyId, cancellationToken);
            if (person is null ||
                !await TenantAccess.CanAccessPersonAsync(db, actor, person, cancellationToken))
                return Results.NotFound();
            if (documentKind == AnnualDocumentKind.DhhsAuthorizedRepresentative &&
                !await TenantAccess.OwnsPersonAsync(db, actor, personId, cancellationToken))
                return Results.NotFound();
            if (person.EffectiveDate is not DateTime effectiveDate ||
                AnnualDocumentCycle.CurrentStart(effectiveDate, request.CycleStart) != request.CycleStart.Date)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["cycleStart"] = ["The document cycle must begin on the consumer's effective-date anniversary."]
                }, statusCode: StatusCodes.Status422UnprocessableEntity);
            }
            var releaseLink = await ResolveDocumentReleaseObligationAsync(
                db, actor, personId, documentKind, request.CycleStart.Date,
                request.ReleaseObligationId, clock.Today, cancellationToken);
            if (releaseLink.Error is not null)
                return releaseLink.Error;

            await using var transaction = await db.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable, cancellationToken);
            if (documentKind == AnnualDocumentKind.DhhsAuthorizedRepresentative &&
                await db.DocumentArtifacts.AnyAsync(artifact => artifact.PersonId == personId &&
                    artifact.Kind == nameof(AnnualDocumentKind.DhhsAuthorizedRepresentative) &&
                    artifact.Origin == nameof(DocumentArtifactOrigin.RecordedAsExternal) &&
                    artifact.SupersededByArtifactId == null, cancellationToken))
            {
                return Results.Conflict(new ApiErrorDto(
                    "authorized_representative_already_on_file",
                    "A signed DHHS Authorized Representative form is already recorded on file.",
                    string.Empty));
            }
            var artifact = await DocumentArtifactPersistence.StageExternalAsync(
                db, personId, actor.AgencyId, documentKind, request.CycleStart,
                clock.UtcNow.UtcDateTime, actor.UserId, request.Note, cancellationToken,
                releaseLink.Obligation?.Id);
            auditTrail.Record(actor, AuditActions.DocumentRecordedExternal, "Person", personId,
                JsonSerializer.Serialize(new
                {
                    kind = documentKind.ToString(),
                    cycleStart = request.CycleStart.Date.ToString("yyyy-MM-dd"),
                    releaseObligationId = releaseLink.Obligation?.ObligationId,
                    releaseObligationKey = releaseLink.Obligation?.StableKey
                }));
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Results.Ok(DocumentArtifactPersistence.ToDto(artifact));
        });

        api.MapGet("/people/{personId:int}/documents", async Task<IResult> (
            int personId,
            DateTime cycleStart,
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            var person = await db.People.AsNoTracking().SingleOrDefaultAsync(candidate =>
                candidate.Id == personId && candidate.AgencyId == actor.AgencyId, cancellationToken);
            if (person is null ||
                !await TenantAccess.CanAccessPersonAsync(db, actor, person, cancellationToken))
                return Results.NotFound();
            var artifacts = await db.DocumentArtifacts.AsNoTracking()
                .Where(artifact => artifact.PersonId == personId &&
                    artifact.CycleStart == cycleStart.Date && artifact.SupersededByArtifactId == null)
                .OrderBy(artifact => artifact.Kind)
                .ToListAsync(cancellationToken);
            return Results.Ok(artifacts.Select(DocumentArtifactPersistence.ToDto).ToList());
        });
    }

    private static async Task<DocumentReleaseLinkResolution> ResolveDocumentReleaseObligationAsync(
        ApiDbContext db,
        Actor actor,
        int personId,
        AnnualDocumentKind documentKind,
        DateTime? requestedCycleStart,
        Guid? obligationId,
        DateTime today,
        CancellationToken cancellationToken)
    {
        var expectedCategory = documentKind switch
        {
            AnnualDocumentKind.ReleaseAgency => ReleaseObligationCategory.Agency,
            AnnualDocumentKind.ReleaseMedical => ReleaseObligationCategory.Medical,
            AnnualDocumentKind.ReleaseDhhs => ReleaseObligationCategory.Dhhs,
            _ => (ReleaseObligationCategory?)null
        };
        if (obligationId is null &&
            (documentKind != AnnualDocumentKind.ReleaseDhhs || requestedCycleStart is null))
            return new(null, null);
        if (expectedCategory is null)
        {
            return new(null, Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["releaseObligationId"] =
                    ["Only an agency, medical, or DHHS release document can be linked to a release obligation."]
            }, statusCode: StatusCodes.Status422UnprocessableEntity));
        }

        var obligations = db.ReleaseObligations.AsNoTracking()
            .Include(item => item.AuthorizationEvents)
            .Where(item =>
                item.AgencyId == actor.AgencyId &&
                item.PersonId == personId);
        var obligation = obligationId is Guid exactId
            ? await obligations.SingleOrDefaultAsync(
                item => item.ObligationId == exactId,
                cancellationToken)
            : await obligations.SingleOrDefaultAsync(
                item => item.Category == ReleaseObligationCategory.Dhhs &&
                        item.TargetEffectiveDate == requestedCycleStart!.Value.Date,
                cancellationToken);
        if (obligation is null)
            return obligationId is null
                ? new(null, null)
                : new(null, Results.NotFound());
        if (obligation.Category != expectedCategory.Value)
        {
            return new(null, Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["releaseObligationId"] =
                    [$"The selected obligation requires a {obligation.Category} release, not a {expectedCategory.Value} release."]
            }, statusCode: StatusCodes.Status422UnprocessableEntity));
        }
        if (requestedCycleStart is DateTime cycleStart &&
            obligation.TargetEffectiveDate.Date != cycleStart.Date)
        {
            return new(null, Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["releaseObligationId"] =
                    ["The selected obligation belongs to a different annual effective-date cycle."]
            }, statusCode: StatusCodes.Status422UnprocessableEntity));
        }
        if (today.Date < obligation.AvailableOn.Date)
        {
            return new(null, Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["releaseObligationId"] =
                    [$"This release obligation becomes available on {obligation.AvailableOn:yyyy-MM-dd}."]
            }, statusCode: StatusCodes.Status422UnprocessableEntity));
        }
        if (obligation.RetiredOn is DateTime retiredOn && today.Date >= retiredOn.Date)
        {
            return new(null, Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["releaseObligationId"] =
                    ["This release obligation has been retired and cannot receive a new document."]
            }, statusCode: StatusCodes.Status422UnprocessableEntity));
        }
        if (obligation.WithdrawnOn is not null)
        {
            return new(null, Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["releaseObligationId"] =
                    ["This release authorization was withdrawn and cannot receive a replacement document."]
            }, statusCode: StatusCodes.Status422UnprocessableEntity));
        }

        return new(obligation, null);
    }

    private static async Task<AnnualReleaseTiming> GetDhhsReleaseTimingAsync(
        ApiDbContext db,
        int agencyId,
        CancellationToken cancellationToken)
    {
        var timing = await db.Settings.AsNoTracking()
            .Where(item => item.AgencyId == agencyId)
            .Select(item => new AnnualReleaseTiming(
                item.ReleaseDhhsOpenDaysBefore,
                item.ReleaseDhhsDaysBeforeAnniversary))
            .SingleOrDefaultAsync(cancellationToken);
        return timing is null
            ? new AnnualReleaseTiming(90, 0)
            : new AnnualReleaseTiming(
                Math.Max(0, timing.OpenDaysBefore),
                Math.Max(0, timing.DueDaysBeforeEffective));
    }

    private sealed record DocumentReleaseLinkResolution(
        ReleaseObligation? Obligation,
        IResult? Error);

    private sealed record AnnualReleaseTiming(
        int OpenDaysBefore,
        int DueDaysBeforeEffective);

    private static void MapForms(RouteGroupBuilder api)
    {
        api.MapGet("/people/{personId:int}/forms/{type}/attestations", async Task<IResult> (
            int personId,
            string type,
            int formId,
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            var person = await db.People.AsNoTracking().SingleOrDefaultAsync(candidate =>
                candidate.Id == personId && candidate.AgencyId == actor.AgencyId, cancellationToken);
            if (person is null ||
                !await TenantAccess.CanAccessPersonAsync(db, actor, person, cancellationToken))
                return Results.NotFound();
            var formExists = await db.Forms.AsNoTracking().AnyAsync(candidate =>
                candidate.Id == formId && candidate.PersonId == personId && candidate.Type == type,
                cancellationToken);
            if (!formExists)
                return Results.NotFound();

            var history = await (
                from entry in db.FormAttestations.AsNoTracking()
                join user in db.Users.AsNoTracking()
                    on entry.ActorUserId equals (int?)user.Id into actorUsers
                from user in actorUsers.DefaultIfEmpty()
                where entry.FormId == formId
                orderby entry.RecordedAtUtc descending, entry.Id descending
                select new FormAttestationHistoryDto(
                    entry.Id,
                    entry.FormId,
                    entry.Kind,
                    entry.CompletedOn,
                    entry.ActorKind,
                    entry.ActorUserId,
                    user == null ? "Sati" : user.DisplayName,
                    entry.RecordedAtUtc,
                    entry.EvidenceNoteId,
                    entry.Reason)).ToListAsync(cancellationToken);
            return Results.Ok(history);
        });

        api.MapGet("/people/{personId:int}/forms/{type}/prerequisite", async Task<IResult> (
            int personId,
            string type,
            int formId,
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            var person = await db.People.AsNoTracking().SingleOrDefaultAsync(candidate =>
                candidate.Id == personId && candidate.AgencyId == actor.AgencyId, cancellationToken);
            if (person is null ||
                !await TenantAccess.CanAccessPersonAsync(db, actor, person, cancellationToken))
                return Results.NotFound();
            var form = await db.Forms.AsNoTracking().SingleOrDefaultAsync(candidate =>
                candidate.Id == formId && candidate.PersonId == personId && candidate.Type == type,
                cancellationToken);
            if (form is null || person.EffectiveDate is not DateTime effectiveDate)
                return Results.NotFound();
            var cycle = FormAttestationRules.ResolveCycleForForm(
                effectiveDate,
                form.Type,
                form.DueDate,
                form.TargetEffectiveDate == default ? null : form.TargetEffectiveDate);
            if (cycle is null)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["form"] = ["The form has no valid compliance cycle."] });
            var prerequisite = FormAttestationRules.PrerequisiteFor(form.Type);
            if (prerequisite == PrerequisiteKind.None)
            {
                return Results.Ok(new FormPrerequisiteStatusDto(
                    prerequisite.ToString(),
                    true,
                    "Attestation is sufficient; no separate document prerequisite applies.",
                    [],
                    CanSupervisorOverride: false));
            }

            var assessment = await FindAssessmentForAnnualTargetAsync(
                db,
                personId,
                form,
                cycle.Value,
                cancellationToken);
            var isSatisfied = assessment?.CompletedDate is not null;
            return Results.Ok(new FormPrerequisiteStatusDto(
                prerequisite.ToString(),
                isSatisfied,
                isSatisfied
                    ? "The Comprehensive Assessment for this annual effective date is already attested."
                    : "A completed Reclassification implies a completed Comprehensive Assessment. Enter the actual assessment completion date; Sati will save two separate attestations together.",
                [],
                CanSupervisorOverride: false));
        });

        api.MapPost("/people/{personId:int}/forms/{type}/attestation", async Task<IResult> (
            int personId,
            string type,
            AttestFormRequest request,
            ClaimsPrincipal principal,
            ApiDbContext db,
            ApiClock clock,
            AuditTrail auditTrail,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            var person = await db.People.AsNoTracking()
                .SingleOrDefaultAsync(candidate =>
                    candidate.Id == personId && candidate.AgencyId == actor.AgencyId,
                    cancellationToken);
            if (person is null ||
                !await TenantAccess.CanAccessPersonAsync(db, actor, person, cancellationToken))
                return Results.NotFound();

            await using var formWrite = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
            var form = await db.Forms.SingleOrDefaultAsync(candidate =>
                candidate.Id == request.FormId &&
                candidate.PersonId == personId &&
                candidate.Type == type,
                cancellationToken);
            if (form is null)
                return Results.NotFound();
            if (form.CompletedDate is not null)
            {
                return Results.Conflict(new ApiErrorDto(
                    "form_attestation_changed",
                    "This form already has a live attestation. Refresh it before trying again.",
                    string.Empty));
            }

            if (!string.IsNullOrWhiteSpace(request.SupervisorOverrideReason))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["supervisorOverrideReason"] =
                        ["Form prerequisite overrides are no longer supported."]
                }, statusCode: StatusCodes.Status422UnprocessableEntity);
            }

            var cycle = person.EffectiveDate is DateTime effectiveDate
                ? FormAttestationRules.ResolveCycleForForm(
                    effectiveDate,
                    form.Type,
                    form.DueDate,
                    form.TargetEffectiveDate == default ? null : form.TargetEffectiveDate)
                : null;
            if (cycle is null)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["form"] = ["The form is not attached to a valid compliance cycle."]
                }, statusCode: StatusCodes.Status422UnprocessableEntity);
            }

            var actorKind = actor.UserId == person.UserId
                ? AttestationActorKind.CaseManager
                : AttestationActorKind.Supervisor;
            var settings = await db.Settings.AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.AgencyId == actor.AgencyId,
                    cancellationToken)
                ?? new ServerSettings { AgencyId = actor.AgencyId };
            var availableOn = form.DueDate.Date.AddDays(
                -OpenDaysBefore(form.Type, settings));
            var formFacts = await db.Forms.AsNoTracking()
                .Where(candidate => candidate.PersonId == personId)
                .Select(candidate => new FormFact(
                    candidate.Id, candidate.PersonId, candidate.Type,
                    candidate.DueDate, candidate.CompletedDate,
                    candidate.TargetEffectiveDate))
                .ToListAsync(cancellationToken);
            var cycleAmbiguity = AnnualFormCycleDisambiguationRules.Evaluate(
                personId,
                form.Type,
                form.Id,
                request.CompletedOn,
                formFacts,
                ToComplianceSchedule(settings));
            if (cycleAmbiguity is not null)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["formId"] = [cycleAmbiguity.Reason]
                }, statusCode: StatusCodes.Status422UnprocessableEntity);
            }
            ServerForm? assessment = null;
            ServerFormAttestation? impliedAssessmentAttestation = null;

            if (string.Equals(form.Type, "Reclassification", StringComparison.Ordinal))
            {
                assessment = await FindAssessmentForAnnualTargetAsync(
                    db,
                    personId,
                    form,
                    cycle.Value,
                    cancellationToken);
                if (assessment is null)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["comprehensiveAssessment"] =
                            ["The Comprehensive Assessment obligation for this annual effective date is missing. Refresh the consumer's compliance forms before attesting the Reclassification."]
                    }, statusCode: StatusCodes.Status422UnprocessableEntity);
                }

                if (assessment.CompletedDate is null)
                {
                    if (request.ComprehensiveAssessmentCompletedOn is not DateTime assessmentCompletedOn)
                    {
                        return Results.ValidationProblem(new Dictionary<string, string[]>
                        {
                            ["comprehensiveAssessmentCompletedOn"] =
                                ["Enter the actual Comprehensive Assessment completion date. A completed Reclassification implies that its Comprehensive Assessment was completed."]
                        }, statusCode: StatusCodes.Status422UnprocessableEntity);
                    }

                    var assessmentDateError = FormAttestationRules.ValidateAssessmentCompletionDate(
                        assessmentCompletedOn,
                        request.CompletedOn,
                        cycle.Value.CycleStart,
                        clock.Today,
                        assessment.DueDate.Date.AddDays(
                            -OpenDaysBefore(assessment.Type, settings)));
                    if (assessmentDateError is not null)
                    {
                        return Results.ValidationProblem(new Dictionary<string, string[]>
                        {
                            ["comprehensiveAssessmentCompletedOn"] = [assessmentDateError]
                        }, statusCode: StatusCodes.Status422UnprocessableEntity);
                    }

                    var assessmentLinkedNotes = await db.Notes.AsNoTracking()
                        .Where(note => note.PersonId == personId && note.FormId == assessment.Id &&
                            note.AgencyId == actor.AgencyId)
                        .Select(note => new { note.Id, note.EventDate, note.Status })
                        .ToListAsync(cancellationToken);
                    var safelyCancelledAssessmentNoteIds =
                        await SafelyCancelledScheduledDuplicateNoteIdsAsync(
                            db,
                            actor.AgencyId,
                            assessmentLinkedNotes.Select(note =>
                                (note.Id, (int?)note.Status)),
                            cancellationToken);
                    assessmentLinkedNotes.RemoveAll(note =>
                        !ManualAttestationNoteRules.CompetesForManualAttestation(
                            note.Status,
                            safelyCancelledAssessmentNoteIds.Contains(note.Id)));
                    if (assessmentLinkedNotes.Count > 1)
                        return Results.Conflict(new ApiErrorDto("ambiguous_assessment_note",
                            "Several notes are linked to this assessment. Have a supervisor review the exact evidence before attesting.", string.Empty));
                    int assessmentEvidenceNoteId;
                    if (assessmentLinkedNotes.Count == 1)
                    {
                        var linkedAssessment = assessmentLinkedNotes[0];
                        var hasClaimLine = await db.ClaimLines.AsNoTracking().AnyAsync(
                            line => line.NoteId == linkedAssessment.Id, cancellationToken);
                        var conflict = ManualAttestationNoteRules.Conflict(
                            assessmentCompletedOn, linkedAssessment.EventDate,
                            linkedAssessment.Status, hasClaimLine);
                        if (conflict is not null)
                            return Results.Conflict(new ApiErrorDto(
                                "assessment_note_date_conflict", conflict, string.Empty));
                        assessmentEvidenceNoteId = linkedAssessment.Id;
                    }
                    else
                    {
                        var existingUnlinkedAssessment = await db.Notes.AsNoTracking().AnyAsync(note =>
                            note.PersonId == personId && note.AgencyId == actor.AgencyId &&
                            note.FormId == null && note.FormType == (int)FormType.ComprehensiveAssessment &&
                            note.EventDate != null &&
                            note.EventDate.Value.Date >= cycle.Value.CycleStart.Date &&
                            note.EventDate.Value.Date <= cycle.Value.CycleEnd.Date,
                            cancellationToken);
                        if (existingUnlinkedAssessment)
                            return Results.Conflict(new ApiErrorDto("unlinked_assessment_note",
                                "An unlinked assessment note may already document this work. Review it before attesting; Sati will not create a duplicate by guessing.", string.Empty));
                        var assessmentDraft = new ServerNote
                        {
                            PersonId = personId,
                            AgencyId = actor.AgencyId,
                            Narrative = "Draft: document completion of Comprehensive Assessment.",
                            EventDate = assessmentCompletedOn.Date,
                            Status = NoteWorkflow.Pending,
                            NoteType = (int)NoteType.Form,
                            Activities = (int)NoteActivity.Form,
                            FormType = (int)FormType.ComprehensiveAssessment,
                            FormId = assessment.Id
                        };
                        db.Notes.Add(assessmentDraft);
                        await db.SaveChangesAsync(cancellationToken);
                        auditTrail.Record(actor, AuditActions.NoteDraftCreatedFromAttestation,
                            "Note", assessmentDraft.Id);
                        assessmentEvidenceNoteId = assessmentDraft.Id;
                    }

                    assessment.ApplyAttestation(assessmentCompletedOn);
                    impliedAssessmentAttestation = new ServerFormAttestation
                    {
                        FormId = assessment.Id,
                        Kind = "Attested",
                        CompletedOn = assessmentCompletedOn.Date,
                        ActorKind = actorKind.ToString(),
                        ActorUserId = actor.UserId,
                        RecordedAtUtc = clock.UtcNow.UtcDateTime,
                        EvidenceNoteId = assessmentCompletedOn.Date <= cycle.Value.CycleEnd.Date
                            ? assessmentEvidenceNoteId : null,
                        PrerequisiteStateJson = FormAttestationRules.NoPrerequisitesStateJson
                    };
                    db.FormAttestations.Add(impliedAssessmentAttestation);
                    formFacts = formFacts
                        .Where(fact => fact.FormId != assessment.Id)
                        .Append(new FormFact(
                            assessment.Id,
                            assessment.PersonId,
                            assessment.Type,
                            assessment.DueDate,
                            assessment.CompletedDate,
                            assessment.TargetEffectiveDate))
                        .ToList();
                }
                else if (request.ComprehensiveAssessmentCompletedOn is not null)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["comprehensiveAssessmentCompletedOn"] =
                            ["The Comprehensive Assessment already has an attestation. Do not enter a replacement date unless that attestation is revoked first."]
                    }, statusCode: StatusCodes.Status422UnprocessableEntity);
                }
            }
            else if (request.ComprehensiveAssessmentCompletedOn is not null)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["comprehensiveAssessmentCompletedOn"] =
                        ["A Comprehensive Assessment completion date can be supplied only with a Reclassification attestation."]
                }, statusCode: StatusCodes.Status422UnprocessableEntity);
            }

            var decision = FormAttestationRules.Evaluate(
                form.Type,
                request.CompletedOn,
                cycle.Value.CycleStart,
                clock.Today,
                actorKind,
                [],
                formFacts,
                targetEffectiveDate: form.TargetEffectiveDate == default
                    ? null
                    : form.TargetEffectiveDate,
                availableOn: availableOn);
            if (!decision.Accepted)
            {
                var errorKey = decision.DateError is null ? "prerequisite" : "completedOn";
                var messages = decision.DateError is not null
                    ? new[] { decision.DateError }
                    : decision.UnmetPrerequisites.Select(item => item.Message).ToArray();
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    [errorKey] = messages
                }, statusCode: StatusCodes.Status422UnprocessableEntity);
            }

            var linkedNotes = await db.Notes
                .Where(note => note.PersonId == personId && note.FormId == form.Id &&
                    note.AgencyId == actor.AgencyId)
                .ToListAsync(cancellationToken);
            var safelyCancelledDuplicateIds =
                await SafelyCancelledScheduledDuplicateNoteIdsAsync(
                    db,
                    actor.AgencyId,
                    linkedNotes.Select(note => (note.Id, (int?)note.Status)),
                    cancellationToken);
            linkedNotes.RemoveAll(note =>
                !ManualAttestationNoteRules.CompetesForManualAttestation(
                    note.Status,
                    safelyCancelledDuplicateIds.Contains(note.Id)));
            if (request.EvidenceNoteId is int explicitlySelectedId &&
                safelyCancelledDuplicateIds.Contains(explicitlySelectedId))
                return Results.Conflict(new ApiErrorDto("form_note_mismatch",
                    "The selected evidence note was retired as a duplicate. Refresh the form and use its surviving linked note.",
                    string.Empty));
            if (linkedNotes.Count > 1)
                return Results.Conflict(new ApiErrorDto("ambiguous_form_note",
                    "Several notes are linked to this form. Have a supervisor review the exact evidence before attesting.", string.Empty));
            int? resolvedEvidenceNoteId = request.EvidenceNoteId;
            if (linkedNotes.Count == 1)
            {
                var linked = linkedNotes[0];
                if (resolvedEvidenceNoteId is int citedId && citedId != linked.Id)
                    return Results.Conflict(new ApiErrorDto("form_note_mismatch",
                        "The selected evidence note differs from the note already linked to this form.", string.Empty));
                var hasClaimLine = await db.ClaimLines.AsNoTracking().AnyAsync(
                    line => line.NoteId == linked.Id, cancellationToken);
                var noteType = linked.NoteType is int typeValue
                    ? ((NoteType)typeValue).ToString()
                    : null;
                if (!string.Equals(form.Type, "Reclassification", StringComparison.Ordinal) &&
                    ManualAttestationNoteRules.CanConvertScheduledFormNote(
                        linked.Status, noteType, linked.Activities, hasClaimLine))
                {
                    if (!request.ConfirmScheduledNoteConversion)
                        return Results.Conflict(new ApiErrorDto(
                            ManualAttestationNoteRules.ScheduledConversionRequiredCode,
                            ManualAttestationNoteRules.ScheduledConversionPrompt(
                                form.Type, linked.EventDate, request.CompletedOn), string.Empty,
                            ManualAttestationNoteRules.ScheduledConversionToken(
                                linked.Id, linked.Revision, linked.EventDate,
                                request.CompletedOn)));

                    if (request.ScheduledNoteConversionToken !=
                        ManualAttestationNoteRules.ScheduledConversionToken(
                            linked.Id, linked.Revision, linked.EventDate,
                            request.CompletedOn))
                        return Results.Conflict(new ApiErrorDto("scheduled_form_note_changed",
                            ManualAttestationNoteRules.ScheduledNoteChangedMessage, string.Empty));

                    var plannedOn = linked.EventDate;
                    linked.EventDate = request.CompletedOn.Date;
                    linked.Status = NoteWorkflow.Pending;
                    linked.Revision++;
                    auditTrail.Record(actor, AuditActions.NotePlannedWorkConverted,
                        "Note", linked.Id, JsonSerializer.Serialize(new
                        {
                            plannedOn = plannedOn?.ToString("yyyy-MM-dd"),
                            actualWorkDate = request.CompletedOn.Date.ToString("yyyy-MM-dd"),
                            newStatus = "Pending"
                        }));
                    await db.SaveChangesAsync(cancellationToken);
                }
                else
                {
                    if (request.ConfirmScheduledNoteConversion)
                        return Results.Conflict(new ApiErrorDto("scheduled_form_note_changed",
                            ManualAttestationNoteRules.ScheduledNoteChangedMessage, string.Empty));
                    var conflict = ManualAttestationNoteRules.Conflict(
                        request.CompletedOn, linked.EventDate, linked.Status, hasClaimLine);
                    if (conflict is not null)
                        return Results.Conflict(new ApiErrorDto("form_note_date_conflict", conflict, string.Empty));
                }
                resolvedEvidenceNoteId = linked.Id;
            }
            else if (request.ConfirmScheduledNoteConversion)
                return Results.Conflict(new ApiErrorDto("scheduled_form_note_changed",
                    ManualAttestationNoteRules.ScheduledNoteChangedMessage, string.Empty));
            else if (resolvedEvidenceNoteId is null)
            {
                var legacyCandidateExists = await db.Notes.AsNoTracking().AnyAsync(note =>
                    note.PersonId == personId && note.AgencyId == actor.AgencyId &&
                    note.FormId == null &&
                    note.FormType == (int)Enum.Parse<FormType>(form.Type) &&
                    note.EventDate != null &&
                    note.EventDate.Value.Date >= cycle.Value.CycleStart.Date &&
                    note.EventDate.Value.Date <= cycle.Value.CycleEnd.Date,
                    cancellationToken);
                if (legacyCandidateExists)
                    return Results.Conflict(new ApiErrorDto("unlinked_form_note",
                        ManualAttestationNoteRules.UnlinkedFormNoteMessage, string.Empty));
                var draft = new ServerNote
                {
                    PersonId = personId,
                    AgencyId = actor.AgencyId,
                    Narrative = $"Draft: document completion of {form.Type}.",
                    EventDate = request.CompletedOn.Date,
                    Status = NoteWorkflow.Pending,
                    NoteType = (int)NoteType.Form,
                    Activities = (int)NoteActivity.Form,
                    FormType = (int)Enum.Parse<FormType>(form.Type),
                FormId = form.Id
                };
                db.Notes.Add(draft);
                await db.SaveChangesAsync(cancellationToken);
                auditTrail.Record(actor, AuditActions.NoteDraftCreatedFromAttestation, "Note", draft.Id);
                resolvedEvidenceNoteId = draft.Id;
            }

            // An overdue historical form may be completed after its annual cycle.
            // Keep the exact FormId on the note, while retaining the existing
            // cycle limit for evidence IDs in the attestation ledger.
            var citedEvidenceNoteId = request.CompletedOn.Date <= cycle.Value.CycleEnd.Date
                ? resolvedEvidenceNoteId : null;
            if (citedEvidenceNoteId is int evidenceNoteId)
            {
                var evidenceIsValid = await db.Notes.AsNoTracking().AnyAsync(note =>
                    note.Id == evidenceNoteId &&
                    note.PersonId == personId &&
                    note.FormType == (int)Enum.Parse<FormType>(form.Type) &&
                    (FormNoteLinkRules.IsRelease(form.Type) ||
                     (note.FormId == form.Id && note.EventDate != null &&
                      note.EventDate.Value.Date == request.CompletedOn.Date)) &&
                    note.EventDate != null &&
                    note.EventDate.Value.Date >= cycle.Value.CycleStart.Date &&
                    note.EventDate.Value.Date <= cycle.Value.CycleEnd.Date &&
                    (note.Status == (int)NoteStatus.Pending ||
                     note.Status == (int)NoteStatus.Logged ||
                     note.Status == (int)NoteStatus.Approved),
                    cancellationToken);
                if (!evidenceIsValid)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["evidenceNoteId"] = ["The cited note is not matching form evidence."]
                    }, statusCode: StatusCodes.Status422UnprocessableEntity);
                }
            }

            var recordedAtUtc = clock.UtcNow.UtcDateTime;
            var prerequisiteStateJson = assessment is null
                ? FormAttestationRules.NoPrerequisitesStateJson
                : FormAttestationRules.AssessmentPrerequisiteStateJson(assessment.Id);
            var precedingEntry = await db.FormAttestations.AsNoTracking()
                .Where(entry => entry.FormId == form.Id)
                .OrderByDescending(entry => entry.Id)
                .Select(entry => new { entry.Kind, entry.Reason })
                .FirstOrDefaultAsync(cancellationToken);
            var correctionReason = precedingEntry?.Kind == "Revoked" &&
                !string.IsNullOrWhiteSpace(precedingEntry.Reason)
                ? precedingEntry.Reason
                : "Form completion attested through the form workflow.";
            await RecordApiLinkedNoteImpactFlagsAsync(
                db, form, actor.AgencyId, null, request.CompletedOn.Date,
                correctionReason, recordedAtUtc, cancellationToken);
            form.ApplyAttestation(request.CompletedOn);
            db.FormAttestations.Add(new ServerFormAttestation
            {
                FormId = form.Id,
                Kind = "Attested",
                CompletedOn = request.CompletedOn.Date,
                ActorKind = actorKind.ToString(),
                ActorUserId = actor.UserId,
                RecordedAtUtc = recordedAtUtc,
                EvidenceNoteId = citedEvidenceNoteId,
                PrerequisiteStateJson = prerequisiteStateJson
            });
            if (impliedAssessmentAttestation is not null)
            {
                auditTrail.Record(
                    actor,
                    AuditActions.FormAttested,
                    "Form",
                    assessment!.Id,
                    JsonSerializer.Serialize(new
                    {
                        formType = assessment.Type,
                        targetEffectiveDate = assessment.TargetEffectiveDate == default
                            ? null
                            : assessment.TargetEffectiveDate.ToString("yyyy-MM-dd"),
                        completedOn = impliedAssessmentAttestation.CompletedOn!.Value.ToString("yyyy-MM-dd"),
                        actorKind = actorKind.ToString(),
                        impliedByReclassificationFormId = form.Id
                    }));
            }
            auditTrail.Record(
                actor,
                AuditActions.FormAttested,
                "Form",
                form.Id,
                JsonSerializer.Serialize(new
                {
                    formType = form.Type,
                    targetEffectiveDate = form.TargetEffectiveDate == default
                        ? null
                        : form.TargetEffectiveDate.ToString("yyyy-MM-dd"),
                    completedOn = request.CompletedOn.Date.ToString("yyyy-MM-dd"),
                    actorKind = actorKind.ToString(),
                    comprehensiveAssessmentFormId = assessment?.Id
                }));
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                await formWrite.CommitAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Results.Conflict(new ApiErrorDto(
                    "form_attestation_changed",
                    "This form's attestation changed in another session. Refresh it and try again.",
                    string.Empty));
            }
            return Results.Ok(ContractMapper.ToForm(form));
        });

        api.MapPost("/people/{personId:int}/forms/{type}/attestation/revoke", async Task<IResult> (
            int personId,
            string type,
            RevokeFormAttestationRequest request,
            ClaimsPrincipal principal,
            ApiDbContext db,
            AuditTrail auditTrail,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.Reason))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["reason"] = ["A reason is required to revoke an attestation."]
                }, statusCode: StatusCodes.Status422UnprocessableEntity);
            }

            var actor = Actor.From(principal);
            var person = await db.People.AsNoTracking()
                .SingleOrDefaultAsync(candidate =>
                    candidate.Id == personId && candidate.AgencyId == actor.AgencyId,
                    cancellationToken);
            if (person is null ||
                !await TenantAccess.CanAccessPersonAsync(db, actor, person, cancellationToken))
                return Results.NotFound();
            await using var formWrite = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
            var form = await db.Forms.SingleOrDefaultAsync(candidate =>
                candidate.Id == request.FormId && candidate.PersonId == personId && candidate.Type == type,
                cancellationToken);
            if (form is null)
                return Results.NotFound();
            if (form.CompletedDate is null)
                return Results.Ok(ContractMapper.ToForm(form));

            var actorKind = actor.UserId == person.UserId
                ? AttestationActorKind.CaseManager
                : AttestationActorKind.Supervisor;
            var previousCompletedOn = form.CompletedDate;
            await RecordApiLinkedNoteImpactFlagsAsync(
                db, form, actor.AgencyId, previousCompletedOn, null,
                request.Reason.Trim(), DateTime.UtcNow, cancellationToken);
            form.ApplyRevocation();
            db.FormAttestations.Add(new ServerFormAttestation
            {
                FormId = form.Id,
                Kind = "Revoked",
                ActorKind = actorKind.ToString(),
                ActorUserId = actor.UserId,
                RecordedAtUtc = DateTime.UtcNow,
                Reason = request.Reason.Trim()
            });
            auditTrail.Record(
                actor,
                AuditActions.FormAttestationRevoked,
                "Form",
                form.Id,
                JsonSerializer.Serialize(new { formType = form.Type, actorKind = actorKind.ToString() }));
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                await formWrite.CommitAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Results.Conflict(new ApiErrorDto(
                    "form_attestation_changed",
                    "This form's attestation changed in another session. Refresh it and try again.",
                    string.Empty));
            }
            return Results.Ok(ContractMapper.ToForm(form));
        });

        api.MapGet("/people/{personId:int}/attestations/pending", async Task<IResult> (
            int personId,
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            var person = await db.People.AsNoTracking()
                .SingleOrDefaultAsync(candidate =>
                    candidate.Id == personId && candidate.AgencyId == actor.AgencyId,
                    cancellationToken);
            if (person is null ||
                !await TenantAccess.CanAccessPersonAsync(db, actor, person, cancellationToken))
                return Results.NotFound();
            var forms = await db.Forms.AsNoTracking()
                .Where(candidate => candidate.PersonId == personId)
                .Select(candidate => new FormFact(
                    candidate.Id,
                    candidate.PersonId,
                    candidate.Type,
                    candidate.DueDate,
                    candidate.CompletedDate,
                    candidate.TargetEffectiveDate))
                .ToListAsync(cancellationToken);
            var notes = await db.Notes.AsNoTracking()
                .Where(candidate => candidate.PersonId == personId &&
                                    candidate.FormType != null &&
                                    candidate.EventDate != null &&
                                    candidate.Status != null)
                .ToListAsync(cancellationToken);
            var facts = notes.Select(note => new NoteFact(
                note.Id,
                note.PersonId,
                Enum.GetName(typeof(FormType), note.FormType!.Value) ?? string.Empty,
                note.EventDate!.Value,
                Enum.GetName(typeof(NoteStatus), note.Status!.Value) ?? string.Empty,
                note.FormId))
                .ToList();
            var pending = FormAttestationRules.PendingAttestations(
                facts, forms, person.EffectiveDate, DateTime.Today)
                .Select(item => new PendingAttestationDto(
                    item.FormId, item.PersonId, item.FormType, item.CycleStart, item.CycleEnd,
                    item.DueDate, item.EvidenceNoteId, item.EvidenceDate,
                    item.IsLegacyUnlinked))
                .ToList();
            return Results.Ok(pending);
        });

        api.MapPost("/forms/delete", async Task<IResult> (
            DeleteFormsRequest request,
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!await TenantAccess.CanAccessUserAsync(db, actor, actor.UserId, cancellationToken))
                return Results.Forbid();

            if (request.FormIds is null)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["formIds"] = ["Form IDs are required."]
                });
            }
            var ids = request.FormIds.Where(id => id > 0).Distinct()
                .Take(FormRetentionRules.MaximumRequestIds + 1).ToList();
            if (ids.Count > FormRetentionRules.MaximumRequestIds)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["formIds"] = [FormRetentionRules.RequestLimitMessage]
                });
            }
            if (ids.Count == 0)
                return Results.Ok(new CountDto(0));

            var ownedIds = await (from form in db.Forms.AsNoTracking()
                                  join person in db.People.AsNoTracking() on form.PersonId equals person.Id
                                  join owner in db.Users.AsNoTracking() on person.UserId equals owner.Id
                                  where ids.Contains(form.Id) && person.UserId == actor.UserId &&
                                        person.AgencyId == actor.AgencyId && owner.AgencyId == actor.AgencyId &&
                                        owner.Role == actor.Role && owner.Permissions == actor.Permissions
                                  select form.Id).ToListAsync(cancellationToken);
            if (ownedIds.Count != ids.Count)
                return Results.NotFound();

            // Stored due dates are billing evidence even without an attestation.
            // This compatibility endpoint never deletes or regenerates form rows.
            return Results.Conflict(new ApiErrorDto(
                FormRetentionRules.ErrorCode,
                FormRetentionRules.Message,
                string.Empty));
        });

        api.MapPut("/forms/{id:int}", async Task<IResult> (
            int id,
            UpdateFormRequest request,
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            var form = await (from f in db.Forms
                              join p in TenantAccess.OwnedPeople(db, actor) on f.PersonId equals p.Id
                              where f.Id == id
                              select f).SingleOrDefaultAsync(cancellationToken);
            if (form is null)
                return TypedResults.NotFound();

            if (request.CompletedDate is DateTime completedOn &&
                FormCompletionRules.Validate(completedOn, DateTime.Today) is string error)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["completedDate"] = [error]
                });
            }

            if (request.CompletedDate?.Date != form.CompletedDate?.Date)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["completedDate"] = ["A completion date can be changed only through an attestation or revocation."]
                });
            }
            if (request.OpenedDate?.Date != form.OpenedDate?.Date)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["openedDate"] = ["An opening date can be recorded only through the audited form-opening workflow."]
                });
            }
            return TypedResults.Ok(ContractMapper.ToForm(form));
        });

        api.MapPost("/forms/{id:int}/open", async Task<IResult> (
            int id,
            OpenFormRequest request,
            ClaimsPrincipal principal,
            ApiDbContext db,
            ApiClock clock,
            AuditTrail auditTrail,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            var form = await (from candidate in db.Forms
                              join person in TenantAccess.OwnedPeople(db, actor)
                                  on candidate.PersonId equals person.Id
                              where candidate.Id == id
                              select candidate).SingleOrDefaultAsync(cancellationToken);
            if (form is null)
                return Results.NotFound();

            if (form.OpenedDate is DateTime existing)
            {
                if (existing.Date == request.OpenedOn.Date)
                    return Results.Ok(ContractMapper.ToForm(form));

                return Results.Conflict(new ApiErrorDto(
                    "form_opening_already_recorded",
                    "This form already has an opening date. Correcting it requires an audited correction workflow.",
                    string.Empty));
            }

            var settings = await GetOrCreateSettingsAsync(
                db, actor.AgencyId, cancellationToken);
            var availableOn = form.DueDate.Date.AddDays(
                -OpenDaysBefore(form.Type, settings));
            var dateError = FormOpeningRules.Validate(
                request.OpenedOn, availableOn, clock.Today);
            if (dateError is not null)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["openedOn"] = [dateError]
                });
            }

            form.OpenedDate = request.OpenedOn.Date;
            auditTrail.Record(
                actor,
                AuditActions.FormOpened,
                "Form",
                form.Id,
                JsonSerializer.Serialize(new
                {
                    formType = form.Type,
                    targetEffectiveDate = form.TargetEffectiveDate == default
                        ? null
                        : form.TargetEffectiveDate.ToString("yyyy-MM-dd"),
                    openedOn = request.OpenedOn.Date.ToString("yyyy-MM-dd"),
                    recordedAtUtc = clock.UtcNow.UtcDateTime
                }));
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(ContractMapper.ToForm(form));
        });
    }

    private static async Task<PersonDto> LoadPersonDtoAsync(
        ApiDbContext db,
        ServerPerson person,
        CancellationToken cancellationToken)
    {
        var forms = await db.Forms.AsNoTracking()
            .Where(x => x.PersonId == person.Id)
            .ToListAsync(cancellationToken);
        var notes = await db.Notes.AsNoTracking()
            .Where(x => x.PersonId == person.Id && x.AgencyId == person.AgencyId)
            .ToListAsync(cancellationToken);
        var releases = await db.ReleaseObligations.AsNoTracking()
            .Include(item => item.Attestations)
            .Where(item => item.PersonId == person.Id && item.AgencyId == person.AgencyId)
            .ToListAsync(cancellationToken);
        return ContractMapper.ToPerson(
            person,
            forms,
            notes,
            releases.Select(item => item.ToComplianceFact()).ToArray());
    }

    private static async Task<IResult> ApplyCheckRequestWorkflowActionAsync(
        int id,
        ApplyCheckRequestWorkflowActionRequest input,
        ClaimsPrincipal principal,
        ApiDbContext db,
        AuditTrail audit,
        CancellationToken cancellationToken)
    {
        var actor = Actor.From(principal);
        var errors = CheckRequestWorkflowRules.ValidateNote(input.Action, input.Note);
        if (errors.Count > 0)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["workflow"] = [.. errors] });
        await using var transaction = await db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable, cancellationToken);
        var request = await db.CheckRequests.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (request is null) return Results.NotFound();
        var person = await db.People.AsNoTracking().SingleOrDefaultAsync(
            x => x.Id == request.PersonId, cancellationToken);
        if (person is null || person.AgencyId != actor.AgencyId) return Results.NotFound();
        var owner = await db.Users.AsNoTracking().SingleAsync(x => x.Id == person.UserId, cancellationToken);

        if (input.Action == CheckRequestWorkflowAction.Submitted)
        {
            if (!person.CaseManagerIsRepPayee)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["personId"] = ["Representative Payee must be enabled for this consumer before submitting a check request."]
                });
            if (!await TenantAccess.OwnsPersonAsync(db, actor, person.Id, cancellationToken))
                return Results.NotFound();
        }
        else if (input.Action is CheckRequestWorkflowAction.Approved or CheckRequestWorkflowAction.Returned)
        {
            if (!actor.HasSupervisorPermissions ||
                (!actor.HasAgencyWideSupervisionPermissions && owner.SupervisorId != actor.UserId))
                return Results.NotFound();
        }
        else if (!actor.HasRepresentativePayeePermissions || !person.CaseManagerIsRepPayee)
        {
            return Results.NotFound();
        }

        var existing = await db.CheckRequestWorkflowEvents
            .Where(x => x.CheckRequestId == request.Id).OrderBy(x => x.Id).ToListAsync(cancellationToken);
        var current = CheckRequestWorkflowRules.Resolve(request.PublishedAtUtc is not null,
            existing.Select(x => x.Action));
        if (!CheckRequestWorkflowRules.CanApply(current, input.Action))
            return Results.Conflict(new ApiErrorDto("invalid_check_request_workflow_state",
                $"This action cannot be applied while the check request is {CheckRequestWorkflowRules.Describe(current).ToLowerInvariant()}.",
                string.Empty));
        var now = DateTime.UtcNow;
        var workflowEvent = new ServerCheckRequestWorkflowEvent
        {
            CheckRequestId = request.Id,
            Checkpoint = CheckRequestWorkflowRules.Checkpoint(input.Action),
            Action = input.Action,
            OccurredAtUtc = now,
            ActorUserId = actor.UserId,
            ActorName = actor.DisplayName,
            Note = Normalize(input.Note)
        };
        db.CheckRequestWorkflowEvents.Add(workflowEvent);
        if (input.Action == CheckRequestWorkflowAction.Released)
        {
            db.RepresentativePayeeLedgerEntries.Add(new ServerRepresentativePayeeLedgerEntry
            {
                PersonId = person.Id,
                CheckRequestId = request.Id,
                EntryDate = now.Date,
                Kind = RepresentativePayeeLedgerEntryKind.CheckRelease,
                Amount = -request.Amount,
                Description = $"Check #{request.Id} released to {request.PayableTo}",
                RecordedAtUtc = now,
                RecordedByUserId = actor.UserId,
                RecordedByName = actor.DisplayName
            });
        }
        audit.Record(actor, CheckRequestWorkflowAuditAction(input.Action), "CheckRequest", request.Id);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Results.Conflict(new ApiErrorDto("check_request_workflow_already_completed",
                "This workflow step was already completed. Reload the queue.", string.Empty));
        }
        existing.Add(workflowEvent);
        return Results.Ok(ToCheckRequestQueueDto(request, existing));
    }

    private static async Task<IReadOnlyList<CheckRequestWorkflowQueueItemDto>> BuildCheckRequestQueueAsync(
        ApiDbContext db,
        IReadOnlyList<ServerCheckRequest> requests,
        Func<CheckRequestWorkflowStatus, bool> include,
        CancellationToken cancellationToken)
    {
        var ids = requests.Select(x => x.Id).ToList();
        var events = await db.CheckRequestWorkflowEvents.AsNoTracking()
            .Where(x => ids.Contains(x.CheckRequestId)).OrderBy(x => x.Id).ToListAsync(cancellationToken);
        return requests.Select(request => ToCheckRequestQueueDto(request,
                events.Where(x => x.CheckRequestId == request.Id).ToList()))
            .Where(item => include(item.Status))
            .OrderBy(item => item.NeededByDate ?? DateTime.MaxValue)
            .ThenBy(item => item.CheckRequestId)
            .ToList();
    }

    private static CheckRequestTemplateDto ToCheckRequestTemplateDto(
        ServerCheckRequestTemplate template) => new(
        template.Id,
        template.PersonId,
        template.Revision,
        template.IsEnabled,
        template.GenerateOn,
        template.NeededByDaysAfterRequest,
        template.PayableTo,
        template.MailingAddress,
        template.Amount,
        template.Reason,
        template.EffectiveFrom,
        template.CreatedAtUtc,
        template.UpdatedAtUtc);

    private static async Task<IReadOnlyList<GeneratedCheckRequestDraftDto>>
        LoadPendingGeneratedCheckRequestDraftsAsync(
            ApiDbContext db,
            Actor actor,
            CancellationToken cancellationToken)
    {
        var requests = await (from request in db.CheckRequests.AsNoTracking()
            join person in db.People.AsNoTracking() on request.PersonId equals person.Id
            where request.TemplateId != null && request.ScheduledForDate != null &&
                  person.UserId == actor.UserId && person.AgencyId == actor.AgencyId &&
                  !db.CheckRequestWorkflowEvents.Any(workflow =>
                      workflow.CheckRequestId == request.Id &&
                      workflow.Action == CheckRequestWorkflowAction.Submitted)
            orderby request.ScheduledForDate, request.Id
            select request).ToListAsync(cancellationToken);
        var requestIds = requests.Select(request => request.Id).ToList();
        var actions = await db.CheckRequestWorkflowEvents.AsNoTracking()
            .Where(workflow => requestIds.Contains(workflow.CheckRequestId))
            .Select(workflow => new { workflow.CheckRequestId, workflow.Action })
            .ToListAsync(cancellationToken);

        return requests.Select(request => new GeneratedCheckRequestDraftDto(
                request.Id,
                request.TemplateId!.Value,
                request.PersonId,
                request.ConsumerName,
                request.ScheduledForDate!.Value,
                request.NeededByDate,
                request.PayableTo,
                request.Amount,
                CheckRequestWorkflowRules.Resolve(
                    request.PublishedAtUtc is not null,
                    actions.Where(action => action.CheckRequestId == request.Id)
                        .Select(action => action.Action))))
            .ToList();
    }

    private static async Task<IReadOnlyList<TimeOffCheckRequestCollisionDto>>
        LoadTimeOffCheckRequestCollisionsAsync(
            ApiDbContext db,
            Actor actor,
            DateTime timeOffDate,
            DateTime today,
            CancellationToken cancellationToken)
    {
        var date = timeOffDate.Date;
        if (date < today.Date ||
            !await db.ExemptDates.AsNoTracking().AnyAsync(item =>
                item.UserId == actor.UserId && item.Date == date, cancellationToken))
            return [];

        var templates = await (from template in db.CheckRequestTemplates.AsNoTracking()
            join person in db.People.AsNoTracking() on template.PersonId equals person.Id
            where template.IsEnabled && template.GenerateOn == date.DayOfWeek &&
                  template.EffectiveFrom <= date && person.UserId == actor.UserId &&
                  person.AgencyId == actor.AgencyId && person.CaseManagerIsRepPayee
            select new { Template = template, Person = person }).ToListAsync(cancellationToken);
        var templateIds = templates.Select(item => item.Template.Id).ToList();
        var requests = templateIds.Count == 0
            ? new List<ServerCheckRequest>()
            : await db.CheckRequests.AsNoTracking()
                .Where(request => request.TemplateId != null &&
                    templateIds.Contains(request.TemplateId.Value))
                .OrderBy(request => request.ScheduledForDate).ThenBy(request => request.Id)
                .ToListAsync(cancellationToken);
        var requestIds = requests.Select(request => request.Id).ToList();
        var submittedIds = await db.CheckRequestWorkflowEvents.AsNoTracking()
            .Where(workflow => requestIds.Contains(workflow.CheckRequestId) &&
                workflow.Action == CheckRequestWorkflowAction.Submitted)
            .Select(workflow => workflow.CheckRequestId)
            .ToListAsync(cancellationToken);
        var submittedSet = submittedIds.ToHashSet();

        return templates.Select(item =>
            {
                var exact = requests.FirstOrDefault(request =>
                    request.TemplateId == item.Template.Id && request.ScheduledForDate == date);
                if (exact is not null && submittedSet.Contains(exact.Id)) return null;
                var pending = requests.FirstOrDefault(request =>
                    request.TemplateId == item.Template.Id && !submittedSet.Contains(request.Id));
                return new TimeOffCheckRequestCollisionDto(
                    item.Template.Id,
                    item.Person.Id,
                    $"{item.Person.FirstName} {item.Person.LastName}".Trim(),
                    date,
                    item.Template.PayableTo,
                    item.Template.Amount,
                    pending?.Id);
            })
            .Where(item => item is not null)
            .Cast<TimeOffCheckRequestCollisionDto>()
            .ToList();
    }

    private static CheckRequestWorkflowQueueItemDto ToCheckRequestQueueDto(
        ServerCheckRequest request,
        IReadOnlyList<ServerCheckRequestWorkflowEvent> events)
    {
        var last = events.OrderBy(x => x.Id).LastOrDefault();
        return new CheckRequestWorkflowQueueItemDto(
            request.Id, request.PersonId, request.ConsumerName, request.CaseManagerName,
            request.SupervisorName, request.PayableTo, request.Amount, request.NeededByDate,
            request.Reason, CheckRequestWorkflowRules.Resolve(request.PublishedAtUtc is not null,
                events.Select(x => x.Action)), last?.OccurredAtUtc, last?.ActorName, last?.Note);
    }

    private static RepresentativePayeeLedgerEntryDto ToLedgerDto(
        ServerRepresentativePayeeLedgerEntry entry) => new(
        entry.Id, entry.PersonId, entry.CheckRequestId, entry.EntryDate, entry.Kind,
        entry.Amount, entry.Description, entry.RecordedAtUtc, entry.RecordedByUserId,
        entry.RecordedByName);

    private static string CheckRequestWorkflowAuditAction(CheckRequestWorkflowAction action) => action switch
    {
        CheckRequestWorkflowAction.Submitted => AuditActions.CheckRequestSubmitted,
        CheckRequestWorkflowAction.Approved => AuditActions.CheckRequestApproved,
        CheckRequestWorkflowAction.Returned => AuditActions.CheckRequestReturned,
        CheckRequestWorkflowAction.Released => AuditActions.CheckRequestReleased,
        CheckRequestWorkflowAction.ReceiptAcknowledged => AuditActions.CheckRequestReceiptAcknowledged,
        _ => throw new ArgumentOutOfRangeException(nameof(action))
    };

    private static Task<ServerPerson?> LoadAuditablePersonAsync(
        ApiDbContext db,
        Actor actor,
        int personId,
        CancellationToken cancellationToken) =>
        (from person in db.People
         join owner in db.Users on person.UserId equals owner.Id
         where person.Id == personId &&
               person.AgencyId == actor.AgencyId &&
               owner.AgencyId == actor.AgencyId
         select person).SingleOrDefaultAsync(cancellationToken);

    private static Dictionary<string, string[]> ValidatePerson(
        SavePersonRequest request,
        bool requireNewForms) =>
        PersonSaveRules.Validate(request, DateTime.Today, requireNewForms);

    private static void ApplyPerson(ServerPerson person, SavePersonRequest request, int gender, int waiver)
    {
        person.FirstName = request.FirstName.Trim();
        person.LastName = request.LastName.Trim();
        person.BirthDate = request.BirthDate.Date;
        person.Gender = gender;
        person.EffectiveDate = request.EffectiveDate?.Date;
        person.Bio = request.Bio?.Trim();
        person.Waiver = waiver;
        person.MaineCareId = Normalize(request.MaineCareId);
        person.DiagnosisCode = Normalize(request.DiagnosisCode);
        person.PlaceOfService = request.PlaceOfService;
        person.EvergreenId = Normalize(request.EvergreenId);
        person.CredibleClientId = Normalize(request.CredibleClientId);
        person.OpenWithVR = request.OpenWithVR;
        person.VrCounselorName = Normalize(request.VrCounselorName);
        person.VrAssistantName = Normalize(request.VrAssistantName);
        person.HasGuardian = request.HasGuardian;
        person.GuardianName = Normalize(request.GuardianName);
        person.PhoneNumber = Normalize(request.PhoneNumber);
        person.Email = Normalize(request.Email);
        person.Address = Normalize(request.Address);
        if (request.UpdateBillingAddress)
        {
            person.BillingStreet = Normalize(request.BillingStreet);
            person.BillingCity = Normalize(request.BillingCity);
            person.BillingState = Normalize(request.BillingState)?.ToUpperInvariant();
            person.BillingZip = Normalize(request.BillingZip);
        }
        person.PrimaryCareProvider = Normalize(request.PrimaryCareProvider);
        person.HealthcareSystemName = Normalize(request.HealthcareSystemName);
        person.CaseManagerIsRepPayee = request.CaseManagerIsRepPayee;
        person.CaseManagerIsDhhsRepresentative = request.CaseManagerIsDhhsRepresentative;
        person.UsesModivcare = request.UsesModivcare;
        person.RepPayeeMonthlyIncome = request.CaseManagerIsRepPayee
            ? request.RepPayeeMonthlyIncome
            : null;
        person.RepPayeeRegularCheckRequestNeeds = request.CaseManagerIsRepPayee
            ? Normalize(request.RepPayeeRegularCheckRequestNeeds)
            : null;
        person.HasHomeSupport = request.HasHomeSupport;
        person.HasSelfDirectedHomeSupport = request.HasSelfDirectedHomeSupport;
        person.HasSharedLiving = request.HasSharedLiving;
        person.HasCommunitySupport1To1 = request.HasCommunitySupport1To1;
        person.HasCommunitySupportSelfDirected = request.HasCommunitySupportSelfDirected;
        person.HasCommunitySupportDayProgram = request.HasCommunitySupportDayProgram;
        person.DayProgramCount = request.HasCommunitySupportDayProgram ? request.DayProgramCount : 1;
        person.IsEmployed = request.IsEmployed;
        person.HasEmploymentSpecialist = request.IsEmployed && request.HasEmploymentSpecialist;
        person.HasWorkSupports = request.IsEmployed && request.HasWorkSupports;
    }

    private static List<ServerForm> BuildInitialForms(
        IReadOnlyList<SavePersonFormRequest> requestForms,
        DateTime effectiveDate,
        ServerSettings settings)
    {
        var byType = requestForms
            .Where(form => form.Id == 0)
            .ToDictionary(form => form.Type, StringComparer.Ordinal);
        var forms = new List<ServerForm>(PersonSaveRules.FormTypes.Count);
        foreach (var typeName in PersonSaveRules.FormTypes)
        {
            var requested = byType[typeName];
            // New clients send the explicit annual-cycle identity. During a rolling
            // upgrade, an older client can omit it only for the admission cycle.
            var targetEffectiveDate = requested.TargetEffectiveDate?.Date ?? effectiveDate.Date;
            forms.Add(new ServerForm
            {
                Type = typeName,
                TargetEffectiveDate = targetEffectiveDate,
                DueDate = ComplianceScheduleRules.DueDate(
                    typeName, targetEffectiveDate, ToComplianceSchedule(settings)),
                // requested.IsCompliant is deliberately not stored. Compliance is the
                // completion date; accepting a flag from the client alongside the date
                // is what let the two disagree, and the client's flag is itself
                // derived. PersonSaveRules still rejects a request whose flag and date
                // contradict each other, so a confused caller is told rather than
                // silently reinterpreted.
                CompletedDate = requested.CompletedDate?.Date,
                OpenedDate = requested.OpenedDate?.Date
            });
        }
        return forms;
    }

    private static void AddInitialFormAttestations(
        ApiDbContext db,
        AuditTrail auditTrail,
        Actor actor,
        DateTime effectiveDate,
        ServerSettings settings,
        IEnumerable<ServerForm> forms,
        IReadOnlyCollection<ServerForm> allForms)
    {
        foreach (var form in forms.Where(candidate =>
                     candidate.CompletedDate is not null && candidate.Attestations.Count == 0))
        {
            var completedOn = form.CompletedDate!.Value.Date;
            var targetEffectiveDate = form.TargetEffectiveDate == default
                ? (DateTime?)null
                : form.TargetEffectiveDate.Date;
            var cycle = FormAttestationRules.ResolveCycleForForm(
                    effectiveDate,
                    form.Type,
                    form.DueDate,
                    targetEffectiveDate)
                ?? throw new InvalidOperationException(
                    "A completed form is not attached to a valid compliance cycle.");
            var availableOn = form.DueDate.Date.AddDays(
                -OpenDaysBefore(form.Type, settings));
            var decision = FormAttestationRules.Evaluate(
                form.Type, completedOn, cycle.CycleStart, DateTime.Today,
                AttestationActorKind.CaseManager, [],
                allForms.Select(candidate => new FormFact(
                    candidate.Id, candidate.PersonId, candidate.Type,
                    candidate.DueDate, candidate.CompletedDate,
                    candidate.TargetEffectiveDate)).ToList(),
                targetEffectiveDate: targetEffectiveDate,
                availableOn: availableOn);
            if (!decision.Accepted)
            {
                throw new InvalidOperationException(decision.DateError ?? string.Join(" ",
                    decision.UnmetPrerequisites.Select(item => item.Message)));
            }
            var attestation = new ServerFormAttestation
            {
                Form = form,
                Kind = "Attested",
                CompletedOn = completedOn,
                ActorKind = AttestationActorKind.CaseManager.ToString(),
                ActorUserId = actor.UserId,
                RecordedAtUtc = DateTime.UtcNow,
                PrerequisiteStateJson = FormAttestationRules.NoPrerequisitesStateJson
            };
            form.Attestations.Add(attestation);
            db.FormAttestations.Add(attestation);
            auditTrail.Record(
                actor,
                AuditActions.FormAttested,
                "Form",
                metadataJson: JsonSerializer.Serialize(new
                {
                    formType = form.Type,
                    targetEffectiveDate = targetEffectiveDate?.ToString("yyyy-MM-dd"),
                    cycleStart = cycle.CycleStart.ToString("yyyy-MM-dd"),
                    completedOn = completedOn.ToString("yyyy-MM-dd"),
                    actorKind = AttestationActorKind.CaseManager.ToString(),
                    prerequisiteArtifactIds = Array.Empty<int>()
                }));
        }
    }

    private static async Task<ServerForm?> FindAssessmentForAnnualTargetAsync(
        ApiDbContext db,
        int personId,
        ServerForm reclassification,
        (DateTime CycleStart, DateTime CycleEnd) cycle,
        CancellationToken cancellationToken)
    {
        var candidates = await db.Forms
            .Where(candidate =>
                candidate.PersonId == personId &&
                candidate.Type == "ComprehensiveAssessment")
            .OrderByDescending(candidate => candidate.DueDate)
            .ThenByDescending(candidate => candidate.Id)
            .ToListAsync(cancellationToken);

        if (reclassification.TargetEffectiveDate != default)
        {
            var exact = candidates.FirstOrDefault(candidate =>
                candidate.TargetEffectiveDate != default &&
                candidate.TargetEffectiveDate.Date == reclassification.TargetEffectiveDate.Date);
            if (exact is not null)
                return exact;

            return candidates.FirstOrDefault(candidate =>
                candidate.TargetEffectiveDate == default &&
                candidate.DueDate.Date > cycle.CycleStart.Date &&
                candidate.DueDate.Date <= cycle.CycleEnd.Date);
        }

        return candidates.FirstOrDefault(candidate =>
            candidate.DueDate.Date > cycle.CycleStart.Date &&
            candidate.DueDate.Date <= cycle.CycleEnd.Date);
    }

    private static DateTime ComputeFormDueDate(
        int type,
        DateTime targetEffectiveDate,
        ServerSettings settings)
    {
        if (!Enum.IsDefined(typeof(FormType), type))
            throw new ArgumentOutOfRangeException(nameof(type));
        return ComplianceScheduleRules.DueDate(
            ((FormType)type).ToString(),
            targetEffectiveDate,
            ToComplianceSchedule(settings));
    }

    private static int OpenDaysBefore(string formType, ServerSettings settings) =>
        ComplianceScheduleRules.OpenDaysBefore(
            formType,
            ToComplianceSchedule(settings));

    private static ComplianceScheduleSettings ToComplianceSchedule(ServerSettings settings) => new(
        settings.ReviewOpenDaysBefore,
        settings.PcpOpenDaysBefore,
        settings.CompAssessmentOpenDaysBefore,
        settings.ReclassificationOpenDaysBefore,
        settings.SafetyPlanOpenDaysBefore,
        settings.PrivacyPracticesOpenDaysBefore,
        settings.ReleaseAgencyOpenDaysBefore,
        settings.ReleaseDhhsOpenDaysBefore,
        settings.ReleaseMedicalOpenDaysBefore,
        settings.PcpDaysBeforeAnniversary,
        settings.CompAssessmentDaysBeforeAnniversary,
        settings.ReclassificationDaysBeforeAnniversary,
        settings.SafetyPlanDaysBeforeAnniversary,
        settings.PrivacyPracticesDaysBeforeAnniversary,
        settings.ReleaseAgencyDaysBeforeAnniversary,
        settings.ReleaseDhhsDaysBeforeAnniversary,
        settings.ReleaseMedicalDaysBeforeAnniversary);

    private static Dictionary<string, string[]> ValidatePersonContact(SavePersonContactRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.FirstName) || request.FirstName.Trim().Length > 75)
            errors["firstName"] = ["First name is required and must not exceed 75 characters."];
        if (string.IsNullOrWhiteSpace(request.LastName) || request.LastName.Trim().Length > 75)
            errors["lastName"] = ["Last name is required and must not exceed 75 characters."];
        if (request.Kind is not ("Personal" or "Guardian" or "AuthorizedRepresentative" or
            "ServiceProvider" or "HealthcareProvider" or "Other"))
            errors["kind"] = ["Contact type is invalid."];
        ValidateLength(errors, "relationship", request.Relationship, 100);
        ValidateLength(errors, "organization", request.Organization, 150);
        ValidateLength(errors, "phone", request.Phone, 30);
        ValidateLength(errors, "email", request.Email, 254);
        return errors;
    }

    private static void ApplyPersonContact(ServerPersonContact contact, SavePersonContactRequest request)
    {
        contact.FirstName = request.FirstName.Trim();
        contact.LastName = request.LastName.Trim();
        contact.Kind = request.Kind;
        contact.Relationship = Normalize(request.Relationship);
        contact.Organization = Normalize(request.Organization);
        contact.Phone = Normalize(request.Phone);
        contact.Email = Normalize(request.Email);
        contact.IsEmergencyContact = request.IsEmergencyContact;
        contact.HasActiveRelease = request.HasActiveRelease;
        contact.IsActive = true;
    }

    private static void ValidateLength(
        Dictionary<string, string[]> errors,
        string field,
        string? value,
        int maximum)
    {
        if (value?.Trim().Length > maximum)
            errors[field] = [$"Value must not exceed {maximum} characters."];
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static async Task<ReviewableNote?> LoadReviewableNoteAsync(
        ApiDbContext db,
        Actor actor,
        int noteId,
        CancellationToken cancellationToken)
    {
        if (!actor.HasSupervisorPermissions)
            return null;

        // The client's own agency is checked alongside its owner's. A person row
        // carrying a different agency than the case manager who holds it must not
        // become reviewable through that case manager.
        return await (from note in db.Notes
                      join person in db.People on note.PersonId equals person.Id
                      join owner in db.Users on person.UserId equals owner.Id
                      where note.Id == noteId && owner.AgencyId == actor.AgencyId &&
                            note.AgencyId == actor.AgencyId &&
                            person.AgencyId == actor.AgencyId &&
                            (owner.Permissions & UserPermissions.CaseManagement) != 0 &&
                            (actor.HasAgencyWideSupervisionPermissions ||
                             owner.SupervisorId == actor.UserId)
                      select new ReviewableNote(note, person)).SingleOrDefaultAsync(cancellationToken);
    }

    private static async Task<IResult?> FindFormLinkProblemAsync(
        ApiDbContext db, Actor actor, SaveNoteRequest request, CancellationToken cancellationToken)
    {
        if (request.ReleaseObligationId is long releaseId)
        {
            var release = await db.ReleaseObligations.AsNoTracking()
                .Include(row => row.Attestations)
                .SingleOrDefaultAsync(row => row.Id == releaseId &&
                    row.PersonId == request.PersonId && row.AgencyId == actor.AgencyId,
                    cancellationToken);
            if (release is null || request.FormId is not null ||
                request.FormType != ReleaseNoteLinkRules.FormTypeName(release.Category))
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["releaseObligationId"] =
                        ["The selected release obligation does not match this client and release type."]
                });
            if (release.CompletedOn is DateTime attestedOn &&
                request.EventDate?.Date != attestedOn.Date)
                return Results.Conflict(new ApiErrorDto("release_note_date_conflict",
                    "This release is attested for a different date. Revoke that attestation with a reason before correcting the linked note.",
                    string.Empty));
        }
        if (request.FormId is not int formId)
            return null;

        var matches = await db.Forms.AsNoTracking().AnyAsync(form =>
            form.Id == formId && form.PersonId == request.PersonId &&
            form.Type == request.FormType, cancellationToken);
        return matches ? null : Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["formId"] = ["The selected form obligation does not match this client and form type."]
        });
    }

    private static async Task<IResult?> FindNoteSubmissionProblemAsync(
        ApiDbContext db, Actor actor, SaveNoteRequest request, DateTime today,
        CancellationToken cancellationToken, int noteId = 0,
        AuditTrail? auditTrail = null)
    {
        ContractMapper.TryParseNoteStatus(request.Status, out var status);
        if (status != NoteWorkflow.Logged) return null;

        // Ownership has already been checked. Read the target consumer's current
        // persisted evidence, never the client's displayed forms or FormType tag.
        // The decision is the note's service date under the policy version in force
        // on that date, evaluated by the same rule billing and approval use.
        var person = await TenantAccess.OwnedPeople(db, actor).AsNoTracking()
            .SingleAsync(x => x.Id == request.PersonId, cancellationToken);
        var forms = await db.Forms.AsNoTracking()
            .Include(form => form.Attestations)
            .Where(x => x.PersonId == person.Id)
            .ToListAsync(cancellationToken);
        var releaseRows = (await LoadReleaseBillingRowsByPersonAsync(
                db, [person.Id], cancellationToken))
            .GetValueOrDefault(person.Id) ?? [];
        var providerLinks = (await LoadReleaseProviderLinksByPersonAsync(
                db, actor.AgencyId, [person.Id], cancellationToken))
            .GetValueOrDefault(person.Id) ?? [];
        var policy = await LoadBillingCompliancePolicyContextAsync(
            db, actor.AgencyId, cancellationToken);
        await PopulateContactHistoryAsync(db, actor.AgencyId, [person], cancellationToken);
        // The note being logged stands in for its stored copy, so a visit counts toward
        // its own service date and a note changed away from a contact stops counting.
        ContractMapper.TryParseNoteType(request.NoteType, out var noteType);
        var compliance = EvaluateNoteCompliance(
            new ServerNote
            {
                Id = noteId,
                PersonId = person.Id,
                EventDate = request.EventDate,
                NoteType = noteType,
                Activities = request.Activities,
                Status = status
            },
            person, forms, releaseRows, policy, providerLinks);
        var reasons = compliance.Reasons.ToList();
        AnnualFormCycleAmbiguity? ambiguity = null;
        if (FormNoteAttestationRules.AttestsExactFormOnLog(
                request.Status,
                request.Activities,
                request.NoteType,
                request.FormType,
                request.FormId))
        {
            ambiguity = AnnualFormCycleDisambiguationRules.Evaluate(
                request.PersonId,
                request.FormType,
                request.FormId,
                request.EventDate,
                forms.Select(form => new FormFact(
                    form.Id,
                    form.PersonId,
                    form.Type,
                    form.DueDate,
                    form.CompletedDate,
                    form.TargetEffectiveDate == default
                        ? null
                        : form.TargetEffectiveDate)).ToArray(),
                policy.Schedule);
            if (ambiguity is not null)
                reasons.Add(ambiguity.Reason);
        }
        reasons = reasons.Distinct(StringComparer.Ordinal).ToList();
        if (reasons.Count == 0) return null;

        var configurationInvalid = request.EventDate is DateTime serviceDate &&
            !BillingComplianceGate.IsSupported(policy.Resolve(serviceDate));
        var result = new NoteSubmissionResult(
            false, reasons, !configurationInvalid, configurationInvalid);

        // Clinical review may still proceed with a written justification; billing
        // stays blocked until a supervisor exception or an administrative recovery.
        if (!NoteSubmissionGate.IsSubmissionAllowed(result, request.CaseManagerJustification))
            return Results.Conflict(new ApiErrorDto(
                NoteSubmissionGate.RefusalCode, result.Message, string.Empty));

        if (ambiguity is not null && auditTrail is not null && noteId > 0)
        {
            auditTrail.Record(
                actor,
                AuditActions.NoteOlderCycleJustified,
                "Note",
                noteId,
                JsonSerializer.Serialize(new
                {
                    selectedFormId = ambiguity.SelectedFormId,
                    selectedTargetEffectiveDate = ambiguity.SelectedTargetEffectiveDate
                        .ToString("yyyy-MM-dd"),
                    renewalFormId = ambiguity.RenewalFormId,
                    renewalTargetEffectiveDate = ambiguity.RenewalTargetEffectiveDate
                        .ToString("yyyy-MM-dd"),
                    activityDate = request.EventDate?.Date.ToString("yyyy-MM-dd")
                }));
            await db.SaveChangesAsync(cancellationToken);
        }
        return null;
    }

    private static BillingComplianceResult EvaluateNoteCompliance(
        ServerNote note,
        ServerPerson person,
        IReadOnlyList<ServerForm> forms,
        IReadOnlyList<ReleaseObligation> releaseObligations,
        ServerBillingCompliancePolicyContext policy,
        IReadOnlyList<ReleaseProviderLinkFact>? providerLinks = null)
    {
        if (note.EventDate is not DateTime serviceDate)
            return new BillingComplianceResult(true, [], []);

        var releaseFacts = ExpectedBillingComplianceObligations.IncludeMissingReleases(
            person.EffectiveDate,
            releaseObligations.Select(item => item.ToComplianceFact()),
            serviceDate,
            providerLinks ?? []);
        var reconciledReleaseCycles = releaseFacts
            .Where(item => item.TargetEffectiveDate is not null)
            .Select(item => item.TargetEffectiveDate!.Value.Date)
            .ToHashSet();
        var formSnapshots = forms
            .Where(form => !IsLegacyReleaseFormType(form.Type) ||
                           !reconciledReleaseCycles.Contains(
                               (form.TargetEffectiveDate == default
                                   ? form.DueDate
                                   : form.TargetEffectiveDate).Date))
            .Select(form => new ComplianceFormSnapshot(
                form.Type,
                form.DueDate,
                form.CompletedDate,
                form.OpenedDate,
                $"form:{form.Id}",
                TargetEffectiveDate: form.TargetEffectiveDate == default
                    ? null
                    : form.TargetEffectiveDate));
        var withExpectedForms = ExpectedBillingComplianceObligations.IncludeMissingForms(
            person.EffectiveDate,
            formSnapshots,
            serviceDate,
            policy.Schedule);
        var snapshots = BillingComplianceGate.IncludeOpeningObligations(
                withExpectedForms)
            .Concat(ReleaseBillingRules.BuildComplianceSnapshots(
                releaseFacts,
                serviceDate))
            .Concat(MonthlyContactRules.BuildObligations(
                person.EffectiveDate,
                ContactHistoryFor(person, note)));
        return BillingComplianceGate.EvaluateBillingWindowDetailed(
                snapshots,
                serviceDate,
                policy.Resolve(serviceDate));
    }

    private static bool IsLegacyReleaseFormType(string type) => type is
        "Release_Agency" or "Release_DHHS" or "Release_Medical";

    private static IReadOnlyList<string> ValidateBillingCandidate(
        ServerNote note,
        ServerPerson person,
        ServerAgency? agency,
        IReadOnlyList<ServerForm> forms,
        IReadOnlyList<ReleaseObligation> releaseObligations,
        ServerBillingCompliancePolicyContext policy,
        IReadOnlyList<Sati.Contracts.V1.BillingComplianceRecoveryDecision>? recoveryDecisions = null,
        IReadOnlyList<ReleaseProviderLinkFact>? providerLinks = null)
    {
        var errors = ValidateNonComplianceBillingCandidate(note, person, agency).ToList();
        // The note documenting a form must satisfy its own due date. This check
        // is outside ordinary compliance recovery and cannot be waived by it.
        errors.AddRange(EvaluateFormWorkBilling(note, forms));
        errors.AddRange(EvaluateBillingComplianceRelease(
            note, person, forms, releaseObligations, policy, recoveryDecisions, providerLinks));
        return errors;
    }

    private static BillingComplianceResult EvaluateSupervisorNoteCompliance(
        ServerNote note,
        ServerPerson person,
        IReadOnlyList<ServerForm> forms,
        IReadOnlyList<ReleaseObligation> releases,
        ServerBillingCompliancePolicyContext policy,
        IReadOnlyList<ReleaseProviderLinkFact> providerLinks)
    {
        var historical = EvaluateNoteCompliance(
            note, person, forms, releases, policy, providerLinks);
        var formWorkReasons = EvaluateFormWorkBilling(note, forms);
        if (formWorkReasons.Count == 0)
            return historical;
        return new BillingComplianceResult(
            false,
            historical.Reasons.Concat(formWorkReasons).Distinct(StringComparer.Ordinal).ToArray(),
            historical.Blockers);
    }

    private static IReadOnlyList<string> EvaluateFormWorkBilling(
        ServerNote note,
        IReadOnlyList<ServerForm> forms)
    {
        if (!NoteActivityRules.Has(note.Activities, ContractMapper.NoteTypeName(note.NoteType), NoteActivity.Form))
            return [];

        var formType = note.FormType is int type
            ? ContractMapper.FormTypeName(type)
            : string.Empty;
        if (FormWorkBillingRules.IsRelease(formType))
            return [];

        var linkedForm = forms.FirstOrDefault(form => form.Id == note.FormId);
        var decision = FormWorkBillingRules.Evaluate(
            new FormWorkNoteFact(note.PersonId, formType, note.EventDate, note.FormId),
            linkedForm is null
                ? null
                : new FormWorkObligationFact(
                    linkedForm.Id, linkedForm.PersonId, linkedForm.Type,
                    linkedForm.DueDate, linkedForm.CompletedDate));
        return decision.Reasons;
    }

    /// <summary>
    /// The one compliance decision for releasing a note to billing: the service-date
    /// policy, minus an exact-obligation Supervisor exception or an Admin recovery
    /// whose frozen evidence still matches. Claim creation and 837P release share it
    /// so a released note cannot be re-blocked by a second, weaker rule.
    /// </summary>
    private static IReadOnlyList<string> EvaluateBillingComplianceRelease(
        ServerNote note,
        ServerPerson person,
        IReadOnlyList<ServerForm> forms,
        IReadOnlyList<ReleaseObligation> releaseObligations,
        ServerBillingCompliancePolicyContext policy,
        IReadOnlyList<Sati.Contracts.V1.BillingComplianceRecoveryDecision>? recoveryDecisions = null,
        IReadOnlyList<ReleaseProviderLinkFact>? providerLinks = null)
    {
        var errors = new List<string>();
        var compliance = EvaluateNoteCompliance(
            note, person, forms, releaseObligations, policy, providerLinks);
        if (compliance.Passed)
        {
            // No exception is needed for this service date.
        }
        else if (IsReleasedByRecovery(
                     note,
                     person,
                     forms,
                     releaseObligations,
                     policy,
                     recoveryDecisions ?? [],
                     providerLinks ?? []))
        {
            // This exact note and blocker evidence were released by an immutable
            // Admin recovery decision after compliance was restored.
        }
        else if (note.ComplianceOverride)
        {
            IReadOnlyList<string> selectedIds;
            try
            {
                selectedIds = string.IsNullOrWhiteSpace(note.OverrideObligationIdsJson)
                    ? []
                    : JsonSerializer.Deserialize<string[]>(note.OverrideObligationIdsJson) ?? [];
            }
            catch (JsonException)
            {
                selectedIds = [];
            }

            var exception = BillingComplianceExceptionRules.Validate(
                compliance.Blockers ?? [],
                selectedIds,
                note.OverrideReason,
                note.OverrideAttestationConfirmed &&
                note.OverrideApprovedById is not null &&
                note.OverrideApprovedAt is not null);
            errors.AddRange(exception.Errors.Select(error => $"Compliance exception: {error}"));
            if (exception.Accepted)
            {
                errors.AddRange(BillingComplianceExceptionRules.RemainingBlockers(
                        compliance.Blockers ?? [],
                        exception.SelectedObligationIds)
                    .Select(blocker =>
                        $"{blocker.Name} was due {blocker.DueDate:MMM d, yyyy} " +
                        "and was not completed as of this service date."));
            }
        }
        else
        {
            errors.AddRange(compliance.Reasons);
        }
        return errors;
    }

    private static async Task<IReadOnlyList<string>> RevalidateDraftPeriodComplianceAsync(
        ApiDbContext db,
        int agencyId,
        ServerBillingPeriod period,
        CancellationToken cancellationToken)
    {
        var noteIds = period.Lines.Select(line => line.NoteId).Distinct().ToArray();
        var rows = await (from note in db.Notes.AsNoTracking()
                          join person in db.People.AsNoTracking() on note.PersonId equals person.Id
                          join owner in db.Users.AsNoTracking() on person.UserId equals owner.Id
                          where noteIds.Contains(note.Id) &&
                                note.AgencyId == agencyId &&
                                person.AgencyId == agencyId &&
                                owner.AgencyId == agencyId
                          select new ReviewableNote(note, person))
            .ToListAsync(cancellationToken);
        if (rows.Count != noteIds.Length)
            return ["A draft claim line no longer has an accessible source note."];

        var agency = await db.Agencies.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == agencyId, cancellationToken);
        var personIds = rows.Select(row => row.Person.Id).Distinct().ToArray();
        var formsByPerson = (await db.Forms.AsNoTracking()
                .Include(form => form.Attestations)
                .Where(form => personIds.Contains(form.PersonId))
                .ToListAsync(cancellationToken))
            .GroupBy(form => form.PersonId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<ServerForm>)group.ToList());
        var releasesByPerson = await LoadReleaseBillingRowsByPersonAsync(
            db, personIds, cancellationToken);
        var providerLinksByPerson = await LoadReleaseProviderLinksByPersonAsync(
            db, agencyId, personIds, cancellationToken);
        await PopulateContactHistoryAsync(
            db, agencyId, rows.Select(row => row.Person), cancellationToken);
        var policy = await LoadBillingCompliancePolicyContextAsync(
            db, agencyId, cancellationToken);
        var recoveryByNote = await LoadRecoveryDecisionsByNoteAsync(
            db, agencyId, noteIds, cancellationToken);
        var rowsByNoteId = rows.ToDictionary(row => row.Note.Id);
        var errors = new List<string>();

        foreach (var line in period.Lines)
        {
            if (!rowsByNoteId.TryGetValue(line.NoteId, out var row))
                continue;
            if (row.Note.EventDate?.Date != line.DateOfService.Date)
            {
                errors.Add($"Draft claim line {line.Id} no longer matches its source note's service date.");
                continue;
            }

            var validation = ValidateBillingCandidate(
                row.Note,
                row.Person,
                agency,
                formsByPerson.GetValueOrDefault(row.Person.Id) ?? [],
                releasesByPerson.GetValueOrDefault(row.Person.Id) ?? [],
                policy,
                recoveryByNote.GetValueOrDefault(row.Note.Id) ?? [],
                providerLinksByPerson.GetValueOrDefault(row.Person.Id) ?? []);
            errors.AddRange(validation.Select(error =>
                $"Draft claim line {line.Id} is no longer eligible for submission: {error}"));
        }

        return errors;
    }

    private static IReadOnlyList<string> ValidateNonComplianceBillingCandidate(
        ServerNote note,
        ServerPerson person,
        ServerAgency? agency)
    {
        var errors = new List<string>();
        if (note.Status != 6)
            errors.Add("Service note is not approved.");
        if (note.EventDate is null)
            errors.Add("No service date.");
        if (BillingRules.CalculateSection13Units(note.Minutes) < 1)
            errors.Add("Units must be at least 1.");
        if (string.IsNullOrWhiteSpace(person.MaineCareId))
            errors.Add("Consumer has no MaineCare ID.");
        if (!BillingRules.IsValidDiagnosisCode(person.DiagnosisCode))
            errors.Add("Consumer diagnosis code is missing or invalid.");
        if (person.PlaceOfService is null)
            errors.Add("Consumer has no place of service.");
        if (!HasValidSubscriberClaimIdentity(person))
            errors.Add("Consumer claim name, birth date, or structured claim address is incomplete or invalid.");
        if (!BillingRules.IsValidNpi(agency?.Npi))
            errors.Add("Agency NPI is missing or invalid.");
        if (agency is not null)
            errors.AddRange(ValidateBillingConfiguration(agency));
        return errors;
    }

    private static bool IsReleasedByRecovery(
        ServerNote note,
        ServerPerson person,
        IReadOnlyList<ServerForm> forms,
        IReadOnlyList<ReleaseObligation> releaseObligations,
        ServerBillingCompliancePolicyContext policy,
        IReadOnlyList<Sati.Contracts.V1.BillingComplianceRecoveryDecision> decisions,
        IReadOnlyList<ReleaseProviderLinkFact>? providerLinks = null)
    {
        if (note.EventDate is not DateTime serviceDate || decisions.Count == 0)
            return false;

        var noteSnapshot = new BillingRecoveryNoteSnapshot(
            note.Id, person.Id, serviceDate, IsSubmittedOrBilled: false);
        var obligations = BuildRecoveryObligations(
            person.Id,
            person.EffectiveDate,
            forms,
            releaseObligations,
            policy.Schedule,
            serviceDate,
            providerLinks ?? [],
            ContactHistoryFor(person, note));
        var version = policy.ResolveSnapshot(serviceDate);
        return decisions.Any(decision => BillingComplianceRecoveryRules.IsReleased(
            noteSnapshot, obligations, version, decision));
    }

    private static IReadOnlyList<BillingComplianceObligationSnapshot> BuildRecoveryObligations(
        int personId,
        DateTime? initialEffectiveDate,
        IReadOnlyList<ServerForm> forms,
        IReadOnlyList<ReleaseObligation> releaseObligations,
        ComplianceScheduleSettings schedule,
        DateTime asOfDate,
        IReadOnlyList<ReleaseProviderLinkFact>? providerLinks,
        IReadOnlyList<ContactFact>? contacts)
    {
        var releaseFacts = ExpectedBillingComplianceObligations.IncludeMissingReleases(
            initialEffectiveDate,
            releaseObligations.Select(item => item.ToComplianceFact()),
            asOfDate,
            providerLinks ?? []);
        var reconciledReleaseCycles = releaseFacts
            .Where(item => item.TargetEffectiveDate is not null)
            .Select(item => item.TargetEffectiveDate!.Value.Date)
            .ToHashSet();
        var formSnapshots = forms
            .Where(form => !IsLegacyReleaseFormType(form.Type) ||
                           !reconciledReleaseCycles.Contains(
                               (form.TargetEffectiveDate == default
                                   ? form.DueDate
                                   : form.TargetEffectiveDate).Date))
            .Select(form =>
            {
                var attestation = form.CompletedDate is DateTime completedOn
                    ? form.Attestations
                        .Where(item => string.Equals(
                                           item.Kind,
                                           FormAttestationKind.Attested.ToString(),
                                           StringComparison.Ordinal) &&
                                       item.CompletedOn?.Date == completedOn.Date)
                        .OrderBy(item => item.RecordedAtUtc)
                        .ThenBy(item => item.Id)
                        .FirstOrDefault()
                    : null;
                var completionEvidence = attestation is { Id: > 0 }
                    ? $"form-attestation:{attestation.Id}"
                    : form.CompletedDate is DateTime completed
                        ? $"form-completion:{form.Id}:{completed:yyyy-MM-dd}"
                        : null;
                var openingEvidence = form.OpenedDate is DateTime opened
                    ? $"form-opened:{form.Id}:{opened:yyyy-MM-dd}"
                    : null;
                return new ComplianceFormSnapshot(
                    form.Type,
                    form.DueDate,
                    form.CompletedDate,
                    form.OpenedDate,
                    $"form:{form.Id}",
                    completionEvidence,
                    openingEvidence,
                    form.TargetEffectiveDate == default
                        ? null
                        : form.TargetEffectiveDate);
            });
        var withExpectedForms = ExpectedBillingComplianceObligations.IncludeMissingForms(
            initialEffectiveDate,
            formSnapshots,
            asOfDate,
            schedule);
        var formsAndOpening = BillingComplianceGate.IncludeOpeningObligations(
            withExpectedForms);
        return BillingComplianceRecoveryRules.FromComplianceSnapshots(
                personId,
                formsAndOpening.Concat(MonthlyContactRules.BuildObligations(
                    initialEffectiveDate, contacts)))
            .Concat(ReleaseBillingRules.BuildRecoveryObligations(
                personId,
                releaseFacts))
            .ToArray();
    }

    private static async Task<IReadOnlyDictionary<int, IReadOnlyList<Sati.Contracts.V1.BillingComplianceRecoveryDecision>>>
        LoadRecoveryDecisionsByNoteAsync(
            ApiDbContext db,
            int agencyId,
            IEnumerable<int> noteIds,
            CancellationToken cancellationToken)
    {
        var ids = noteIds.Distinct().ToArray();
        if (ids.Length == 0)
            return new Dictionary<int, IReadOnlyList<Sati.Contracts.V1.BillingComplianceRecoveryDecision>>();

        var rows = await db.BillingComplianceRecoveryDecisions.AsNoTracking()
            .Include(item => item.Obligations)
            .Include(item => item.Notes)
            .Where(item => item.AgencyId == agencyId &&
                           item.Notes.Any(note => ids.Contains(note.NoteId)))
            .ToListAsync(cancellationToken);
        return ids.ToDictionary(
            noteId => noteId,
            noteId => (IReadOnlyList<Sati.Contracts.V1.BillingComplianceRecoveryDecision>)rows
                .Where(item => item.Notes.Any(note => note.NoteId == noteId))
                .Select(item => item.ToContract())
                .ToArray());
    }

    private static async Task<ServerRecoveryInputs?> LoadRecoveryInputsAsync(
        ApiDbContext db,
        int agencyId,
        int personId,
        CancellationToken cancellationToken)
    {
        var person = await (from candidate in db.People.AsNoTracking()
                            join owner in db.Users.AsNoTracking()
                                on candidate.UserId equals owner.Id
                            where candidate.Id == personId &&
                                  candidate.AgencyId == agencyId &&
                                  owner.AgencyId == agencyId
                            select candidate)
            .SingleOrDefaultAsync(cancellationToken);
        if (person is null)
            return null;

        var agency = await db.Agencies.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == agencyId, cancellationToken);
        var forms = await db.Forms.AsNoTracking()
            .Include(form => form.Attestations)
            .Where(form => form.PersonId == personId)
            .ToListAsync(cancellationToken);
        var releases = (await LoadReleaseBillingRowsByPersonAsync(
                db, [personId], cancellationToken))
            .GetValueOrDefault(personId) ?? [];
        var notes = await db.Notes.AsNoTracking()
            .Where(note => note.PersonId == personId &&
                           note.AgencyId == agencyId &&
                           note.Status == 6 &&
                           !db.ClaimLines.Any(line => line.NoteId == note.Id))
            .OrderBy(note => note.EventDate)
            .ThenBy(note => note.Id)
            .ToListAsync(cancellationToken);
        var policy = await LoadBillingCompliancePolicyContextAsync(
            db, agencyId, cancellationToken);
        var providerLinks = (await LoadReleaseProviderLinksByPersonAsync(
                db, agencyId, [personId], cancellationToken))
            .GetValueOrDefault(personId) ?? [];
        await PopulateContactHistoryAsync(db, agencyId, [person], cancellationToken);
        var decisionsByNote = await LoadRecoveryDecisionsByNoteAsync(
            db, agencyId, notes.Select(note => note.Id), cancellationToken);
        notes = notes.Where(note => !IsReleasedByRecovery(
                note,
                person,
                forms,
                releases,
                policy,
                decisionsByNote.GetValueOrDefault(note.Id) ?? [],
                providerLinks))
            .ToList();
        return new ServerRecoveryInputs(
            person, agency, forms, releases, providerLinks, notes, policy);
    }

    private static BillingComplianceRecoveryPlan PrepareRecoveryPlan(
        ServerRecoveryInputs inputs,
        DateTime agencyToday)
    {
        var notes = inputs.Notes
            .Where(note => ValidateNonComplianceBillingCandidate(
                note, inputs.Person, inputs.Agency).Count == 0)
            .Select(note => new BillingRecoveryNoteSnapshot(
                note.Id,
                inputs.Person.Id,
                note.EventDate!.Value.Date,
                IsSubmittedOrBilled: false))
            .ToArray();
        return BillingComplianceRecoveryRules.Prepare(
            inputs.Policy.AgencyId,
            inputs.Person.Id,
            inputs.Policy.RecoveryVersions,
            BuildRecoveryObligations(
                inputs.Person.Id,
                inputs.Person.EffectiveDate,
                inputs.Forms,
                inputs.ReleaseObligations,
                inputs.Policy.Schedule,
                agencyToday,
                inputs.ProviderLinks,
                inputs.Person.ContactFactsForCompliance),
            notes,
            agencyToday);
    }

    private static IReadOnlyList<string> ValidateBillingConfiguration(ServerAgency agency)
    {
        var errors = new List<string>();
        if (!BillingRules.IsValidProcedureCode(agency.BillingProcedureCode))
            errors.Add("Agency billing procedure code is missing or invalid.");
        if (!BillingRules.IsValidModifier(agency.BillingModifier))
            errors.Add("Agency billing modifier is invalid.");
        if (agency.BillingUnitRate is null or <= 0)
            errors.Add("Agency billing unit rate is missing or invalid.");
        if (!BillingRules.IsSafeX12Element(agency.EdiSubmitterId, 15))
            errors.Add("EDI submitter ID is missing or invalid.");
        if (!BillingRules.IsSafeX12Element(agency.EdiPayerName, 60) ||
            !BillingRules.IsSafeX12Element(agency.EdiPayerId, 80))
            errors.Add("EDI payer name or payer ID is missing or invalid.");
        if (!BillingRules.IsSafeX12Element(agency.EdiContactName, 60) ||
            agency.EdiContactPhone is null || agency.EdiContactPhone.Length is < 10 or > 15 ||
            agency.EdiContactPhone.Any(character => !char.IsDigit(character)))
            errors.Add("EDI contact name or telephone number is missing or invalid.");
        if (!BillingRules.IsSafeX12Element(agency.Name, 60) ||
            !BillingRules.IsValidNpi(agency.Npi) ||
            !BillingRules.IsSafeX12Element(agency.TaxId, 50) ||
            !BillingRules.IsSafeX12Element(agency.Street, 55) ||
            !BillingRules.IsSafeX12Element(agency.City, 30) ||
            !BillingRules.IsSafeX12Element(agency.State, 2) ||
            !BillingRules.IsSafeX12Element(agency.Zip, 15))
            errors.Add("Agency billing provider name, NPI, tax ID, or structured address is incomplete or invalid.");
        return errors;
    }

    private static bool HasValidSubscriberClaimIdentity(ServerPerson person) =>
        BillingRules.IsSafeX12Element(person.FirstName, 35) &&
        BillingRules.IsSafeX12Element(person.LastName, 60) &&
        person.BirthDate >= new DateTime(1900, 1, 1) &&
        BillingRules.IsSafeX12Element(person.BillingStreet, 55) &&
        BillingRules.IsSafeX12Element(person.BillingCity, 30) &&
        BillingRules.IsSafeX12Element(person.BillingState, 2) &&
        BillingRules.IsSafeX12Element(person.BillingZip, 15);

    private static ProfessionalClaimSnapshot CreateClaimSnapshot(ServerPerson person, ServerAgency agency) => new(
        ProfessionalClaimSnapshotCodec.CurrentVersion,
        agency.Id,
        person.Id,
        person.FirstName!,
        person.LastName!,
        person.BirthDate.Date,
        person.Gender == 1 ? "M" : person.Gender == 2 ? "F" : "U",
        person.MaineCareId!,
        person.BillingStreet!,
        person.BillingCity!,
        person.BillingState!,
        person.BillingZip!,
        agency.Name,
        agency.Npi!,
        agency.TaxId!,
        agency.Street!,
        agency.City!,
        agency.State!,
        agency.Zip!,
        agency.EdiSubmitterId!,
        agency.EdiContactName!,
        agency.EdiContactPhone!,
        agency.EdiPayerName!,
        agency.EdiPayerId!);

    private static DateTime? CurrentCycleAnchor(DateTime? effectiveDate, DateTime today)
    {
        if (effectiveDate is null || effectiveDate.Value.Date > today.Date) return null;
        var start = effectiveDate.Value.Date;
        var years = today.Year - start.Year;
        if (today.Date < start.AddYears(years)) years--;
        return start.AddYears(years);
    }

    private static IEnumerable<(int Quarter, string Category, int SlotIndex)> RequiredReviewItems(ServerPerson person)
    {
        for (var quarter = 1; quarter <= 4; quarter++)
        {
            yield return (quarter, "Medical", 0);
            yield return (quarter, "Dental", 0);
            yield return (quarter, "GoalReview", 0);
            if (person.IsEmployed && !person.HasEmploymentSpecialist && !person.HasWorkSupports && !person.OpenWithVR)
                yield return (quarter, "Employment", 0);
        }
        var arrangements = new List<(string Category, int SlotIndex)>();
        if (person.HasHomeSupport) arrangements.Add(("NoteReviewHome", arrangements.Count(x => x.Category == "NoteReviewHome")));
        if (person.HasSharedLiving) arrangements.Add(("NoteReviewHome", arrangements.Count(x => x.Category == "NoteReviewHome")));
        if (person.HasCommunitySupport1To1) arrangements.Add(("NoteReviewCommunity", arrangements.Count(x => x.Category == "NoteReviewCommunity")));
        if (person.HasCommunitySupportDayProgram)
            for (var i = 0; i < Math.Max(0, person.DayProgramCount); i++)
                arrangements.Add(("NoteReviewCommunity", arrangements.Count(x => x.Category == "NoteReviewCommunity")));
        if (arrangements.Count == 0) yield break;
        for (var quarter = 1; quarter <= 4; quarter++)
        {
            var arrangement = arrangements[(quarter - 1) % arrangements.Count];
            yield return (quarter, arrangement.Category, arrangement.SlotIndex);
        }
    }

    private static async Task<ServerReviewItem?> LoadAccessibleReviewAsync(
        ApiDbContext db, Actor actor, int reviewItemId, CancellationToken cancellationToken)
    {
        var item = await db.ReviewItems.Include(x => x.Appointment).SingleOrDefaultAsync(x => x.Id == reviewItemId, cancellationToken);
        if (item is null) return null;
        var person = await db.People.AsNoTracking().SingleOrDefaultAsync(
            x => x.Id == item.PersonId, cancellationToken);
        return person is not null &&
               await TenantAccess.CanAccessPersonAsync(db, actor, person, cancellationToken)
            ? item
            : null;
    }

    private static Task<ServerAppointment?> LatestAppointmentAsync(
        ApiDbContext db, int personId, string category, CancellationToken cancellationToken) =>
        (from appointment in db.Appointments.AsNoTracking()
         join review in db.ReviewItems on appointment.ReviewItemId equals review.Id
         where review.PersonId == personId && review.Category == category
         orderby appointment.Date descending
         select appointment).FirstOrDefaultAsync(cancellationToken);

    private static int CalculateUnits(int? minutes) =>
        minutes.HasValue ? Math.Max(1, (int)Math.Ceiling(minutes.Value / 15.0)) : 0;

    private static IResult StaleAssessmentConflict() =>
        Results.Conflict(new ApiErrorDto(
            "stale_assessment",
            "This assessment was changed after you opened it. Reload it before saving or submitting.",
            string.Empty));

    // A bulk import folder is expected to hold 300-400 consumers, and the client chunks its
    // lookups. The cap is a runaway guard on a request whose body is a caller-supplied list.
    private const int CredibleMatchLookupLimit = 500;

    private static IResult StalePersonConflict() =>
        Results.Conflict(new ApiErrorDto(
            "stale_person",
            "This person record was changed after you opened it. Reload it before saving.",
            string.Empty));

    private static IResult StaleTestConsumerConflict() =>
        Results.Conflict(new ApiErrorDto(
            "stale_test_consumer",
            "This consumer changed after you selected them. Refresh the Admin dashboard and review the current record before trying again.",
            string.Empty));

    private static LegalHoldDto ToLegalHoldDto(ServerLegalHold hold) => new(
        hold.Id, hold.PersonId, hold.Reason, hold.CaseReference, hold.IssuedBy,
        hold.EffectiveAtUtc, hold.PlacedByUserId, hold.PlacedAtUtc,
        hold.IsReleased, hold.ReleasedByUserId, hold.ReleasedAtUtc, hold.ReleaseNote);

    private static IResult StaleNoteConflict() =>
        Results.Conflict(new ApiErrorDto(
            "stale_note",
            "This note changed after it was opened. Reload the saved copy before applying your changes.",
            string.Empty));

    /// <summary>
    /// The shape both AT request list routes project to. A named type rather than
    /// an anonymous one so the two routes can share the row-building step below
    /// instead of each re-expressing the total.
    /// </summary>
    private sealed class AtRequestRow
    {
        public int Id { get; init; }
        public string? ClientName { get; init; }
        public string Status { get; init; } = string.Empty;
        public decimal SalesTax { get; init; }
        public DateTime? SubmittedDate { get; init; }
        public string? VendorName { get; init; }
        public string? CaseManagerName { get; init; }
        public decimal? PassthroughRate { get; init; }
        public string? SignedByName { get; init; }
        public DateTime? SignedAtUtc { get; init; }
        public bool HasSnapshot { get; init; }
    }

    /// <summary>
    /// Attaches item totals and produces the list DTOs.
    ///
    /// MIRRORED MATH — the canonical definition is ATRequestCalculator.Total. It
    /// is re-expressed here because the sum is done in SQL without loading item
    /// rows. A request's FROZEN rate wins over the agency's current one, so a
    /// published row keeps reporting the total it was filed at.
    /// </summary>
    private static async Task<List<AtRequestListItemDto>> BuildAtRequestRowsAsync(
        ApiDbContext db, List<AtRequestRow> requests, decimal currentRate, CancellationToken cancellationToken)
    {
        var requestIds = requests.Select(x => x.Id).ToList();
        var totals = await db.AtRequestItems.AsNoTracking()
            .Where(x => requestIds.Contains(x.ATRequestId))
            .GroupBy(x => x.ATRequestId)
            .Select(x => new { RequestId = x.Key, Total = x.Sum(i => i.ItemCost * i.Quantity) })
            .ToDictionaryAsync(x => x.RequestId, x => x.Total, cancellationToken);

        return [.. requests.Select(request => new AtRequestListItemDto(
            request.Id, request.ClientName, request.Status,
            (totals.GetValueOrDefault(request.Id) + request.SalesTax)
                * (1 + (request.PassthroughRate ?? currentRate)),
            request.SubmittedDate, request.VendorName, request.CaseManagerName, request.HasSnapshot,
            request.SignedByName, request.SignedAtUtc))];
    }

    private static IResult StaleAtRequestConflict() =>
        Results.Conflict(new ApiErrorDto(
            "stale_at_request",
            "This AT request changed after it was opened. Reload the saved request before applying your changes.",
            string.Empty));

    private static IResult StaleCheckRequestTemplateConflict() =>
        Results.Conflict(new ApiErrorDto(
            "stale_check_request_template",
            "This weekly check-request default changed after it was opened. Reload it before trying again.",
            string.Empty));

    // Distinct code from stale_at_request: the client cannot fix this by
    // reloading and retrying, which is exactly what a stale conflict invites.
    private static IResult PublishedAtRequestConflict() =>
        Results.Conflict(new ApiErrorDto(
            "published_at_request",
            "This AT request has been published and can no longer be edited. Reopen it for correction first; reopening removes the attestation.",
            string.Empty));

    // Publication state is read through the shared rule owner rather than tested
    // field by field, so the API and the desktop agree on what counts as signed.
    private static bool IsAtRequestPublished(ServerAtRequest request) =>
        AtRequestPublication.IsPublished(request.SignedByName, request.SignedAtUtc);

    private static IResult StaleSettingsConflict() =>
        Results.Conflict(new ApiErrorDto(
            "stale_settings",
            "Agency settings changed after this window was opened. Review the latest settings before trying again.",
            string.Empty));

    private static IResult StaleScratchpadConflict() =>
        Results.Conflict(new ApiErrorDto(
            "stale_scratchpad",
            "Your scratchpad changed in another Sati session. Reload the saved copy before trying again.",
            string.Empty));

    private static IResult DuplicateClaimLineConflict() =>
        Results.Conflict(new ApiErrorDto(
            "claim_line_exists",
            "This service note already has a billing claim line.",
            string.Empty));

    private static IResult? EdiReadinessConflict(ServerBillingPeriod period)
    {
        if (period.Lines.Any(line => line.Units <= 0 || line.ChargeAmount <= 0))
        {
            return Results.Conflict(new ApiErrorDto(
                "billing_claim_amount_invalid",
                "This billing period contains one or more claims with zero units or a $0 charge. Correct and rebuild those claims before submitting or generating an 837P file.",
                string.Empty));
        }

        if (period.Lines.Any(line => string.IsNullOrWhiteSpace(line.ClaimSnapshotJson)))
        {
            return Results.Conflict(new ApiErrorDto(
                "billing_snapshot_missing",
                "This older billing period is missing the frozen claim details required for an 837P file. It cannot be generated safely; the affected claims must be rebuilt into a new billing period.",
                string.Empty));
        }

        try
        {
            ServerEdiGenerator.ValidatePeriod(period);
            return null;
        }
        catch (InvalidOperationException)
        {
            return Results.Conflict(new ApiErrorDto(
                "billing_period_not_edi_ready",
                "This billing period contains incomplete or invalid frozen claim details and cannot produce an 837P file. Review and rebuild the affected claims before submitting it.",
                string.Empty));
        }
    }

    private static bool IsDuplicateClaimLine(DbUpdateException exception) =>
        exception.InnerException is SqlException sqlException &&
        sqlException.Number is 2601 or 2627 &&
        sqlException.Message.Contains("IX_ClaimLines_NoteId", StringComparison.Ordinal);

    private static IResult ReplayEdiOrConflict(
        ServerEdiGeneration generation,
        int billingPeriodId,
        bool isTest) =>
        generation.BillingPeriodId == billingPeriodId && generation.IsTest == isTest
            ? Results.Ok(new EdiFileDto(generation.FileName, generation.Content))
            : Results.Conflict(new ApiErrorDto(
                "idempotency_key_reused",
                "This retry key was already used for a different EDI request.",
                string.Empty));

    private static string CreateEdiControlNumber(string normalizedKey) =>
        (Convert.ToUInt32(normalizedKey[..8], 16) % 1_000_000_000)
        .ToString("D9", System.Globalization.CultureInfo.InvariantCulture);

    private static bool IsDuplicateEdiGeneration(DbUpdateException exception) =>
        exception.InnerException is SqlException sqlException &&
        sqlException.Number is 2601 or 2627 &&
        sqlException.Message.Contains("IX_EdiGenerations_AgencyId_ActorUserId_IdempotencyKey", StringComparison.Ordinal)
        || exception.InnerException?.Message.Contains(
            "EdiGenerations.AgencyId, EdiGenerations.ActorUserId, EdiGenerations.IdempotencyKey",
            StringComparison.Ordinal) == true;

    // Format and escaping are owned by Sati.Contracts AuditCsv, shared with the
    // desktop's local export. Do not hand-build this file here.
    private static string BuildAuditCsv(
        IReadOnlyList<AuditExportRow> rows,
        string reason,
        DateTime exportedAtUtc) =>
        AuditCsv.Build(
            rows.Select(row => new AuditCsvRow(
                row.EventId,
                row.OccurredAtUtc,
                row.ActorUserId,
                row.ActorDisplayName,
                row.Action,
                row.ResourceType,
                row.ResourceId,
                row.CorrelationId)),
            reason,
            exportedAtUtc);

    private sealed record AuditExportRow(
        Guid EventId,
        DateTime OccurredAtUtc,
        int ActorUserId,
        string ActorDisplayName,
        string Action,
        string ResourceType,
        string? ResourceId,
        string CorrelationId);

    private sealed record BillingLossPersonRow(
        int Id,
        string? FirstName,
        string? LastName,
        DateTime? EffectiveDate);

    private sealed record BillingLossFormRow(
        int Id,
        int PersonId,
        string Type,
        DateTime DueDate,
        DateTime? CompletedDate,
        DateTime? OpenedDate,
        DateTime TargetEffectiveDate);

    private sealed record BillingLossNoteRow(int PersonId, DateTime EventDate, int? Minutes);

    private sealed record ReviewableNote(ServerNote Note, ServerPerson Person);

    /// <summary>
    /// The billing queue needs claim identity and compliance facts, never clinical
    /// narrative, journal, visit detail, or encrypted identity material. Keeping the
    /// scalar projection explicit prevents those potentially unbounded or sensitive
    /// columns from becoming part of the Azure SQL result merely because the rule
    /// evaluator works with server persistence shapes.
    /// </summary>
    internal static IQueryable<BillingCandidateBaseRow> BillingCandidateBaseQuery(
        ApiDbContext db,
        int agencyId) =>
        from note in db.Notes.AsNoTracking()
        join person in db.People.AsNoTracking() on note.PersonId equals person.Id
        join owner in db.Users.AsNoTracking() on person.UserId equals owner.Id
        where note.Status == 6 &&
              owner.AgencyId == agencyId &&
              person.AgencyId == agencyId &&
              note.AgencyId == agencyId &&
              !db.ClaimLines.Any(line => line.NoteId == note.Id)
        orderby note.EventDate
        select new BillingCandidateBaseRow(
            note.Id,
            note.EventDate,
            note.Status,
            note.Minutes,
            note.PersonId,
            note.FormType,
            note.FormId,
            note.ReleaseObligationId,
            note.NoteType,
            note.Activities,
            note.AgencyId,
            note.ComplianceOverride,
            note.OverrideReason,
            note.OverrideApprovedById,
            note.OverrideApprovedAt,
            note.OverrideAttestationConfirmed,
            note.OverrideObligationIdsJson,
            person.UserId,
            person.FirstName,
            person.LastName,
            person.BirthDate,
            person.EffectiveDate,
            person.AgencyId,
            person.MaineCareId,
            person.DiagnosisCode,
            person.PlaceOfService,
            person.BillingStreet,
            person.BillingCity,
            person.BillingState,
            person.BillingZip);

    private static ReviewableNote ToReviewableNote(BillingCandidateBaseRow row) => new(
        new ServerNote
        {
            Id = row.NoteId,
            EventDate = row.EventDate,
            Status = row.Status,
            Minutes = row.Minutes,
            PersonId = row.PersonId,
            FormType = row.FormType,
            FormId = row.FormId,
            ReleaseObligationId = row.ReleaseObligationId,
            NoteType = row.NoteType,
            Activities = row.Activities,
            AgencyId = row.NoteAgencyId,
            ComplianceOverride = row.ComplianceOverride,
            OverrideReason = row.OverrideReason,
            OverrideApprovedById = row.OverrideApprovedById,
            OverrideApprovedAt = row.OverrideApprovedAt,
            OverrideAttestationConfirmed = row.OverrideAttestationConfirmed,
            OverrideObligationIdsJson = row.OverrideObligationIdsJson
        },
        new ServerPerson
        {
            Id = row.PersonId,
            UserId = row.PersonOwnerUserId,
            FirstName = row.FirstName,
            LastName = row.LastName,
            BirthDate = row.BirthDate,
            EffectiveDate = row.EffectiveDate,
            AgencyId = row.PersonAgencyId,
            MaineCareId = row.MaineCareId,
            DiagnosisCode = row.DiagnosisCode,
            PlaceOfService = row.PlaceOfService,
            BillingStreet = row.BillingStreet,
            BillingCity = row.BillingCity,
            BillingState = row.BillingState,
            BillingZip = row.BillingZip
        });

    internal sealed record BillingCandidateBaseRow(
        int NoteId,
        DateTime? EventDate,
        int? Status,
        int? Minutes,
        int PersonId,
        int? FormType,
        int? FormId,
        long? ReleaseObligationId,
        int? NoteType,
        int? Activities,
        int? NoteAgencyId,
        bool ComplianceOverride,
        string? OverrideReason,
        int? OverrideApprovedById,
        DateTime? OverrideApprovedAt,
        bool OverrideAttestationConfirmed,
        string? OverrideObligationIdsJson,
        int PersonOwnerUserId,
        string FirstName,
        string LastName,
        DateTime BirthDate,
        DateTime? EffectiveDate,
        int? PersonAgencyId,
        string? MaineCareId,
        string? DiagnosisCode,
        int? PlaceOfService,
        string? BillingStreet,
        string? BillingCity,
        string? BillingState,
        string? BillingZip);

    private sealed record ServerRecoveryInputs(
        ServerPerson Person,
        ServerAgency? Agency,
        IReadOnlyList<ServerForm> Forms,
        IReadOnlyList<ReleaseObligation> ReleaseObligations,
        IReadOnlyList<ReleaseProviderLinkFact> ProviderLinks,
        IReadOnlyList<ServerNote> Notes,
        ServerBillingCompliancePolicyContext Policy);

    private sealed record ServerBillingCompliancePolicyContext(
        int AgencyId,
        BillingComplianceRequirements FallbackRequirements,
        int PcpOpenDaysBefore,
        ComplianceScheduleSettings Schedule,
        IReadOnlyList<BillingCompliancePolicyVersionSnapshot> Versions)
    {
        public BillingCompliancePolicyVersionSnapshot ResolveSnapshot(DateTime serviceDate) =>
            BillingCompliancePolicyRules.ResolveForServiceDate(
                Versions,
                AgencyId,
                serviceDate) ?? new BillingCompliancePolicyVersionSnapshot(
                    long.MinValue, AgencyId, DateTime.MinValue, FallbackRequirements);

        public BillingComplianceRequirements Resolve(DateTime serviceDate) =>
            ResolveSnapshot(serviceDate).Requirements;

        public IReadOnlyList<BillingCompliancePolicyVersionSnapshot> RecoveryVersions =>
            [new BillingCompliancePolicyVersionSnapshot(
                long.MinValue, AgencyId, DateTime.MinValue, FallbackRequirements), .. Versions];
    }

    internal static IQueryable<CaseloadNoteSummaryRow> CaseloadNoteSummaries(
        ApiDbContext db,
        IReadOnlyCollection<int> personIds,
        int agencyId,
        DateTime businessDate)
    {
        var today = businessDate.Date;
        var lookahead = today.AddDays(30);
        return db.Notes.AsNoTracking()
            .Where(note => personIds.Contains(note.PersonId) &&
                           note.AgencyId == agencyId &&
                           note.Status == (int)NoteStatus.Scheduled &&
                           note.EventDate >= today &&
                           note.EventDate <= lookahead)
            .Select(note => new CaseloadNoteSummaryRow(
                note.Id,
                note.PersonId,
                note.Status,
                note.EventDate,
                note.NoteType,
                note.Activities,
                note.FormType,
                note.ReleaseObligationId));
    }

    private static async Task<ServerSettings> GetOrCreateSettingsAsync(
        ApiDbContext db,
        int agencyId,
        CancellationToken cancellationToken)
    {
        var settings = await db.Settings.SingleOrDefaultAsync(
            x => x.AgencyId == agencyId,
            cancellationToken);
        if (settings is not null)
            return settings;

        settings = new ServerSettings { AgencyId = agencyId };
        db.Settings.Add(settings);
        await db.SaveChangesAsync(cancellationToken);
        return settings;
    }

    internal static async Task<BillingComplianceRequirements> ResolveBillingComplianceRequirementsAsync(
        ApiDbContext db,
        int agencyId,
        DateTime serviceDate,
        BillingComplianceRequirements fallbackRequirements,
        CancellationToken cancellationToken)
    {
        var rows = await db.BillingCompliancePolicyVersions.AsNoTracking()
            .Where(version => version.AgencyId == agencyId &&
                              version.EffectiveOn <= serviceDate.Date)
            .ToListAsync(cancellationToken);
        return BillingCompliancePolicyRules.ResolveForServiceDate(
                rows.Select(version => version.ToSnapshot()), agencyId, serviceDate)
            ?.Requirements ?? fallbackRequirements;
    }

    private static async Task<ServerBillingCompliancePolicyContext> LoadBillingCompliancePolicyContextAsync(
        ApiDbContext db,
        int agencyId,
        CancellationToken cancellationToken)
    {
        var settings = await GetOrCreateSettingsAsync(db, agencyId, cancellationToken);
        var versions = await db.BillingCompliancePolicyVersions.AsNoTracking()
            .Where(version => version.AgencyId == agencyId)
            .OrderBy(version => version.EffectiveOn)
            .ThenBy(version => version.Id)
            .ToListAsync(cancellationToken);
        return new ServerBillingCompliancePolicyContext(
            agencyId,
            settings.BillingComplianceRequirements,
            settings.PcpOpenDaysBefore,
            ToComplianceSchedule(settings),
            versions.Select(version => version.ToSnapshot()).ToList());
    }

    private static async Task<ServerBillingPolicyImpactEvaluation>
        BuildBillingCompliancePolicyImpactPreviewAsync(
            ApiDbContext db,
            int agencyId,
            DateTime effectiveOn,
            BillingComplianceRequirements proposedRequirements,
            CancellationToken cancellationToken)
    {
        // Preview is deliberately read-only. Do not use GetOrCreateSettingsAsync
        // here: an inspection must never initialize or otherwise mutate agency data.
        var settings = await db.Settings.AsNoTracking()
            .SingleOrDefaultAsync(item => item.AgencyId == agencyId, cancellationToken);
        var policyRows = await db.BillingCompliancePolicyVersions.AsNoTracking()
            .Where(version => version.AgencyId == agencyId)
            .OrderBy(version => version.EffectiveOn)
            .ThenBy(version => version.Id)
            .ToListAsync(cancellationToken);
        var policy = new ServerBillingCompliancePolicyContext(
            agencyId,
            settings?.BillingComplianceRequirements ??
                BillingComplianceGate.DefaultRequirements,
            settings?.PcpOpenDaysBefore ?? 90,
            settings is null
                ? new ComplianceScheduleSettings()
                : ToComplianceSchedule(settings),
            policyRows.Select(version => version.ToSnapshot()).ToArray());
        var enforcementDate = effectiveOn.Date;
        var nextPolicyDate = policy.Versions
            .Where(version => version.EffectiveOn.Date > enforcementDate)
            .Select(version => (DateTime?)version.EffectiveOn.Date)
            .Min();
        var relevantStatuses = new[]
        {
            NoteWorkflow.HeldForCompliance,
            NoteWorkflow.Pending,
            NoteWorkflow.Logged,
            NoteWorkflow.Approved,
            NoteWorkflow.ComplianceBlocked
        };

        var notes = await (from note in db.Notes.AsNoTracking()
                           join person in db.People.AsNoTracking()
                               on note.PersonId equals person.Id
                           join owner in db.Users.AsNoTracking()
                               on person.UserId equals owner.Id
                           where note.EventDate != null &&
                                 note.EventDate.Value >= enforcementDate &&
                                 (nextPolicyDate == null ||
                                  note.EventDate.Value < nextPolicyDate.Value) &&
                                 note.AgencyId == agencyId &&
                                 person.AgencyId == agencyId &&
                                 owner.AgencyId == agencyId &&
                                 person.Status != (int)PersonStatus.Ghost &&
                                 (relevantStatuses.Contains(note.Status!.Value) ||
                                  db.ClaimLines.Any(line => line.NoteId == note.Id))
                           select new
                           {
                               note.Id,
                               note.PersonId,
                               person.EffectiveDate,
                               ServiceDate = note.EventDate!.Value,
                               note.Status
                           })
            .ToListAsync(cancellationToken);
        var noteIds = notes.Select(note => note.Id).ToArray();
        var personIds = notes.Select(note => note.PersonId).Distinct().ToArray();
        var formsByPerson = (await db.Forms.AsNoTracking()
                .Include(form => form.Attestations)
                .Where(form => personIds.Contains(form.PersonId))
                .ToListAsync(cancellationToken))
            .GroupBy(form => form.PersonId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<ServerForm>)group.ToList());
        var releasesByPerson = await LoadReleaseBillingRowsByPersonAsync(
            db, personIds, cancellationToken);
        var providerLinksByPerson = await LoadReleaseProviderLinksByPersonAsync(
            db, agencyId, personIds, cancellationToken);
        var contactsByPerson = await LoadContactFactsByPersonAsync(
            db, agencyId, personIds, cancellationToken);
        var claims = await (from line in db.ClaimLines.AsNoTracking()
                            join period in db.BillingPeriods.AsNoTracking()
                                on line.BillingPeriodId equals period.Id
                            join owner in db.Users.AsNoTracking()
                                on period.UserId equals owner.Id
                            where noteIds.Contains(line.NoteId) &&
                                  owner.AgencyId == agencyId
                            select new
                            {
                                line.Id,
                                line.NoteId,
                                IsFinalized = period.SubmittedAt != null || period.Status != 0
                            })
            .ToListAsync(cancellationToken);
        var claimsByNote = claims.ToLookup(claim => claim.NoteId);
        var facts = notes.Select(note =>
        {
            var noteClaims = claimsByNote[note.Id].ToArray();
            var finalizedCount = noteClaims.Count(claim => claim.IsFinalized);
            return new BillingCompliancePolicyImpactRecordSnapshot(
                note.Id,
                note.PersonId,
                note.ServiceDate.Date,
                note.Status is NoteWorkflow.Logged or NoteWorkflow.Approved ||
                    finalizedCount > 0,
                noteClaims.Length - finalizedCount,
                finalizedCount,
                BuildRecoveryObligations(
                    note.PersonId,
                    note.EffectiveDate,
                    formsByPerson.GetValueOrDefault(note.PersonId) ?? [],
                    releasesByPerson.GetValueOrDefault(note.PersonId) ?? [],
                    policy.Schedule,
                    note.ServiceDate.Date,
                    providerLinksByPerson.GetValueOrDefault(note.PersonId) ?? [],
                    contactsByPerson[note.PersonId].ToArray()),
                noteClaims.Where(claim => claim.IsFinalized)
                    .Select(claim => claim.Id)
                    .ToArray());
        }).ToArray();

        var impacts = BillingCompliancePolicyImpactRules.Analyze(
            agencyId,
            policy.FallbackRequirements,
            policy.Versions,
            enforcementDate,
            proposedRequirements,
            facts);
        var preview = BillingCompliancePolicyImpactRules.Preview(
            agencyId,
            policy.FallbackRequirements,
            policy.Versions,
            enforcementDate,
            proposedRequirements,
            facts);
        return new ServerBillingPolicyImpactEvaluation(preview, impacts);
    }

    private static async Task RecalculateUnsubmittedNoteStatesAsync(
        ApiDbContext db,
        IEnumerable<BillingCompliancePolicyRecordImpact> impacts,
        CancellationToken cancellationToken)
    {
        var changes = impacts
            .Where(impact => !impact.IsSubmittedOrFinalized &&
                             impact.ChangeKind is
                                 BillingCompliancePolicyImpactChangeKind.NewlyBlocked or
                                 BillingCompliancePolicyImpactChangeKind.NewlyUnblocked)
            .ToDictionary(impact => impact.NoteId, impact => impact.ChangeKind);
        if (changes.Count == 0)
            return;

        var noteIds = changes.Keys.ToArray();
        var notes = await db.Notes
            .Where(note => noteIds.Contains(note.Id))
            .ToListAsync(cancellationToken);
        foreach (var note in notes)
        {
            note.Status = changes[note.Id] switch
            {
                BillingCompliancePolicyImpactChangeKind.NewlyBlocked
                    when note.Status == NoteWorkflow.Pending => NoteWorkflow.ComplianceBlocked,
                BillingCompliancePolicyImpactChangeKind.NewlyUnblocked
                    when note.Status == NoteWorkflow.ComplianceBlocked => NoteWorkflow.Pending,
                _ => note.Status
            };
        }
    }

    private static List<BillingCompliancePolicyReviewFlag>
        CreateBillingCompliancePolicyReviewFlags(
            BillingCompliancePolicyVersion version,
            IEnumerable<BillingCompliancePolicyRecordImpact> impacts,
            DateTime createdAtUtc)
    {
        var flags = new List<BillingCompliancePolicyReviewFlag>();
        foreach (var impact in impacts.Where(item => item.IsSubmittedOrFinalized))
        {
            flags.Add(BillingCompliancePolicyReviewFlag.ForNote(
                version, impact, createdAtUtc));
            flags.AddRange(impact.SubmittedOrFinalizedClaimRecordIds.Select(
                claimLineId => BillingCompliancePolicyReviewFlag.ForClaimLine(
                    version, impact, claimLineId, createdAtUtc)));
        }

        return flags;
    }

    private sealed record ServerBillingPolicyImpactEvaluation(
        BillingCompliancePolicyImpactPreviewDto Preview,
        IReadOnlyList<BillingCompliancePolicyRecordImpact> Impacts);
    private static async Task<bool> IsComprehensiveAssessmentAuthoringEnabledAsync(
        ApiDbContext db,
        int agencyId,
        CancellationToken cancellationToken) =>
        await db.Settings.AsNoTracking()
            .Where(settings => settings.AgencyId == agencyId)
            .Select(settings => (bool?)settings.IsComprehensiveAssessmentAuthoringEnabled)
            .SingleOrDefaultAsync(cancellationToken) ?? false;

    private static async Task<bool> IsPersonCenteredPlanAuthoringEnabledAsync(
        ApiDbContext db,
        int agencyId,
        CancellationToken cancellationToken) =>
        await db.Settings.AsNoTracking()
            .Where(settings => settings.AgencyId == agencyId)
            .Select(settings => (bool?)settings.IsPersonCenteredPlanAuthoringEnabled)
            .SingleOrDefaultAsync(cancellationToken) ?? false;

    private sealed record DayNoteRow(ServerNote Note, ServerPerson Person);

    private static async Task<List<DayNoteRow>> LoadDayNotesAsync(
        ApiDbContext db, int userId, int agencyId, DateTime date, CancellationToken cancellationToken)
    {
        var dayStart = date.Date;
        var dayEnd = dayStart.AddDays(1);
        return await (from note in db.Notes.AsNoTracking()
                      join person in db.People.AsNoTracking() on note.PersonId equals person.Id
                      where person.UserId == userId &&
                            person.AgencyId == agencyId && note.AgencyId == agencyId &&
                            note.EventDate >= dayStart && note.EventDate < dayEnd
                      select new DayNoteRow(note, person))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Server-side enforcement of the service-day rule. The desktop client checks
    /// the same <see cref="ServiceTimeline"/> rule for immediate feedback, but a
    /// rule that decides what may be billed cannot be enforced by a client, so
    /// this runs on every create and update regardless of what the client sent.
    /// Returns null when the request claims no time or claims only free time.
    /// </summary>
    private static Task<IResult?> FindReviewServiceTimeProblemAsync(
        ApiDbContext db, ReviewableNote row, int agencyId, CancellationToken cancellationToken) =>
        FindServiceTimeProblemAsync(db, row.Person.UserId, agencyId,
            new SaveNoteRequest(row.Note.Narrative, row.Note.EventDate, "Logged", row.Note.Minutes,
                row.Note.StartTime, row.Person.Id, null, null, null, null), row.Note.Id, cancellationToken);

    private static Task<IResult?> FindServiceTimeProblemAsync(
        ApiDbContext db,
        Actor actor,
        SaveNoteRequest request,
        int? editingNoteId,
        CancellationToken cancellationToken) =>
        FindServiceTimeProblemAsync(db, actor.UserId, actor.AgencyId, request, editingNoteId, cancellationToken);

    private static async Task<IResult?> FindServiceTimeProblemAsync(
        ApiDbContext db, int caseManagerId, int agencyId, SaveNoteRequest request,
        int? editingNoteId, CancellationToken cancellationToken)
    {
        var candidate = ServiceTimeline.TryCreateBlock(
            editingNoteId ?? 0, request.StartTime, request.Minutes, request.Status);
        if (candidate is null)
            return null;

        var windowProblem = ServiceTimeline.DescribeWindowViolation(candidate.StartMinutes, candidate.Minutes);
        if (windowProblem is not null)
            return Results.Conflict(new ApiErrorDto("service_time_window", windowProblem, string.Empty));

        if (request.EventDate is not DateTime eventDate)
            return null;

        var sameDay = await LoadDayNotesAsync(db, caseManagerId, agencyId, eventDate, cancellationToken);
        var blocks = sameDay
            .Select(row => ServiceTimeline.TryCreateBlock(
                row.Note.Id,
                row.Note.StartTime,
                row.Note.Minutes,
                ContractMapper.NoteStatusName(row.Note.Status),
                DescribeNoteOwner(row.Person)))
            .OfType<ServiceBlock>();

        var conflicts = ServiceTimeline.FindConflicts(candidate, blocks);
        if (conflicts.Count == 0)
            return null;

        return Results.Conflict(new ApiErrorDto(
            "service_time_overlap",
            "This service time overlaps time already recorded on this date. " +
            string.Join(" ", conflicts.Select(conflict => conflict.Reason)),
            string.Empty));
    }

    private static string DescribeNoteOwner(ServerPerson person)
    {
        var name = $"{person.FirstName} {person.LastName}".Trim();
        return string.IsNullOrWhiteSpace(name) ? "another note" : $"a note for {name}";
    }

    private static Dictionary<string, string[]>? ValidateNote(SaveNoteRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.Narrative is null || request.Narrative.Length > 1_000_000)
            errors["narrative"] = ["Narrative is required and must not exceed 1,000,000 characters."];
        if (request.PersonId <= 0)
            errors["personId"] = ["A valid person is required."];
        if (string.Equals(request.NoteType, NoteSchedulingPolicy.ReminderType, StringComparison.Ordinal) &&
            request.EventDate is null)
            errors["eventDate"] = ["A calendar reminder requires a date."];
        if (request.Minutes is < 0 or > 1_440)
            errors["minutes"] = ["Minutes must be between 0 and 1,440."];
        if (request.StartTime is int start && (start < 0 || start > ServiceTimeline.WindowLengthMinutes))
            errors["startTime"] = ["Service start time must fall inside the 7:00 AM to 7:00 PM logging window."];
        if (!ContractMapper.TryParseNoteStatus(request.Status, out _))
            errors["status"] = ["The note status is invalid."];
        else if (!ContractMapper.TryParseNoteStatus(request.Status, out var status) ||
                 !NoteWorkflow.IsCaseManagerWritableStatus(status))
            errors["status"] = ["That note status is controlled by a server workflow."];
        if (!ContractMapper.TryParseFormType(request.FormType, out _))
            errors["formType"] = ["The form type is invalid."];
        if (!ContractMapper.TryParseNoteType(request.NoteType, out _))
            errors["noteType"] = ["The note type is invalid."];
        var activityError = NoteActivityRules.Validate(request.Activities, request.NoteType);
        if (activityError is not null)
            errors["activities"] = [activityError];
        var formLinkError = FormNoteLinkRules.Validate(
            request.NoteType, request.FormType, request.Status, request.FormId,
            request.FormDateCorrectionReason, request.Activities);
        if (formLinkError is not null)
            errors["formId"] = [formLinkError];
        if (!ContractMapper.TryParseGoalProgress(request.GoalProgress, out _))
            errors["goalProgress"] = ["Goal progress must be None, Minimal, Moderate, or Substantial."];
        else if (string.Equals(request.Status, "Logged", StringComparison.Ordinal) &&
                 request.GoalProgress is null)
            errors["goalProgress"] = ["Goal progress is required before a note can be submitted for review."];
        return errors.Count == 0 ? null : errors;
    }

    private static async Task<Dictionary<string, string[]>> ValidateUserRequestAsync(
        ApiDbContext db, Actor actor, SaveUserRequest request, int? currentUserId,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        var username = request.Username?.Trim() ?? string.Empty;
        var displayName = request.DisplayName?.Trim() ?? string.Empty;
        if (username.Length is < 1 or > 50) errors["username"] = ["Username is required and must not exceed 50 characters."];
        if (displayName.Length is < 1 or > 150) errors["displayName"] = ["Display name is required and must not exceed 150 characters."];
        if (request.Email?.Length > 254) errors["email"] = ["Email must not exceed 254 characters."];
        if (request.Phone?.Length > 30) errors["phone"] = ["Phone must not exceed 30 characters."];
        // Empty/unsupported set, foreign agency, and the non-administrator scope all live in
        // Sati.Contracts.V1 so the desktop-local UserService enforces the identical rule
        // rather than a second hand-written copy of it.
        if (UserManagementRules.DescribeGrantRefusal(
                actor.ToAgencyActor(), request.Permissions, request.SupervisorId, request.AgencyId)
            is { } refusal)
            errors[refusal.Field] = [refusal.Message];
        if (!string.IsNullOrWhiteSpace(username) && await db.Users.AsNoTracking().AnyAsync(
                x => x.Username == username && x.Id != currentUserId, cancellationToken))
            errors["username"] = ["A user with that username already exists."];
        if (request.SupervisorId.HasValue && !await db.Users.AsNoTracking().AnyAsync(
                x => x.Id == request.SupervisorId && x.AgencyId == actor.AgencyId &&
                     (x.Permissions & UserPermissions.Supervision) != 0, cancellationToken))
            errors["supervisorId"] = ["The selected supervisor is invalid."];
        return errors;
    }

    private static bool ValidPassword(string? password) => password?.Length is >= 8 and <= 128;

    private static Dictionary<string, string[]>? ValidateIncidentReport(IncidentReportRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (!IsSafeIncidentToken(request.Reference, 6, 40))
            errors["reference"] = ["Reference must contain 6-40 letters, numbers, hyphens, or underscores."];
        if (request.Source is not ("Desktop" or "Api"))
            errors["source"] = ["Source must be Desktop or Api."];
        if (!IncidentSeverities.IsValid(request.Severity))
            errors["severity"] = ["Severity must be Warning, Error, or Critical."];
        if (!IsSafeIncidentToken(request.Operation, 1, 80))
            errors["operation"] = ["Operation must contain only letters, numbers, dots, hyphens, or underscores."];
        if (!IsSafeIncidentToken(request.Release, 1, 30))
            errors["release"] = ["Release contains unsupported characters."];
        if (string.IsNullOrWhiteSpace(request.ExceptionFingerprint) ||
            request.ExceptionFingerprint.Length is < 12 or > 64 ||
            request.ExceptionFingerprint.Any(character => !Uri.IsHexDigit(character)))
            errors["exceptionFingerprint"] = ["Exception fingerprint must be 12-64 hexadecimal characters."];
        if (request.CrashDiagnostic is { } diagnostic)
        {
            if (!CrashDiagnosticRules.IsValid(diagnostic))
            {
                errors["crashDiagnostic"] = ["Crash diagnostic metadata is not in the supported bounded format."];
            }
            else
            {
                var heartbeat = DateTime.SpecifyKind(diagnostic.LastHeartbeatUtc, DateTimeKind.Utc);
                var now = DateTime.UtcNow;
                if (heartbeat < now.AddDays(-90) || heartbeat > now.AddMinutes(5))
                    errors["crashDiagnostic.lastHeartbeatUtc"] = ["Crash heartbeat must be within the last 90 days and not in the future."];
                if (diagnostic.WindowsEventTimeUtc is { } eventTime)
                {
                    eventTime = DateTime.SpecifyKind(eventTime, DateTimeKind.Utc);
                    if (eventTime < heartbeat.AddMinutes(-1) || eventTime > heartbeat.AddMinutes(30))
                        errors["crashDiagnostic.windowsEventTimeUtc"] = ["Windows event time is outside the bounded crash-correlation window."];
                }
            }
        }
        return errors.Count == 0 ? null : errors;
    }

    private static bool IsSafeIncidentToken(string? value, int minimumLength, int maximumLength) =>
        value?.Length >= minimumLength && value.Length <= maximumLength &&
        value.All(character => char.IsLetterOrDigit(character) || character is '.' or '-' or '_');

    private static IncidentGroupDto ToIncidentDto(ServerIncidentGroup incident) => new(
        incident.Id,
        incident.AgencyId,
        incident.Scope,
        incident.Source,
        incident.Severity,
        incident.Operation,
        incident.FirstRelease,
        incident.LastRelease,
        incident.ExceptionFingerprint,
        incident.Status,
        incident.OccurrenceCount,
        incident.FirstSeenUtc,
        incident.LastSeenUtc,
        incident.LastReference,
        incident.LastActorRole,
        CrashDiagnosticRules.Deserialize(incident.LastCrashDiagnosticJson));

    /// <summary>
    /// Refuses a second directory entry for an organization this agency has already
    /// recorded under the same durable identifier. The database enforces this with a
    /// filtered unique index; this check exists so the answer names the existing
    /// entry instead of surfacing a constraint violation.
    ///
    /// Scope is deliberately one agency. The same organization appearing in several
    /// agencies' directories is correct — each holds its own local knowledge of that
    /// organization — and must not be treated as a duplicate.
    /// </summary>
    // Affiliation is decided by ProviderAffiliation in Sati.Contracts, the same call the
    // transitional desktop service makes. Only this agency's rows are loaded, so a parent
    // belonging to another tenant fails as "not in this directory" rather than linking
    // across the boundary.
    private static async Task<Dictionary<string, string[]>> ValidateProviderAffiliationAsync(
        ApiDbContext db,
        int agencyId,
        SaveProviderRequest request,
        int editingProviderId,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();

        MedicalProviderKind? kind = null;
        if (!string.IsNullOrWhiteSpace(request.MedicalKind))
        {
            if (!Enum.TryParse<MedicalProviderKind>(request.MedicalKind, out var parsed))
            {
                errors["medicalKind"] = ["The medical provider designation is invalid."];
                return errors;
            }
            kind = parsed;
        }

        var kindProblem = ProviderAffiliation.ValidateKind(request.Type == "Healthcare", kind);
        if (kindProblem is not null)
        {
            errors["medicalKind"] = [kindProblem];
            return errors;
        }

        if (request.ParentProviderId is null)
            return errors;

        var directory = (await db.Providers.AsNoTracking()
                .Where(candidate => candidate.AgencyId == agencyId)
                .Select(candidate => new { candidate.Id, candidate.Name, candidate.ParentProviderId, candidate.MedicalKind })
                .ToListAsync(cancellationToken))
            .Select(candidate => new ProviderAffiliationNode(
                candidate.Id,
                candidate.Name,
                candidate.ParentProviderId,
                Enum.TryParse<MedicalProviderKind>(candidate.MedicalKind, out var storedKind) ? storedKind : null))
            .ToList();

        var parentProblem = ProviderAffiliation.ValidateParent(
            editingProviderId, kind, request.ParentProviderId, directory);
        if (parentProblem is not null)
            errors["parentProviderId"] = [parentProblem];

        return errors;
    }

    private static async Task<IResult?> FindDuplicateProviderAsync(
        ApiDbContext db,
        int agencyId,
        SaveProviderRequest request,
        int? editingProviderId,
        CancellationToken cancellationToken)
    {
        var npi = Normalize(request.Npi);
        var maineCareProviderId = Normalize(request.MaineCareProviderId);
        if (npi is null && maineCareProviderId is null)
            return null;

        var clash = await db.Providers.AsNoTracking()
            .Where(candidate => candidate.AgencyId == agencyId &&
                                (editingProviderId == null || candidate.Id != editingProviderId) &&
                                ((npi != null && candidate.Npi == npi) ||
                                 (maineCareProviderId != null && candidate.MaineCareProviderId == maineCareProviderId)))
            .Select(candidate => new { candidate.Name, candidate.Npi, candidate.MaineCareProviderId })
            .FirstOrDefaultAsync(cancellationToken);

        if (clash is null)
            return null;

        var which = npi is not null && clash.Npi == npi
            ? "National Provider Identifier"
            : "MaineCare provider identifier";

        return Results.Conflict(new ApiErrorDto(
            "duplicate_provider_identifier",
            $"\"{clash.Name}\" is already in this agency's provider directory with the same {which}. " +
            "Edit that entry rather than creating a second one, so the organization stays a single record.",
            string.Empty));
    }

    private static Dictionary<string, string[]> ValidateProvider(SaveProviderRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.Type is not ("Waiver" or "Healthcare" or "Other")) errors["type"] = ["The provider type is invalid."];
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 200) errors["name"] = ["Provider name is required and must not exceed 200 characters."];
        if (request.OfferedServices < 0 || (request.OfferedServices & ~15) != 0) errors["offeredServices"] = ["The selected services are invalid."];

        // Durable identifiers. An NPI carries a Luhn check digit, so a typo is
        // detectable here rather than surfacing years later as a failed match
        // against an organization onboarding as a tenant. BillingRules already owns
        // that check for claim generation; validating it in one place keeps the two
        // from drifting.
        var npi = request.Npi?.Trim();
        if (!string.IsNullOrEmpty(npi) && !BillingRules.IsValidNpi(npi))
            errors["npi"] = ["The National Provider Identifier must be 10 digits with a valid check digit."];

        var maineCareProviderId = request.MaineCareProviderId?.Trim();
        if (maineCareProviderId is { Length: > 30 })
            errors["maineCareProviderId"] = ["The MaineCare provider identifier must not exceed 30 characters."];

        return errors;
    }

    private static void ApplyProvider(ServerProvider provider, SaveProviderRequest request)
    {
        provider.Type = request.Type; provider.Name = request.Name.Trim(); provider.Street = Normalize(request.Street);
        provider.City = Normalize(request.City); provider.State = Normalize(request.State); provider.Zip = Normalize(request.Zip);
        provider.PrimaryContact = Normalize(request.PrimaryContact); provider.Phone = Normalize(request.Phone);
        provider.OfferedServices = request.OfferedServices; provider.ProvidesPassthroughService = request.ProvidesPassthroughService;
        provider.BillingLocationEis = Normalize(request.BillingLocationEis); provider.ProgramContact = Normalize(request.ProgramContact);
        provider.BillingContact = Normalize(request.BillingContact);
        provider.Npi = Normalize(request.Npi); provider.MaineCareProviderId = Normalize(request.MaineCareProviderId);
        provider.MedicalKind = Normalize(request.MedicalKind); provider.ParentProviderId = request.ParentProviderId;
    }

    private static Dictionary<string, string[]> ValidateAtRequest(SaveAtRequestRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        var statuses = new[] { "Development", "Review", "Approved", "Denied", "Appeal", "Received", "Withdrawn" };
        if (!statuses.Contains(request.Status)) errors["status"] = ["The request status is invalid."];
        if (request.SalesTax < 0) errors["salesTax"] = ["Sales tax cannot be negative."];
        if (request.Items.Count > 500 || request.Items.Any(x => x.ItemCost < 0 || x.Quantity < 1 || x.Quantity > 10000))
            errors["items"] = ["Request items contain an invalid cost or quantity."];

        // Screenshots are re-checked here even though the desktop caps them at the
        // paste boundary. A client-side limit tells the user something useful; it
        // does not constrain what arrives in a request body.
        foreach (var item in request.Items)
        {
            if (item.ScreenshotBase64 is null)
                continue;

            var decoded = TryDecodeBase64(item.ScreenshotBase64);
            var problem = decoded is null
                ? "A screenshot was not valid base64 data."
                : AtRequestScreenshot.Describe(decoded);
            if (problem is not null)
            {
                errors["screenshots"] = [problem];
                break;
            }
        }
        return errors;
    }

    private static byte[]? DecodeScreenshot(string? base64) =>
        string.IsNullOrEmpty(base64) ? null : TryDecodeBase64(base64);

    private static byte[]? TryDecodeBase64(string value)
    {
        // Convert.FromBase64String throws on malformed input, and a bad paste is
        // a client mistake, not a server fault. TryFromBase64String needs a
        // buffer sized from the encoded length.
        var buffer = new byte[value.Length * 3 / 4 + 3];
        return Convert.TryFromBase64String(value, buffer, out var written)
            ? buffer.AsSpan(0, written).ToArray()
            : null;
    }

    private static void ApplyAtRequest(ServerAtRequest request, SaveAtRequestRequest input)
    {
        request.VendorName = Normalize(input.VendorName); request.VendorBillingLocation = Normalize(input.VendorBillingLocation);
        request.VendorProgramContact = Normalize(input.VendorProgramContact); request.VendorBillingContact = Normalize(input.VendorBillingContact);
        request.SalesTax = input.SalesTax; request.SalesTaxOverridden = input.SalesTaxOverridden;
        request.SubmittedDate = input.SubmittedDate?.Date;
        request.DecisionDate = input.DecisionDate?.Date; request.Status = input.Status;
        request.Items.Clear();
        foreach (var item in input.Items)
            request.Items.Add(new ServerAtRequestItem { Name = Normalize(item.Name), ItemCost = item.ItemCost,
                Quantity = item.Quantity, Url = Normalize(item.Url),
                ScreenshotPng = DecodeScreenshot(item.ScreenshotBase64) });
    }

    private static async Task<ServerAtRequest?> LoadAccessibleAtRequestAsync(
        ApiDbContext db, Actor actor, int id, CancellationToken cancellationToken)
    {
        var request = await db.AtRequests.Include(x => x.Items).SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (request is null) return null;
        var person = await db.People.AsNoTracking().SingleOrDefaultAsync(
            x => x.Id == request.PersonId, cancellationToken);
        return person is not null &&
               await TenantAccess.CanAccessPersonAsync(db, actor, person, cancellationToken)
            ? request
            : null;
    }

    private static void PreventSensitiveResponseCaching(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store, no-cache";
        context.Response.Headers.Pragma = "no-cache";
    }

    /// <summary>
    /// Removes a stored SSN.
    ///
    /// Every part goes, including the last four. Leaving the tail behind would keep a
    /// consumer who asked to have their number removed partially on file, and would
    /// leave the mask claiming a number that can no longer be produced.
    /// </summary>
    private static void ClearSsn(ServerPerson person)
    {
        person.SsnCiphertext = null;
        person.SsnNonce = null;
        person.SsnTag = null;
        person.SsnWrappedKey = null;
        person.SsnKeyId = null;
        person.SsnLastFour = null;
    }

    private static async Task ProtectSsnAsync(
        ServerPerson person,
        int agencyId,
        string normalized,
        EnvelopeProtector protector,
        CancellationToken cancellationToken)
    {
        var binding = new FieldBinding(agencyId, person.Id, "Ssn");
        var protectedValue = await protector.ProtectAsync(normalized, binding, cancellationToken);
        person.SsnCiphertext = protectedValue.Ciphertext;
        person.SsnNonce = protectedValue.Nonce;
        person.SsnTag = protectedValue.Tag;
        person.SsnWrappedKey = protectedValue.WrappedDataKey;
        person.SsnKeyId = protectedValue.KeyId;
        person.SsnLastFour = SsnMask.LastFourOf(normalized);
    }

    private static string SafeFileName(string value)
    {
        var safe = new string(value
            .Select(character => char.IsLetterOrDigit(character) || character is '-' or '_'
                ? character
                : '-')
            .ToArray());
        while (safe.Contains("--", StringComparison.Ordinal))
            safe = safe.Replace("--", "-", StringComparison.Ordinal);
        return string.IsNullOrWhiteSpace(safe.Trim('-')) ? "person" : safe.Trim('-');
    }

    private static string? ComposeAddress(params string?[] parts)
    {
        var present = parts.Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part!.Trim())
            .ToArray();
        return present.Length == 0 ? null : string.Join(", ", present);
    }

    private static IReadOnlyList<string> ReleaseDraftBlankFields(AgencyReleaseRequest request)
    {
        var fields = new List<string>();
        if (request.AuthorizationGranted is null) fields.Add(nameof(request.AuthorizationGranted));
        if (string.IsNullOrWhiteSpace(request.ContactName)) fields.Add(nameof(request.ContactName));
        if (request.InformationCategories is null || request.InformationCategories.Count == 0) fields.Add(nameof(request.InformationCategories));
        if (request.StartDate is null) fields.Add(nameof(request.StartDate));
        if (request.ExpirationDate is null) fields.Add(nameof(request.ExpirationDate));
        if (string.IsNullOrWhiteSpace(request.Scope)) fields.Add(nameof(request.Scope));
        if (request.IncludeDrugAlcohol is null) fields.Add(nameof(request.IncludeDrugAlcohol));
        if (request.IncludeMentalHealth is null) fields.Add(nameof(request.IncludeMentalHealth));
        if (request.IncludeHivAids is null) fields.Add(nameof(request.IncludeHivAids));
        return fields;
    }

    private static int[] MatchingArtifactIds(
        string formType,
        IReadOnlyCollection<ArtifactFact> artifacts)
    {
        var entry = AnnualDocumentCatalog.ForFormType(formType);
        return entry is null
            ? []
            : artifacts.Where(artifact =>
                    artifact.Kind.Equals(entry.Kind.ToString(), StringComparison.OrdinalIgnoreCase) &&
                    !artifact.IsDraft)
                .Select(artifact => artifact.ArtifactId)
                .Distinct().Order().ToArray();
    }

}
