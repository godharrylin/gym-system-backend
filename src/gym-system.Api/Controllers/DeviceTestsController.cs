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
    SendDeviceTestCommandHandler handler, GetDeviceTestStatusHandler statusHandler,
    ILogger<DeviceTestsController> logger) : ControllerBase
{
    [HttpGet("status")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> GetStatus(CancellationToken ct)
    {
        var traceId = HttpContext.TraceIdentifier;
        var operatorId = User.FindFirst("sub")?.Value;
        if (string.IsNullOrWhiteSpace(operatorId))
            return StatusCode(403, new ApiErrorResponse
            { Code = "DEVICE_OPERATOR_REQUIRED", Message = "缺少操作者身分。", TraceId = traceId });
        using var scope = logger.BeginScope(new Dictionary<string, object> { ["TraceId"] = traceId });
        var elapsed = Stopwatch.StartNew();
        var outcome = "Unknown";
        try
        {
            var snapshot = await statusHandler.Handle(ct);
            outcome = "QuerySucceeded";
            return Ok(new
            {
                Status = outcome, snapshot.ControllerSN, snapshot.ControllerTime, snapshot.DoorStates,
                CheckedAt = DateTimeOffset.UtcNow,
                Message = "本次控制器狀態查詢成功；不代表持續在線或實際通行。", TraceId = traceId
            });
        }
        catch (DeviceCommandException ex)
        {
            outcome = ex.Code;
            return StatusCode(ex.StatusCode, new ApiErrorResponse
            { Code = ex.Code, Message = ex.Message, TraceId = traceId });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        { outcome = "RequestCancelled_StatusUnknown"; return StatusCode(499); }
        catch (Exception ex)
        {
            outcome = "DEVICE_STATUS_ERROR";
            logger.LogError(ex, "Device status exception. TraceId: {TraceId}", traceId);
            return StatusCode(500, new ApiErrorResponse
            { Code = outcome, Message = "狀態查詢發生錯誤，未能確認控制器狀態。", TraceId = traceId });
        }
        finally
        {
            logger.LogInformation("Device status Operator={OperatorId} Outcome={Outcome} ElapsedMs={ElapsedMs} TraceId={TraceId} ActivityId={ActivityId}",
                operatorId, outcome, elapsed.ElapsedMilliseconds, traceId, Activity.Current?.Id);
        }
    }

    [HttpPost("open-door")]
    public async Task<IActionResult> OpenDoor([FromBody] DeviceTestRequest request, CancellationToken ct)
    {
        var elapsed = Stopwatch.StartNew();
        var traceId = HttpContext.TraceIdentifier;
        var operatorId = User.FindFirst("sub")?.Value;
        using var logScope = logger.BeginScope(new Dictionary<string, object>
        { ["TraceId"] = traceId });
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
            logger.LogInformation("Device test Operator={OperatorId} Direction={Direction} Outcome={Outcome} ElapsedMs={ElapsedMs} TraceId={TraceId} ActivityId={ActivityId}",
                operatorId, request.Direction, outcome, elapsed.ElapsedMilliseconds, traceId, Activity.Current?.Id);
        }
    }
}
