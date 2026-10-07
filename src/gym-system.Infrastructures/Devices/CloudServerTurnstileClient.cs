using System.Net.Http.Json;
using System.Diagnostics;
using System.Text.Json;
using gym_system.Application.DevicesUseCase;
using Microsoft.Extensions.Logging;

namespace gym_system.Infrastructures.Devices;

public sealed class CloudServerTurnstileClient(
    HttpClient http, TurnstileSettings settings, ILogger<CloudServerTurnstileClient> logger) : ITurnstileCommandClient
{
    private static long nextId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    public async Task OpenDoorAsync(int controllerSN, int doorNo, CancellationToken ct)
    {
        await SendAsync(controllerSN, doorNo, "RemoteOpenDoor", ct);
    }

    public async Task<ControllerStatusSnapshot> QueryStatusAsync(int controllerSN, CancellationToken ct)
    {
        var result = await SendAsync(controllerSN, null, "QueryControllerStatus", ct);
        if (!result.TryGetProperty("ControllerInformation", out var info) || info.ValueKind != JsonValueKind.Object
            || !info.TryGetProperty("Time", out var time) || time.ValueKind != JsonValueKind.String
            || !DateTime.TryParseExact(time.GetString(), "yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out _))
            throw InvalidResponse(true);
        var doors = new Dictionary<string, string>();
        for (var number = 1; number <= 4; number++)
        {
            var key = $"Door{number}#";
            if (!info.TryGetProperty(key, out var state)) continue;
            if (state.ValueKind != JsonValueKind.String || state.GetString() is not ("Open" or "Closed"))
                throw InvalidResponse(true);
            doors[key] = state.GetString()!;
        }
        if (doors.Count == 0) throw InvalidResponse(true);
        // Never expose ReturnPacket, LastSixRecords, card numbers or unrelated vendor fields.
        return new(controllerSN, time.GetString()!, doors);
    }

    private async Task<JsonElement> SendAsync(int controllerSN, int? doorNo, string method, CancellationToken ct)
    {
        var query = method == "QueryControllerStatus";
        var id = Interlocked.Increment(ref nextId);
        var activityId = Activity.Current?.Id;
        logger.LogInformation(
            "Turnstile request Method={Method} ControllerSN={ControllerSN} DoorNO={DoorNO} RpcId={RpcId} ActivityId={ActivityId}",
            method, controllerSN, doorNo, id, activityId);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));
        try
        {
            // Explicit names preserve the vendor's case-sensitive JSON-RPC fields.
            object parameters = query ? new { ControllerSN = controllerSN }
                : new { ControllerSN = controllerSN, DoorNO = doorNo!.Value };
            var command = new
            {
                jsonrpc = "2.0", method,
                @params = new[] { parameters }, id
            };
            using var response = await http.PostAsJsonAsync("", command, new JsonSerializerOptions(), timeout.Token);
            logger.LogInformation("Turnstile HTTP response RpcId={RpcId} HttpStatus={HttpStatus} ActivityId={ActivityId}",
                id, (int)response.StatusCode, activityId);
            if (!response.IsSuccessStatusCode)
                throw new DeviceCommandException("CONTROL_SERVICE_HTTP_ERROR", query ? "控制服務回傳 HTTP 錯誤，未能確認控制器狀態。" : "控制服務回傳 HTTP 錯誤，無法確認開門結果。", 502);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            var root = document.RootElement;
            LogVendorResponse(root, id, activityId);
            if (root.ValueKind != JsonValueKind.Object)
                throw InvalidResponse(query);
            if (root.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null)
                throw new DeviceCommandException("CONTROLLER_REJECTED", "控制服務回報指令錯誤。", 502);
            if (!root.TryGetProperty("jsonrpc", out var version) || version.GetString() != "2.0"
                || !root.TryGetProperty("id", out var replyId) || !replyId.TryGetInt64(out var value) || value != id
                || !root.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Object
                || !result.TryGetProperty("success", out var success)
                || success.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw InvalidResponse(query);
            if (!success.GetBoolean())
                throw new DeviceCommandException("CONTROLLER_REJECTED", query ? "控制器狀態查詢失敗，請檢查控制器連線。" : "控制器回報開門指令失敗。", 502);
            if (!result.TryGetProperty("code", out var code) || !code.TryGetInt32(out var status) || status != 1
                || !result.TryGetProperty("ControllerSN", out var sn) || !sn.TryGetInt32(out var returnedSN) || returnedSN != controllerSN)
                throw InvalidResponse(query);
            return result.Clone();
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("Turnstile response timeout RpcId={RpcId} ActivityId={ActivityId}; result unknown, no retry", id, activityId);
            throw new DeviceCommandException("CONTROL_COMMAND_TIMEOUT", query ? "查詢逾時，未能確認控制器連線狀態。" : "等待控制器回應逾時，結果不明。請先確認設備狀態，勿立即重送。", 504);
        }
        catch (HttpRequestException)
        {
            logger.LogWarning("Turnstile transport failure RpcId={RpcId} ActivityId={ActivityId}; result unconfirmed, no retry", id, activityId);
            throw new DeviceCommandException("CONTROL_SERVICE_UNAVAILABLE", query ? "無法連線到控制服務，未能確認控制器狀態。" : "無法完成與控制服務的連線，開門結果未確認。請先確認設備狀態。", 502);
        }
        catch (JsonException)
        {
            logger.LogWarning("Turnstile response is not valid JSON RpcId={RpcId} ActivityId={ActivityId}", id, activityId);
            throw InvalidResponse(query);
        }
        catch (InvalidOperationException)
        {
            logger.LogWarning("Turnstile response field type is invalid RpcId={RpcId} ActivityId={ActivityId}", id, activityId);
            throw InvalidResponse(query);
        }
        // No automatic retry: a lost response does not prove that the relay was not activated.
    }

    private void LogVendorResponse(JsonElement root, long id, string? activityId)
    {
        var result = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("result", out var value)
            ? value : default;
        var error = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error", out var errorValue)
            ? errorValue : default;
        // Only allowlisted scalar diagnostics, never full responses/headers/tokens/card records.
        logger.LogInformation(
            "Turnstile vendor response RpcId={RpcId} ReplyId={ReplyId} ControllerSN={ControllerSN} Success={Success} Code={Code} Msg={Msg} Reason={Reason} ErrorCode={ErrorCode} ErrorMessage={ErrorMessage} ActivityId={ActivityId}",
            id, DiagnosticField(root, "id"), DiagnosticField(result, "ControllerSN"),
            DiagnosticField(result, "success"), DiagnosticField(result, "code"),
            DiagnosticField(result, "msg"), DiagnosticField(result, "Reason"),
            DiagnosticField(error, "code"), DiagnosticField(error, "message"), activityId);
    }

    private static string? DiagnosticField(JsonElement container, string name)
    {
        if (container.ValueKind != JsonValueKind.Object || !container.TryGetProperty(name, out var value))
            return null;
        if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return null;
        if (value.ValueKind is JsonValueKind.Object or JsonValueKind.Array) return "[non-scalar]";
        var text = value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText();
        text = text.Replace('\r', ' ').Replace('\n', ' ');
        return text.Length <= 1024 ? text : text[..1024] + " [truncated]";
    }

    private static DeviceCommandException InvalidResponse(bool query = false) => new(
        "CONTROL_INVALID_RESPONSE", query ? "無法辨識或核對控制器回應，未能確認控制器狀態。" : "無法辨識或核對控制器回應，開門結果未確認。", 502);
}
