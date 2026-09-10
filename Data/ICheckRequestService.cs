using Sati.Models;

namespace Sati.Data;

public interface ICheckRequestService
{
    Task<List<CheckRequestListItem>> GetAllForPersonAsync(int personId);
    Task<CheckRequest?> GetByIdAsync(int id);
    Task<CheckRequest> CreateDraftAsync(int personId);
    Task<CheckRequest> UpdateAsync(CheckRequest request);
    Task<CheckRequest> PublishAsync(CheckRequest request);
}

public sealed record CheckRequestListItem(
    int Id,
    int Revision,
    DateTime? RequestDate,
    string? PayableTo,
    decimal Amount,
    DateTime? NeededByDate,
    DateTime? PublishedAtUtc)
{
    public bool IsPublished => PublishedAtUtc is not null;
    public string Status => IsPublished ? "PDF prepared" : "Draft";
}
