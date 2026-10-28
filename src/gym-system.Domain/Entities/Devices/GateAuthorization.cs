namespace gym_system.Domain.Entities.Devices;

public enum GateDirection : byte { Entry = 1, Exit = 2 }

public sealed record GateStation(
    int Id, string Code, string Name, GateDirection Direction, bool IsActive, Guid? CurrentSessionId);

public sealed record GateDeviceSession(
    Guid Id, int StationId, string Status, DateTime CreatedAt, DateTime? ExpiresAt);

public sealed record GateDeviceAuthorization(GateStation Station, GateDeviceSession Session)
{
    public bool IsValidAt(DateTime now) =>
        Station.IsActive && Station.CurrentSessionId == Session.Id &&
        Station.Id == Session.StationId && Session.Status == "Active" &&
        (Session.ExpiresAt is null || now < Session.ExpiresAt);
}
