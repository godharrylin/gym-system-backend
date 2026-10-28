using gym_system.Domain.Entities.Devices;

namespace gym_system.Domain.Repositories;

public interface IGatePassageRepository
{
    Task<GatePassageRecord?> FindByRequestIdAsync(Guid requestId, CancellationToken ct);
    Task<bool> TryCreateAsync(
        Guid requestId, GateDeviceAuthorization device, string? userId, string identityKind,
        string roleSnapshot, DateTime requestedAt, CancellationToken ct);
    Task<bool> CompleteAsync(
        Guid requestId, string outcome, string resultCode, bool isSuccess,
        bool isSimulated, bool affectsPresence, DateTime respondedAt, string? remark,
        CancellationToken ct);
}

