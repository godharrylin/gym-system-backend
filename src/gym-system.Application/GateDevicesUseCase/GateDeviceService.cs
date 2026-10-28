using gym_system.Domain.Entities.Devices;
using gym_system.Domain.Repositories;

namespace gym_system.Application.GateDevicesUseCase;

public sealed class GateDeviceException(string code, string message, int statusCode) : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
}

public sealed class GateDeviceService(
    IGateDeviceRepository repository, IUnitOfWork unitOfWork, IClock clock, GateDeviceSettings settings)
{
    public async Task<GateStation> PreviewAsync(string code, CancellationToken ct)
    {
        var stationCode = Resolve(code);
        var station = await repository.FindStationAsync(stationCode, false, ct);
        return RequireActive(station);
    }

    public async Task<GateDeviceAuthorization?> FindValidAsync(Guid id, CancellationToken ct)
    {
        if (!settings.Enabled) return null;
        var authorization = await repository.FindAuthorizationAsync(id, ct);
        return authorization?.IsValidAt(clock.Now()) == true ? authorization : null;
    }

    public async Task<GateDeviceAuthorization> PairAsync(
        string code, string previewStationCode, Guid? expectedSessionId, Guid? browserSessionId, CancellationToken ct)
    {
        var stationCode = Resolve(code);
        if (stationCode != previewStationCode)
            throw new GateDeviceException("PAIRING_PREVIEW_INVALID", "配對資訊已變更，請重新預覽。", 409);

        await unitOfWork.BeginAsync(ct);
        try
        {
            var station = RequireActive(await repository.FindStationAsync(stationCode, true, ct));
            if (station.CurrentSessionId != expectedSessionId)
                throw new GateDeviceException("PAIRING_CONFLICT", "站點授權已變更，請重新預覽並確認換機。", 409);

            // A valid browser pairing its own station keeps its authorization/history.
            if (browserSessionId is { } current && current == station.CurrentSessionId)
            {
                var existing = await FindValidAsync(current, ct);
                if (existing is not null)
                {
                    await unitOfWork.CommitAsync(ct);
                    return existing;
                }
            }

            var now = clock.Now();
            var sessionId = Guid.NewGuid();
            await repository.ReplaceAsync(station, sessionId, now, ct);
            await unitOfWork.CommitAsync(ct);
            return new(station with { CurrentSessionId = sessionId }, new(sessionId, station.Id, "Active", now, null));
        }
        catch
        {
            await unitOfWork.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task RevokeSelfAsync(Guid id, CancellationToken ct)
    {
        var authorization = await FindValidAsync(id, ct)
            ?? throw new GateDeviceException("DEVICE_UNAUTHORIZED", "裝置尚未授權或授權已失效。", 401);
        await unitOfWork.BeginAsync(ct);
        try
        {
            var station = RequireActive(await repository.FindStationAsync(authorization.Station.Code, true, ct));
            if (station.CurrentSessionId != id)
                throw new GateDeviceException("DEVICE_UNAUTHORIZED", "裝置授權已被取代。", 401);
            // This endpoint is not an Admin operation; do not fabricate an Admin ID.
            await repository.RevokeAsync(station, "DEVICE_SELF", clock.Now(), ct);
            await unitOfWork.CommitAsync(ct);
        }
        catch
        {
            await unitOfWork.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private string Resolve(string code) => settings.ResolveStation(code)
        ?? throw new GateDeviceException("PAIRING_CODE_INVALID", "配對碼錯誤或裝置配對尚未啟用。", 400);

    private static GateStation RequireActive(GateStation? station) => station?.IsActive == true
        ? station : throw new GateDeviceException("GATE_STATION_UNAVAILABLE", "站點未設定或已停用，請聯絡管理人員。", 403);
}
