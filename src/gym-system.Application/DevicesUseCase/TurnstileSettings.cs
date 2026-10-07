namespace gym_system.Application.DevicesUseCase;

public sealed class TurnstileSettings
{
    public bool DeviceTestEnabled { get; set; }
    public string BaseUrl { get; set; } = "http://localhost:61080/";
    public int ControllerSN { get; set; } = 253260812;
    public int EntryDoorNo { get; set; } = 1;
    public int ExitDoorNo { get; set; } = 2;
    public int TimeoutSeconds { get; set; } = 5;

    public void Validate()
    {
        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != "http" && uri.Scheme != "https")
            || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidOperationException("Turnstile:BaseUrl 必須是有效的 HTTP/HTTPS 位址。");
        if (ControllerSN is < 100000000 or > 999999999
            || EntryDoorNo is < 1 or > 4 || ExitDoorNo is < 1 or > 4
            || EntryDoorNo == ExitDoorNo || TimeoutSeconds is < 1 or > 60)
            throw new InvalidOperationException("Turnstile 控制器序號、進出接點或逾時設定無效。");
    }
}

public sealed class DeviceCommandException(string code, string message, int statusCode) : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
}

public interface ITurnstileCommandClient
{
    Task OpenDoorAsync(int controllerSN, int doorNo, CancellationToken ct);
}

// One process, one configured controller. No queuing and no distributed locking.
public sealed class DeviceTestCommandGate
{
    private int busy;
    public bool TryEnter() => Interlocked.CompareExchange(ref busy, 1, 0) == 0;
    public void Exit() => Interlocked.Exchange(ref busy, 0);
}
