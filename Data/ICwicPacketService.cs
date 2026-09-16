using Sati.Contracts.V1;

namespace Sati.Data;

public interface ICwicPacketService
{
    Task<CwicPacketResult> GenerateAsync(
        int personId,
        CwicPacketRequest request,
        CancellationToken cancellationToken = default);
}
