using Microsoft.EntityFrameworkCore;
using Sati.Models.Assessments;
using System.Text.Json;

namespace Sati.Data;

public sealed class PersonCenteredPlanSourceService(IDbContextFactory<SatiContext> contextFactory,
    ISessionService sessionService)
    : IPersonCenteredPlanSourceService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<PersonCenteredPlanSource?> GetSourceAsync(int personId, int preferredAuthorUserId)
    {
        var actor = sessionService.CurrentUser
            ?? throw new UnauthorizedAccessException("Sign in to read a person-centered plan source.");
        await using var db = await contextFactory.CreateDbContextAsync();
        if (!await LocalTenantAccess.CanAccessPersonAsync(db, actor, personId) ||
            !await db.People.AsNoTracking().AnyAsync(person =>
                person.Id == personId && person.UserId == preferredAuthorUserId && person.AgencyId == actor.AgencyId))
        {
            throw new UnauthorizedAccessException("That assessment source is not available to this user.");
        }

        // An approved assessment is the authoritative PCP source. Until approval
        // exists, expose only the assigned author's working version to users who
        // currently reach that caseload, and label it provisional in the workspace.
        var assessment = await db.ComprehensiveAssessments
            .AsNoTracking()
            .Where(item => item.PersonId == personId && item.Status == AssessmentStatus.Approved)
            .OrderByDescending(item => item.Version)
            .FirstOrDefaultAsync();

        assessment ??= await db.ComprehensiveAssessments
            .AsNoTracking()
            .Where(item => item.PersonId == personId && item.AuthorUserId == preferredAuthorUserId)
            .Where(item => item.Status == AssessmentStatus.Draft
                || item.Status == AssessmentStatus.Returned
                || item.Status == AssessmentStatus.ReadyForReview)
            .OrderByDescending(item => item.Version)
            .FirstOrDefaultAsync();

        if (assessment is null)
            return null;

        var document = JsonSerializer.Deserialize<AssessmentDocument>(assessment.DocumentJson, JsonOptions)
            ?? new AssessmentDocument();
        return new PersonCenteredPlanSource(
            assessment.Id,
            assessment.Version,
            assessment.Status,
            assessment.UpdatedAt,
            document);
    }
}
