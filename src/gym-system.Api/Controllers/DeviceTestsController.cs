using System.Diagnostics;
using gym_system.Api.Contracts;
using gym_system.Api.Contracts.Devices;
using gym_system.Application.DevicesUseCase;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace gym_system.Api.Controllers;

[ApiController]
[Route("api/v1/device-tests")]
[Authorize(Roles = "Admin")]
public sealed class DeviceTestsController(
    SendDeviceTestCommandHandler handler, ILogger<DeviceTestsController> logger) : ControllerBase
{
    [HttpPost("open-door")]
    public async Task<IActionResult> OpenDoor([FromBody] DeviceTestRequest request, CancellationToken ct)
    {
        var elapsed = Stopwatch.StartNew();
        var traceId = HttpContext.TraceIdentifier;
        var operatorId = User.FindFirst("sub")?.Value;
        // Do not dispatch an authenticated test without an identifiable operator.
        if (string.IsNullOrWhiteSpace(operatorId))
            return StatusCode(403, new ApiErrorResponse
            { Code = "DEVICE_OPERATOR_REQUIRED", Message = "缺少操作者身分。", TraceId = traceId });
        var outcome = "Unknown";
        try
        {
            await handler.Handle(request.Direction, ct);
            outcome = "CommandAccepted";
            return Ok(new
            {
                Status = outcome, request.Direction,
                Message = "控制器確認開門指令執行成功；未確認實際通行。", TraceId = traceId
            });
        }
        catch (DeviceCommandException ex)
        {
            outcome = ex.Code;
            return StatusCode(ex.StatusCode, new ApiErrorResponse
            { Code = ex.Code, Message = ex.Message, TraceId = traceId });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            outcome = "RequestCancelled_ResultUnknown";
            return StatusCode(499);
        }
        catch (Exception ex)
        {
            outcome = "DEVICE_TEST_ERROR";
            logger.LogError(ex, "Device test exception. TraceId: {TraceId}", traceId);
            return StatusCode(500, new ApiErrorResponse
            { Code = outcome, Message = "設備測試發生錯誤，結果未確認。請提供追蹤編號查詢。", TraceId = traceId });
        }
        finally
        {
            logger.LogInformation("Device test Operator={OperatorId} Direction={Direction} Outcome={Outcome} ElapsedMs={ElapsedMs} TraceId={TraceId}",
                operatorId, request.Direction, outcome, elapsed.ElapsedMilliseconds, traceId);
        }
    }
}
