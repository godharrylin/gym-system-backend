using Dapper;
using gym_system.Domain.Entities.Devices;
using gym_system.Domain.Repositories;
using gym_system.Infrastructures.Connections;
using Microsoft.Data.SqlClient;

namespace gym_system.Infrastructures.Devices;

internal sealed class SqlGatePassageRepository(ISqlSession session) : IGatePassageRepository
{
    public async Task<GatePassageRecord?> FindByRequestIdAsync(Guid requestId, CancellationToken ct)
    {
        var row = await session.Connection.QuerySingleOrDefaultAsync<GatePassageRow>(new CommandDefinition("""
            SELECT request_id AS RequestId,gate_station_sn AS StationId,
                   gate_device_session_id AS DeviceSessionId,direction AS Direction,
                   outcome AS Outcome,result_code AS ResultCode,usr_id AS UserId,
                   identity_kind AS IdentityKind,is_simulated AS IsSimulated,
                   requested_at AS RequestedAt,responded_at AS RespondedAt,
                   affects_presence AS AffectsPresence
            FROM dbo.device_record WHERE request_id=@requestId;
            """, new { requestId }, session.Transaction, cancellationToken: ct));
        if (row is null) return null;
        if (row.Direction is not (1 or 2))
            throw new InvalidOperationException("Stored gate passage direction must be Entry(1) or Exit(2).");
        return new(row.RequestId, row.StationId, row.DeviceSessionId, (GateDirection)row.Direction,
            row.Outcome, row.ResultCode, row.UserId, row.IdentityKind, row.IsSimulated,
            row.RequestedAt, row.RespondedAt, row.AffectsPresence);
    }

    public async Task<bool> TryCreateAsync(Guid requestId, GateDeviceAuthorization device,
        string? userId, string identityKind, string roleSnapshot, DateTime requestedAt, CancellationToken ct)
    {
        try
        {
            var changed = await session.Connection.ExecuteAsync(new CommandDefinition("""
                INSERT dbo.device_record
                    (usr_id,direction,method,device_id,is_success,device_record_dt,request_id,
                     gate_station_sn,gate_device_session_id,identity_kind,role_snapshot,operation_kind,
                     outcome,result_code,requested_at,affects_presence,is_simulated)
                VALUES (@userId,@direction,N'Phone',@stationCode,0,@requestedAt,@requestId,
                        @stationId,@sessionId,@identityKind,@roleSnapshot,'CustomerGate',
                        'Requested','REQUESTED',@requestedAt,0,0);
                """, new
            {
                userId, direction = (int)device.Station.Direction, stationCode = device.Station.Code,
                requestedAt, requestId, stationId = device.Station.Id, sessionId = device.Session.Id,
                identityKind, roleSnapshot
            }, session.Transaction, cancellationToken: ct));
            return changed == 1;
        }
        catch (SqlException error) when (error.Number is 2601 or 2627) { return false; }
    }

    public async Task<bool> CompleteAsync(Guid requestId, string outcome, string resultCode,
        bool isSuccess, bool isSimulated, bool affectsPresence, DateTime respondedAt,
        string? remark, CancellationToken ct)
    {
        var changed = await session.Connection.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.device_record
               SET outcome=@outcome,result_code=@resultCode,is_success=@isSuccess,
                   is_simulated=@isSimulated,affects_presence=@affectsPresence,
                   responded_at=@respondedAt,completed_at=@respondedAt,
                   device_record_dt=CASE WHEN @affectsPresence=1 THEN @respondedAt ELSE device_record_dt END,
                   remark=@remark
             WHERE request_id=@requestId AND outcome='Requested';
            """, new { requestId, outcome, resultCode, isSuccess, isSimulated,
                affectsPresence, respondedAt, remark }, session.Transaction, cancellationToken: ct));
        return changed == 1;
    }

    private sealed class GatePassageRow
    {
        public Guid RequestId { get; init; }
        public int StationId { get; init; }
        public Guid DeviceSessionId { get; init; }
        public int Direction { get; init; }
        public string Outcome { get; init; } = string.Empty;
        public string ResultCode { get; init; } = string.Empty;
        public string? UserId { get; init; }
        public string IdentityKind { get; init; } = string.Empty;
        public bool? IsSimulated { get; init; }
        public DateTime RequestedAt { get; init; }
        public DateTime? RespondedAt { get; init; }
        public bool AffectsPresence { get; init; }
    }
}

