namespace gym_system.Application.DevicesUseCase;

public sealed class GetDeviceTestStatusHandler(
    ITurnstileCommandClient client, TurnstileSettings settings, DeviceTestCommandGate gate)
{
    public async Task<TurnstileCommandResult> Handle(CancellationToken ct)
    {
        if (!settings.DeviceTestEnabled)
            throw new DeviceCommandException("DEVICE_TEST_DISABLED", "Device Test 尚未開放。", 403);
        ct.ThrowIfCancellationRequested();
        if (!gate.TryEnter())
            throw new DeviceCommandException("DEVICE_BUSY", "控制器正在處理另一個測試請求，未發送此次查詢。", 409);
        try { return await client.QueryStatusAsync(settings.ControllerSN, ct); }
        finally { gate.Exit(); }
    }
}
