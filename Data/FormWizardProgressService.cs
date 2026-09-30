using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Models;

namespace Sati.Data;

public sealed class FormWizardProgressService(
    IDbContextFactory<SatiContext> contextFactory,
    ISessionService session,
    EnvelopeProtector protector) : IFormWizardProgressService
{
    public async Task<FormWizardProgressDto?> GetAsync(int personId, string formKey,
        CancellationToken cancellationToken = default)
    {
        EnsureKey(formKey);
        var actor = session.CurrentUser ?? throw new InvalidOperationException("Sign in first.");
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await EnsureOwnsAsync(db, actor, personId, cancellationToken);
        var row = await db.FormWizardProgress.AsNoTracking().SingleOrDefaultAsync(x =>
            x.PersonId == personId && x.AgencyId == actor.AgencyId &&
            x.AuthorUserId == actor.Id && x.FormKey == formKey, cancellationToken);
        if (row is null) return null;
        var answers = await protector.UnprotectAsync(row.ProtectedAnswers, row.Binding,
            cancellationToken);
        LocalAuditTrail.Record(db, actor, "form-wizard-progress.viewed", "Person", personId,
            JsonSerializer.Serialize(new { formKey, revision = row.Revision }));
        await db.SaveChangesAsync(cancellationToken);
        return new(formKey, row.Revision, row.StepIndex, answers, row.UpdatedAtUtc);
    }

    public async Task<FormWizardProgressDto> SaveAsync(int personId, string formKey,
        SaveFormWizardProgressRequest request, CancellationToken cancellationToken = default)
    {
        if (!FormWizardProgressRules.Valid(formKey, request))
            throw new ArgumentException("The form draft is invalid or too large.", nameof(request));
        var actor = session.CurrentUser ?? throw new InvalidOperationException("Sign in first.");
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await EnsureOwnsAsync(db, actor, personId, cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        var row = await db.FormWizardProgress.SingleOrDefaultAsync(x =>
            x.PersonId == personId && x.AgencyId == actor.AgencyId &&
            x.AuthorUserId == actor.Id && x.FormKey == formKey, cancellationToken);
        if (row is null && request.ExpectedRevision != 0 ||
            row is not null && row.Revision != request.ExpectedRevision)
            throw new DbUpdateConcurrencyException("The draft changed in another session. Reload it first.");
        row ??= new FormWizardProgress
        {
            PersonId = personId, AgencyId = actor.AgencyId,
            AuthorUserId = actor.Id, FormKey = formKey
        };
        row.SetProtectedAnswers(await protector.ProtectAsync(request.AnswersJson,
            row.Binding, cancellationToken));
        row.Revision++;
        row.StepIndex = request.StepIndex;
        row.UpdatedAtUtc = DateTime.UtcNow;
        if (row.Id == 0) db.FormWizardProgress.Add(row);
        LocalAuditTrail.Record(db, actor, "form-wizard-progress.saved", "Person", personId,
            JsonSerializer.Serialize(new { formKey, revision = row.Revision,
                stepIndex = row.StepIndex }));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(formKey, row.Revision, row.StepIndex, request.AnswersJson, row.UpdatedAtUtc);
    }

    private static void EnsureKey(string formKey)
    {
        if (!FormWizardProgressRules.ValidKey(formKey))
            throw new ArgumentException("Unknown form wizard.", nameof(formKey));
    }

    private async Task EnsureOwnsAsync(SatiContext db, User actor, int personId,
        CancellationToken token)
    {
        await LocalTenantAccess.EnsureSessionAsync(db, session);
        if (!await LocalTenantAccess.OwnsPersonAsync(db, actor, personId, token))
            throw new UnauthorizedAccessException("That consumer is not on your current caseload.");
    }
}
