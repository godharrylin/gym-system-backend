using Dapper;
using gym_system.Domain.Entities.Devices;
using gym_system.Domain.Repositories;
using gym_system.Infrastructures.Connections;

namespace gym_system.Infrastructures.Devices;

internal sealed class SqlGateDeviceRepository(ISqlSession session) : IGateDeviceRepository
{
    public async Task<GateStation?> FindStationAsync(string code, bool forUpdate, CancellationToken ct)
    {
        if (forUpdate) RequireTransaction();
        var hint = forUpdate ? " WITH (UPDLOCK, HOLDLOCK)" : "";
        return await session.Connection.QuerySingleOrDefaultAsync<GateStation>(new CommandDefinition($"""
            SELECT gate_station_sn AS Id, gate_station_code AS Code, gate_station_name AS Name,
                   gate_station_direction AS Direction, gate_station_is_active AS IsActive,
                   gate_station_current_session_id AS CurrentSessionId
            FROM dbo.gate_station{hint} WHERE gate_station_code = @code;
            """, new { code }, session.Transaction, cancellationToken: ct));
    }

    public async Task<GateDeviceAuthorization?> FindAuthorizationAsync(Guid sessionId, CancellationToken ct)
    {
        // One statement: do not combine a station version and session from different reads.
        var rows = await session.Connection.QueryAsync<GateStation, GateDeviceSession, GateDeviceAuthorization>(
            new CommandDefinition("""
                SELECT s.gate_station_sn AS Id, s.gate_station_code AS Code, s.gate_station_name AS Name,
                       s.gate_station_direction AS Direction, s.gate_station_is_active AS IsActive,
                       s.gate_station_current_session_id AS CurrentSessionId,
                       d.gate_device_session_id AS Id, d.gate_station_sn AS StationId,
                       d.gate_device_session_status AS Status, d.gate_device_session_create_dt AS CreatedAt,
                       d.gate_device_session_expire_dt AS ExpiresAt
                FROM dbo.gate_device_session d JOIN dbo.gate_station s ON s.gate_station_sn=d.gate_station_sn
                WHERE d.gate_device_session_id=@sessionId;
                """, new { sessionId }, session.Transaction, cancellationToken: ct),
            (station, device) => new(station, device), splitOn: "Id");
        return rows.SingleOrDefault();
    }

    public async Task ReplaceAsync(GateStation station, Guid sessionId, DateTime now, CancellationToken ct)
    {
        RequireTransaction();
        await session.Connection.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.gate_device_session SET gate_device_session_status='Revoked',
                gate_device_session_revoke_dt=@now, gate_device_session_revoke_reason='Replaced',
                gate_device_session_revoke_by=N'SYSTEM', gate_device_session_up_dt=@now
            WHERE gate_station_sn=@Id AND gate_device_session_status='Active';
            INSERT dbo.gate_device_session
                (gate_device_session_id,gate_station_sn,gate_device_session_status,
                 gate_device_session_previous_id,gate_device_session_create_dt,gate_device_session_expire_dt)
            VALUES (@sessionId,@Id,'Active',@CurrentSessionId,@now,NULL);
            UPDATE dbo.gate_station SET gate_station_current_session_id=@sessionId,gate_station_up_dt=@now
            WHERE gate_station_sn=@Id;
            """, new { station.Id, station.CurrentSessionId, sessionId, now }, session.Transaction, cancellationToken: ct));
    }

    public async Task RevokeAsync(GateStation station, string operatorId, DateTime now, CancellationToken ct)
    {
        RequireTransaction();
        await session.Connection.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.gate_device_session SET gate_device_session_status='Revoked',
                gate_device_session_revoke_dt=@now,gate_device_session_revoke_reason='Manual',
                gate_device_session_revoke_by=@operatorId,gate_device_session_up_dt=@now
            WHERE gate_device_session_id=@CurrentSessionId AND gate_station_sn=@Id AND gate_device_session_status='Active';
            UPDATE dbo.gate_station SET gate_station_current_session_id=NULL,gate_station_up_dt=@now
            WHERE gate_station_sn=@Id AND gate_station_current_session_id=@CurrentSessionId;
            """, new { station.Id, station.CurrentSessionId, operatorId, now }, session.Transaction, cancellationToken: ct));
    }

    private void RequireTransaction()
    {
        if (session.Transaction is null) throw new InvalidOperationException("Station mutations require a transaction and a station lock.");
    }
}
