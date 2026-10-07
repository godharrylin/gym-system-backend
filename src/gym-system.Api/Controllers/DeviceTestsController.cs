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
        bool? simulated = null;
        try
        {
            var result = await statusHandler.Handle(ct);
            simulated = result.IsSimulated;
            outcome = result.Code;
            if (!result.Succeeded) return ResultError(result, traceId);
            var snapshot = result.Snapshot ?? throw new InvalidOperationException("Successful status result requires a snapshot.");
            outcome = "QuerySucceeded";
            return Ok(new
            {
                Status = outcome, snapshot.ControllerSN, snapshot.ControllerTime, snapshot.DoorStates,
                result.Succeeded, result.IsSimulated, result.Code, Outcome = result.Outcome.ToString(),
                CheckedAt = DateTimeOffset.UtcNow,
                result.Message, TraceId = traceId
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
            logger.LogInformation("Device status Operator={OperatorId} Outcome={Outcome} IsSimulated={IsSimulated} ElapsedMs={ElapsedMs} TraceId={TraceId} ActivityId={ActivityId}",
                operatorId, outcome, simulated, elapsed.ElapsedMilliseconds, traceId, Activity.Current?.Id);
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
        bool? simulated = null;
        try
        {
            var result = await handler.Handle(request.Direction, ct);
            simulated = result.IsSimulated;
            outcome = result.Code;
            if (!result.Succeeded) return ResultError(result, traceId);
            outcome = "CommandAccepted";
            return Ok(new
            {
                Status = outcome, request.Direction,
                result.Succeeded, result.IsSimulated, result.Code, Outcome = result.Outcome.ToString(),
                result.Message, TraceId = traceId
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
            logger.LogInformation("Device test Operator={OperatorId} Direction={Direction} Outcome={Outcome} IsSimulated={IsSimulated} ElapsedMs={ElapsedMs} TraceId={TraceId} ActivityId={ActivityId}",
                operatorId, request.Direction, outcome, simulated, elapsed.ElapsedMilliseconds, traceId, Activity.Current?.Id);
        }
    }

    private ObjectResult ResultError(TurnstileCommandResult result, string traceId)
    {
        var status = result.Code switch
        {
            "CONTROL_COMMAND_TIMEOUT" => 504,
            "INVALID_DEVICE_TARGET" => 400,
            _ => 502
        };
        return StatusCode(status, new ApiErrorResponse
        {
            Code = result.Code, Message = result.Message, TraceId = traceId,
            Succeeded = result.Succeeded, IsSimulated = result.IsSimulated, Outcome = result.Outcome.ToString()
        });
    }
}
