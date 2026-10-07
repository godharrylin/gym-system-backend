namespace gym_system.Application.DevicesUseCase;

public sealed class SendDeviceTestCommandHandler(
    ITurnstileCommandClient client, TurnstileSettings settings, DeviceTestCommandGate gate)
{
    public async Task Handle(string? direction, CancellationToken ct)
    {
        var doorNo = direction switch
        {
            "Entry" => settings.EntryDoorNo,
            "Exit" => settings.ExitDoorNo,
            _ => throw new DeviceCommandException("INVALID_DEVICE_DIRECTION", "只接受 Entry 或 Exit 操作方向。", 400)
        };
        if (!settings.DeviceTestEnabled)
            throw new DeviceCommandException("DEVICE_TEST_DISABLED", "Device Test 尚未開放。", 403);
        ct.ThrowIfCancellationRequested();
        if (!gate.TryEnter())
            throw new DeviceCommandException("DEVICE_BUSY", "控制器正在處理另一個測試請求，未發送此次指令。", 409);
        try
        {
            await client.OpenDoorAsync(settings.ControllerSN, doorNo, ct);
        }
        finally { gate.Exit(); }
    }
}
