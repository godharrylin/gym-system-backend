using System.ComponentModel.DataAnnotations;
using gym_system.Api.Devices;
using gym_system.Application.GateDevicesUseCase;
using gym_system.Domain.Entities.Devices;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace gym_system.Api.Controllers;

[ApiController]
[Route("api/v1/gate-devices")]
[DisableCors]
[RequestSizeLimit(8192)]
public sealed class GateDevicesController(
    GateDeviceService devices, GatePairingPreviewProtector previews) : ControllerBase
{
    [HttpPost("pairing-preview")]
    [AllowAnonymous]
    [EnableRateLimiting("GatePairing")]
    public Task<IActionResult> Preview(PairingCodeRequest request, CancellationToken ct) => Execute(async () =>
    {
        var station = await devices.PreviewAsync(request.Code, ct);
        var browser = await HttpContext.AuthenticateAsync(GateDeviceAuthentication.Scheme);
        return Ok(new
        {
            stationCode = station.Code, stationName = station.Name, direction = station.Direction.ToString(),
            requiresReplacement = station.CurrentSessionId is not null &&
                station.CurrentSessionId != GateDeviceAuthentication.GetSessionId(browser.Principal),
            previewToken = previews.Create(station.Code, station.CurrentSessionId)
        });
    });

    [HttpPost("pair")]
    [AllowAnonymous]
    [EnableRateLimiting("GatePairing")]
    public Task<IActionResult> Pair(PairRequest request, CancellationToken ct) => Execute(async () =>
    {
        var preview = previews.Read(request.PreviewToken)
            ?? throw new GateDeviceException("PAIRING_PREVIEW_EXPIRED", "配對預覽已失效，請重新輸入配對碼。", 409);
        var browser = await HttpContext.AuthenticateAsync(GateDeviceAuthentication.Scheme);
        var authorization = await devices.PairAsync(request.Code, preview.StationCode,
            preview.ExpectedSessionId, GateDeviceAuthentication.GetSessionId(browser.Principal), ct);
        await GateDeviceAuthentication.SignInAsync(HttpContext, authorization.Session.Id);
        return Ok(View(authorization));
    });

    [HttpGet("session")]
    [Authorize(AuthenticationSchemes = GateDeviceAuthentication.Scheme)]
    public Task<IActionResult> Session(CancellationToken ct) => Execute(async () =>
    {
        var id = GateDeviceAuthentication.GetSessionId(User);
        var authorization = id is null ? null : await devices.FindValidAsync(id.Value, ct);
        return authorization is null ? Unauthorized() : Ok(View(authorization));
    });

    [HttpPost("revoke")]
    [Authorize(AuthenticationSchemes = GateDeviceAuthentication.Scheme)]
    public Task<IActionResult> Revoke(CancellationToken ct) => Execute(async () =>
    {
        var id = GateDeviceAuthentication.GetSessionId(User)
            ?? throw new GateDeviceException("DEVICE_UNAUTHORIZED", "裝置未授權。", 401);
        await devices.RevokeSelfAsync(id, ct);
        await HttpContext.SignOutAsync(GateDeviceAuthentication.Scheme);
        return NoContent();
    });


    private static object View(GateDeviceAuthorization authorization) => new
    {
        stationCode = authorization.Station.Code, stationName = authorization.Station.Name,
        direction = authorization.Station.Direction.ToString(),
        authorizationExpiresAt = (string?)null
    };

    private async Task<IActionResult> Execute(Func<Task<IActionResult>> action)
    {
        try { return await action(); }
        catch (GateDeviceException error)
        { return StatusCode(error.StatusCode, new { error.Code, error.Message, traceId = HttpContext.TraceIdentifier }); }
    }
}

public sealed class PairingCodeRequest
{
    [Required, StringLength(128)] public string Code { get; set; } = "";
}

public sealed class PairRequest
{
    [Required, StringLength(128)] public string Code { get; set; } = "";
    [Required, StringLength(4096)] public string PreviewToken { get; set; } = "";
}

