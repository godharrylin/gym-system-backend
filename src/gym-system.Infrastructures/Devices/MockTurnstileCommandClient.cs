using System.Globalization;
using gym_system.Application.DevicesUseCase;
using Microsoft.Extensions.Logging;

namespace gym_system.Infrastructures.Devices;

// No HttpClient dependency: this implementation cannot dispatch a hardware command.
public sealed class MockTurnstileCommandClient(ILogger<MockTurnstileCommandClient> logger) : ITurnstileCommandClient
{
    public Task<TurnstileCommandResult> OpenDoorAsync(int controllerSN, int doorNo, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!TurnstileCommandResult.IsValidTarget(controllerSN, doorNo))
            return Task.FromResult(TurnstileCommandResult.InvalidTarget(true));
        logger.LogInformation("Turnstile Mock command ControllerSN={ControllerSN} DoorNO={DoorNO} IsSimulated=true; no hardware command dispatched", controllerSN, doorNo);
        return Task.FromResult(TurnstileCommandResult.Accepted(true));
    }

    public Task<TurnstileCommandResult> QueryStatusAsync(int controllerSN, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!TurnstileCommandResult.IsValidTarget(controllerSN))
            return Task.FromResult(TurnstileCommandResult.InvalidTarget(true));
        var taipei = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Asia/Taipei"));
        var snapshot = new ControllerStatusSnapshot(controllerSN, taipei.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            new Dictionary<string, string> { ["Door1#"] = "Closed", ["Door2#"] = "Closed", ["Door3#"] = "Closed", ["Door4#"] = "Closed" });
        logger.LogInformation("Turnstile Mock status ControllerSN={ControllerSN} IsSimulated=true; synthetic data only", controllerSN);
        return Task.FromResult(TurnstileCommandResult.Accepted(true, snapshot));
    }
}
