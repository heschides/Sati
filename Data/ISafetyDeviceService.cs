using Sati.Contracts.V1;

namespace Sati.Data;

public interface ISafetyDeviceService
{
    Task<SafetyDeviceResult> GenerateAsync(int personId, SafetyDeviceRequest request,
        CancellationToken cancellationToken = default);
}
