using System.ComponentModel.DataAnnotations;
using gym_system.Api.Devices;
using gym_system.Application.GateDevicesUseCase;
using gym_system.Application.GymAccessUseCase;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;

namespace gym_system.Api.Controllers;

[ApiController]
[Route("api/v1/gate-devices/passage")]
[DisableCors]
[RequestSizeLimit(4096)]
[Authorize(AuthenticationSchemes = GateDeviceAuthentication.Scheme)]
public sealed class GatePassagesController(
    GateDeviceService devices, GatePassageService passages) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Execute(GatePassageRequest request, CancellationToken ct)
    {
        var sessionId = GateDeviceAuthentication.GetSessionId(User);
        if (sessionId is null) return Unauthorized(Error("DEVICE_UNAUTHORIZED", "裝置未授權。"));

        // Re-check DB immediately before eligibility/dispatch; the protected Cookie alone is insufficient.
        var device = await devices.FindValidAsync(sessionId.Value, ct);
        if (device is null) return Unauthorized(Error("DEVICE_UNAUTHORIZED", "裝置授權已失效或被取代。"));

        var result = await passages.ExecuteAsync(device, request.RequestId, request.Phone, ct);
        var body = new
        {
            result.RequestId, result.Outcome, result.Code, result.Message,
            Direction = result.Direction.ToString(), result.UserId,
            result.IsSimulated, result.Replayed, result.Succeeded,
            TraceId = HttpContext.TraceIdentifier
        };
        if (result.Succeeded) return Ok(body);
        var status = result.Code switch
        {
            "GYM_PHONE_REQUIRED" or "GYM_PHONE_INVALID" or "GATE_REQUEST_ID_REQUIRED" => 400,
            "DEVICE_BUSY" => 409,
            "CONTROL_COMMAND_TIMEOUT" or "REQUEST_CANCELLED_RESULT_UNKNOWN" or
                "DUPLICATE_REQUEST_RESULT_UNCONFIRMED" => 504,
            "CONTROL_SERVICE_HTTP_ERROR" or "CONTROL_SERVICE_UNAVAILABLE" or
                "CONTROL_INVALID_RESPONSE" or "CONTROLLER_REJECTED" => 502,
            _ => 403
        };
        return StatusCode(status, body);
    }

    private object Error(string code, string message) => new
    { Code = code, Message = message, TraceId = HttpContext.TraceIdentifier };
}

public sealed class GatePassageRequest
{
    public Guid RequestId { get; set; }
    [Required, StringLength(30)] public string Phone { get; set; } = "";
}
