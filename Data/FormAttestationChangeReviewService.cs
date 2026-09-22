using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;

namespace Sati.Data;

public sealed class FormAttestationChangeReviewService(
    IDbContextFactory<SatiContext> contextFactory,
    ISessionService sessionService) : IFormAttestationChangeReviewService
{
    public Task<IReadOnlyList<FormAttestationChangeReviewFlagDto>> GetForSupervisorAsync(
        CancellationToken cancellationToken = default) =>
        GetAsync(forSupervisor: true, cancellationToken);

    public Task<IReadOnlyList<FormAttestationChangeReviewFlagDto>> GetForBillingAsync(
        CancellationToken cancellationToken = default) =>
        GetAsync(forSupervisor: false, cancellationToken);

    private async Task<IReadOnlyList<FormAttestationChangeReviewFlagDto>> GetAsync(
        bool forSupervisor,
        CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var actor = await LocalTenantAccess.EnsureSessionAsync(
            db, sessionService, cancellationToken);
        if (forSupervisor ? !actor.HasSupervisorPermissions : !actor.HasBillingPermissions)
            throw new UnauthorizedAccessException("This review queue is outside your assigned role.");

        var flags = db.FormAttestationChangeReviewFlags.AsNoTracking()
            .Where(flag => flag.AgencyId == actor.AgencyId);
        if (forSupervisor)
        {
            var canReviewAgency = UserPermissionRules.HasAgencyWideSupervisionPermissions(
                actor.Permissions);
            flags = flags.Where(flag =>
                flag.RequiresSupervisorAttention &&
                db.People.Any(person =>
                    person.Id == flag.PersonId &&
                    person.AgencyId == actor.AgencyId &&
                    db.Users.Any(owner =>
                        owner.Id == person.UserId &&
                        owner.AgencyId == actor.AgencyId &&
                        (owner.Permissions & UserPermissions.CaseManagement) != 0 &&
                        (canReviewAgency || owner.SupervisorId == actor.Id))));
        }
        else
        {
            flags = flags.Where(flag => flag.RequiresBillingAttention);
        }

        var rows = await flags.OrderByDescending(flag => flag.CreatedAtUtc)
            .ThenByDescending(flag => flag.Id)
            .ToListAsync(cancellationToken);
        return rows.Select(flag => flag.ToContract()).ToArray();
    }
}
