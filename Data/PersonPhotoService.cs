using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Models;

namespace Sati.Data;

/// <summary>
/// Transitional Local Production implementation. It mirrors the API's authorization,
/// validation, concurrency and audit rules while Local Production still writes through
/// desktop services.
/// </summary>
public sealed class PersonPhotoService(
    IDbContextFactory<SatiContext> contextFactory,
    ISessionService sessionService) : IPersonPhotoService
{
    public async Task<PersonPhotoDto?> GetAsync(int personId)
    {
        var actor = CurrentActor();
        await using var db = contextFactory.CreateDbContext();
        await LocalTenantAccess.EnsureSessionAsync(db, sessionService);
        if (!await LocalTenantAccess.CanAccessPersonAsync(db, actor, personId))
            throw new InvalidOperationException("This consumer is not available in your current caseload.");

        return await db.PersonPhotos.AsNoTracking()
            .Where(photo => photo.PersonId == personId && photo.AgencyId == actor.AgencyId)
            .Select(photo => new PersonPhotoDto(
                photo.ContentType,
                photo.Content,
                photo.PixelWidth,
                photo.PixelHeight,
                photo.Revision))
            .SingleOrDefaultAsync();
    }

    public async Task<PersonPhotoDto> SaveAsync(
        int personId,
        string contentType,
        byte[] content,
        long? expectedRevision)
    {
        var actor = CurrentActor();
        var inspection = PersonPhotoRules.Inspect(content, contentType, out var problem);
        if (inspection is null)
            throw new ArgumentException(problem, nameof(content));

        await using var db = contextFactory.CreateDbContext();
        await LocalTenantAccess.EnsureSessionAsync(db, sessionService);
        await using var transaction = await db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable);
        if (!await LocalTenantAccess.OwnsPersonAsync(db, actor, personId))
            throw new InvalidOperationException("Only the assigned case manager can change this consumer's photo.");

        var stored = await db.PersonPhotos.SingleOrDefaultAsync(photo =>
            photo.PersonId == personId && photo.AgencyId == actor.AgencyId);
        if (stored is null)
        {
            if (expectedRevision is not null)
                throw StalePhoto();
            stored = new PersonPhoto
            {
                PersonId = personId,
                AgencyId = actor.AgencyId,
                Revision = 1
            };
            db.PersonPhotos.Add(stored);
        }
        else
        {
            if (expectedRevision != stored.Revision)
                throw StalePhoto();
            stored.Revision++;
        }

        stored.Content = content.ToArray();
        stored.ContentType = inspection.ContentType;
        stored.ContentSha256 = Convert.ToHexString(SHA256.HashData(content));
        stored.PixelWidth = inspection.PixelWidth;
        stored.PixelHeight = inspection.PixelHeight;
        stored.UpdatedAtUtc = DateTime.UtcNow;
        stored.UpdatedByUserId = actor.Id;
        LocalAuditTrail.Record(db, actor, LocalAuditActions.PersonPhotoUpdated, "Person", personId);
        try
        {
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (DbUpdateException)
        {
            throw StalePhoto();
        }
        return ToDto(stored);
    }

    public async Task DeleteAsync(int personId, long expectedRevision)
    {
        var actor = CurrentActor();
        await using var db = contextFactory.CreateDbContext();
        await LocalTenantAccess.EnsureSessionAsync(db, sessionService);
        await using var transaction = await db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable);
        if (!await LocalTenantAccess.OwnsPersonAsync(db, actor, personId))
            throw new InvalidOperationException("Only the assigned case manager can remove this consumer's photo.");

        var stored = await db.PersonPhotos.SingleOrDefaultAsync(photo =>
            photo.PersonId == personId && photo.AgencyId == actor.AgencyId);
        if (stored is null || stored.Revision != expectedRevision)
            throw StalePhoto();

        db.PersonPhotos.Remove(stored);
        LocalAuditTrail.Record(db, actor, LocalAuditActions.PersonPhotoRemoved, "Person", personId);
        try
        {
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw StalePhoto();
        }
    }

    private User CurrentActor() => sessionService.CurrentUser
        ?? throw new UnauthorizedAccessException("A signed-in user is required.");

    private static InvalidOperationException StalePhoto() => new(
        "This photo changed after you opened the consumer. Select the consumer again before trying once more.");

    private static PersonPhotoDto ToDto(PersonPhoto photo) => new(
        photo.ContentType,
        photo.Content.ToArray(),
        photo.PixelWidth,
        photo.PixelHeight,
        photo.Revision);
}
