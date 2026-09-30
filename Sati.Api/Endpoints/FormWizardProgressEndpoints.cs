using System.Data;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sati.Api.Data;
using Sati.Api.Security;
using Sati.Contracts.V1;
using Sati.Models;

namespace Sati.Api.Endpoints;

internal static partial class ApiEndpoints
{
    private static void MapFormWizardProgress(RouteGroupBuilder api)
    {
        const string route = "/people/{personId:int}/form-wizards/{formKey}/progress";
        api.MapGet(route, async Task<IResult> (int personId, string formKey,
            ClaimsPrincipal principal, HttpContext http, ApiDbContext db,
            EnvelopeProtector protector, AuditTrail audit, CancellationToken token) =>
        {
            if (!FormWizardProgressRules.ValidKey(formKey)) return Results.NotFound();
            var actor = Actor.From(principal);
            if (!await TenantAccess.OwnsPersonAsync(db, actor, personId, token)) return Results.NotFound();
            var row = await db.FormWizardProgress.AsNoTracking().SingleOrDefaultAsync(x =>
                x.PersonId == personId && x.AgencyId == actor.AgencyId &&
                x.AuthorUserId == actor.UserId && x.FormKey == formKey, token);
            PreventSensitiveResponseCaching(http);
            if (row is null)
                return Results.Ok(new FormWizardProgressDto(formKey, 0, 0, "{}", DateTime.MinValue));
            var answers = await protector.UnprotectAsync(row.ProtectedAnswers, row.Binding, token);
            audit.Record(actor, AuditActions.FormWizardProgressViewed, "Person", personId,
                JsonSerializer.Serialize(new { formKey, revision = row.Revision }));
            await db.SaveChangesAsync(token);
            return Results.Ok(new FormWizardProgressDto(formKey, row.Revision,
                row.StepIndex, answers, row.UpdatedAtUtc));
        });

        api.MapPut(route, async Task<IResult> (int personId, string formKey,
            SaveFormWizardProgressRequest request, ClaimsPrincipal principal, HttpContext http,
            ApiDbContext db, EnvelopeProtector protector, AuditTrail audit,
            CancellationToken token) =>
        {
            if (!FormWizardProgressRules.ValidKey(formKey)) return Results.NotFound();
            if (!FormWizardProgressRules.Valid(formKey, request))
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["progress"] = ["The draft data is invalid or too large."]
                });
            var actor = Actor.From(principal);
            if (!await TenantAccess.OwnsPersonAsync(db, actor, personId, token)) return Results.NotFound();

            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, token);
            var row = await db.FormWizardProgress.SingleOrDefaultAsync(x =>
                x.PersonId == personId && x.AgencyId == actor.AgencyId &&
                x.AuthorUserId == actor.UserId && x.FormKey == formKey, token);
            if (row is null && request.ExpectedRevision != 0 ||
                row is not null && row.Revision != request.ExpectedRevision)
                return WizardProgressConflict();
            row ??= new FormWizardProgress
            {
                PersonId = personId, AgencyId = actor.AgencyId,
                AuthorUserId = actor.UserId, FormKey = formKey
            };
            var protectedValue = await protector.ProtectAsync(request.AnswersJson,
                row.Binding, token);
            row.SetProtectedAnswers(protectedValue);
            row.Revision++;
            row.StepIndex = request.StepIndex;
            row.UpdatedAtUtc = DateTime.UtcNow;
            if (row.Id == 0) db.FormWizardProgress.Add(row);
            audit.Record(actor, AuditActions.FormWizardProgressSaved, "Person", personId,
                JsonSerializer.Serialize(new { formKey, revision = row.Revision,
                    stepIndex = row.StepIndex }));
            try
            {
                await db.SaveChangesAsync(token);
                await transaction.CommitAsync(token);
            }
            catch (DbUpdateConcurrencyException) { return WizardProgressConflict(); }
            catch (DbUpdateException error) when (error.InnerException is
                Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 })
            { return WizardProgressConflict(); }
            PreventSensitiveResponseCaching(http);
            return Results.Ok(new FormWizardProgressDto(formKey, row.Revision,
                row.StepIndex, request.AnswersJson, row.UpdatedAtUtc));
        });

    }

    private static IResult WizardProgressConflict() => Results.Conflict(new ApiErrorDto(
        "form_wizard_progress_conflict",
        "This draft changed in another session. Reload it before saving again.", string.Empty));
}
