using Sati.Contracts.V1;

namespace Sati.Data;

public sealed class ConsumerProviderOrderConflictException()
    : InvalidOperationException(ConsumerProviderOrder.ConflictMessage);
