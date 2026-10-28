using gym_system.Domain.Entities.Devices;

namespace gym_system.Domain.Repositories;

public interface IGateDeviceRepository
{
    // Pairing/revocation acquire a station lock inside a caller-owned transaction.
    Task<GateStation?> FindStationAsync(string code, bool forUpdate, CancellationToken ct);
    Task<GateDeviceAuthorization?> FindAuthorizationAsync(Guid sessionId, CancellationToken ct);
    Task ReplaceAsync(GateStation station, Guid sessionId, DateTime now, CancellationToken ct);
    Task RevokeAsync(GateStation station, string operatorId, DateTime now, CancellationToken ct);
}
