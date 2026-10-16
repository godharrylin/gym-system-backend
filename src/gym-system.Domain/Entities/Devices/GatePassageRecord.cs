namespace gym_system.Domain.Entities.Devices;

public sealed record GatePassageRecord(
    Guid RequestId,
    int StationId,
    Guid DeviceSessionId,
    GateDirection Direction,
    string Outcome,
    string ResultCode,
    string? UserId,
    string IdentityKind,
    bool? IsSimulated,
    DateTime RequestedAt,
    DateTime? RespondedAt,
    bool AffectsPresence);

