using System.Net.Http.Json;
using System.Text.Json;
using gym_system.Application.DevicesUseCase;

namespace gym_system.Infrastructures.Devices;

public sealed class CloudServerTurnstileClient(HttpClient http, TurnstileSettings settings) : ITurnstileCommandClient
{
    private static long nextId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    public async Task OpenDoorAsync(int controllerSN, int doorNo, CancellationToken ct)
    {
        var id = Interlocked.Increment(ref nextId);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));
        try
        {
            // Explicit names preserve the vendor's case-sensitive JSON-RPC fields.
            var command = new
            {
                jsonrpc = "2.0", method = "RemoteOpenDoor",
                @params = new[] { new { ControllerSN = controllerSN, DoorNO = doorNo } }, id
            };
            using var response = await http.PostAsJsonAsync("", command, new JsonSerializerOptions(), timeout.Token);
            if (!response.IsSuccessStatusCode)
                throw new DeviceCommandException("CONTROL_SERVICE_HTTP_ERROR", "控制服務回傳 HTTP 錯誤，無法確認開門結果。", 502);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw InvalidResponse();
            if (root.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null)
                throw new DeviceCommandException("CONTROLLER_REJECTED", "控制服務回報指令錯誤。", 502);
            if (!root.TryGetProperty("jsonrpc", out var version) || version.GetString() != "2.0"
                || !root.TryGetProperty("id", out var replyId) || !replyId.TryGetInt64(out var value) || value != id
                || !root.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Object
                || !result.TryGetProperty("success", out var success)
                || success.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw InvalidResponse();
            if (!success.GetBoolean())
                throw new DeviceCommandException("CONTROLLER_REJECTED", "控制器回報開門指令失敗。", 502);
            if (!result.TryGetProperty("code", out var code) || !code.TryGetInt32(out var status) || status != 1
                || !result.TryGetProperty("ControllerSN", out var sn) || !sn.TryGetInt32(out var returnedSN) || returnedSN != controllerSN)
                throw InvalidResponse();
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new DeviceCommandException("CONTROL_COMMAND_TIMEOUT", "等待控制器回應逾時，結果不明。請先確認設備狀態，勿立即重送。", 504);
        }
        catch (HttpRequestException)
        {
            throw new DeviceCommandException("CONTROL_SERVICE_UNAVAILABLE", "無法完成與控制服務的連線，開門結果未確認。請先確認設備狀態。", 502);
        }
        catch (JsonException) { throw InvalidResponse(); }
        catch (InvalidOperationException) { throw InvalidResponse(); }
        // No automatic retry: a lost response does not prove that the relay was not activated.
    }

    private static DeviceCommandException InvalidResponse() => new(
        "CONTROL_INVALID_RESPONSE", "無法辨識或核對控制器回應，開門結果未確認。", 502);
}
