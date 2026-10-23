using System.Text.Json;
using gym_system.Application.DevicesUseCase;
using gym_system.Domain.Entities.Devices;
using gym_system.Domain.Enums;
using gym_system.Domain.Repositories;
using Microsoft.Extensions.Logging;
using gym_system.Application.ClassAttendanceProcessing;

namespace gym_system.Application.GymAccessUseCase;

public sealed record GatePassageResult(
    Guid RequestId, string Outcome, string Code, string Message, GateDirection Direction,
    string? UserId = null, bool IsSimulated = false, bool Replayed = false)
{
    public bool Succeeded => Outcome == "Succeeded";
}

public sealed class GatePassageService(
    IGymAccessEligibilityService eligibility,
    IGateDeviceRepository devices,
    IGatePassageRepository passages,
    IStudentProfileRepository profiles,
    IUnitOfWork unitOfWork,
    IClock clock,
    ITurnstileCommandClient turnstile,
    TurnstileSettings turnstileSettings,
    DeviceTestCommandGate commandGate,
    ILogger<GatePassageService> logger,
    IClassAdmissionService? classes = null)
{
    public async Task<GatePassageResult> ExecuteAsync(
        GateDeviceAuthorization device, Guid requestId, string? phone, CancellationToken ct)
    {
        if (requestId == Guid.Empty)
            return RejectWithoutRecord(requestId, device, "GATE_REQUEST_ID_REQUIRED", "缺少有效的請求識別碼。");

        var prior = await passages.FindByRequestIdAsync(requestId, ct);
        if (prior is not null) return Replay(prior);

        var check = device.Station.Direction == GateDirection.Entry
            ? await eligibility.CheckEntryAsync(phone, ct)
            : await eligibility.CheckExitAsync(phone, ct);
        var identity = Identity(check);
        var roles = JsonSerializer.Serialize(check.ActiveRoles.Select(x => x.ToString()));
        ClassPreparation? preparation = null;
        bool created;
        if (classes is null)
            created = await passages.TryCreateAsync(requestId, device, check.UserId, identity, roles, check.EvaluatedAt, ct);
        else
        {
            await unitOfWork.BeginAsync(ct);
            try
            {
                // Calendar coordination precedes Requested insertion: A cannot deadlock on an
                // uncommitted request while its sender waits for the same calendar lock.
                await classes.LockAsync(ct);
                created = await passages.TryCreateAsync(requestId, device, check.UserId, identity, roles, clock.Now(), ct);
                if (created && check.RequiresClassAndReservationCheck && device.Station.Direction == GateDirection.Entry)
                    preparation = await classes.PrepareEntryAsync(requestId, check.UserId!, ct);
                await unitOfWork.CommitAsync(ct);
            }
            catch (InvalidOperationException error) when (check.RequiresClassAndReservationCheck)
            {
                await unitOfWork.RollbackAsync(CancellationToken.None);
                logger.LogWarning(error, "Student class preparation rejected. RequestId={RequestId}", requestId);
                // Roll back provisional lifecycle/attendance/holds together. Retain a separate denial attempt.
                created = await passages.TryCreateAsync(requestId, device, check.UserId, identity, roles, clock.Now(), ct);
                if (!created)
                    return Replay(await passages.FindByRequestIdAsync(requestId, ct)
                        ?? throw new InvalidOperationException("Duplicate gate request was not readable."));
                return await FinishAsync(device, check, requestId, "Denied", "CLASS_PREPARATION_REJECTED",
                    "課程或額度狀態已變更，請洽工作人員。", false, false, false, clock.Now(), null, ct);
            }
            catch
            {
                await unitOfWork.RollbackAsync(CancellationToken.None);
                throw;
            }
        }
        if (!created)
            return Replay(await passages.FindByRequestIdAsync(requestId, ct)
                ?? throw new InvalidOperationException("Duplicate gate request was not readable after the unique conflict."));

        if (!check.BasicEligibilityPassed)
            return await FinishAsync(device, check, requestId, "Denied", check.Code, check.Message,
                false, false, false, check.EvaluatedAt, null, ct);

        // Stage 03/04 must establish class-window permission and reserve the student's credit.
        // Never turn stage-01's basic ticket check into door authorization.
        if (device.Station.Direction == GateDirection.Entry && check.RequiresClassAndReservationCheck && classes is null)
            return await FinishAsync(device, check, requestId, "Denied", "CLASS_RESERVATION_NOT_IMPLEMENTED",
                "學生進場仍需完成課程時段與額度占用驗證，目前不開門。", false, false, false,
                check.EvaluatedAt, "Awaiting stage 03/04 class/reservation authorization.", ct);

        if (preparation is { Allowed: false })
            return await FinishAsync(device, check, requestId, "Denied", preparation.Code, preparation.Message,
                false, false, false, clock.Now(), null, ct);

        if (!commandGate.TryEnter())
            return await FinishAsync(device, check, requestId, "Failed", "DEVICE_BUSY",
                "三叉機正在處理另一筆指令，請稍後再試。", false, false, false,
                clock.Now(), null, ct);

        try
        {
            // Revalidate immediately before dispatch. Never hold a database transaction over the network call.
            var currentDevice = await devices.FindAuthorizationAsync(device.Session.Id, ct);
            if (currentDevice?.IsValidAt(clock.Now()) != true)
                return await FinishAsync(device, check, requestId, "Denied", "DEVICE_AUTHORIZATION_CHANGED",
                    "裝置授權已失效或被其他裝置取代，目前未送出開門指令。", false, false, false,
                    clock.Now(), null, ct);
            var result = await turnstile.OpenDoorAsync(turnstileSettings.ControllerSN,
                device.Station.Direction == GateDirection.Entry
                    ? turnstileSettings.EntryDoorNo : turnstileSettings.ExitDoorNo, ct);
            var respondedAt = clock.Now(); // Confirmed business event time is receipt of the vendor success response.
            var affectsPresence = result.Outcome == TurnstileOutcome.Succeeded && !result.IsSimulated;
            return await FinishAsync(device, check, requestId, result.Outcome.ToString(), result.Code,
                result.Message, result.Succeeded, result.IsSimulated, affectsPresence, respondedAt, null,
                CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            var at = clock.Now();
            await FinishAsync(device, check, requestId, "Unknown", "REQUEST_CANCELLED_RESULT_UNKNOWN",
                "請求已中止，無法確認開門結果，請勿立即重送。", false, false, false, at,
                "Client/server cancellation after dispatch; result unknown.", CancellationToken.None);
            throw;
        }
        finally { commandGate.Exit(); }
    }

    private async Task<GatePassageResult> FinishAsync(
        GateDeviceAuthorization device, GymAccessEligibilityResult check, Guid requestId,
        string outcome, string code, string message, bool success, bool simulated,
        bool affectsPresence, DateTime respondedAt, string? remark, CancellationToken ct)
    {
        await unitOfWork.BeginAsync(ct);
        try
        {
            if (classes is not null)
            {
                await classes.LockAsync(ct);
                if (check.UserId is not null && (device.Station.Direction == GateDirection.Exit
                    || check.IdentityKind == GymAccessIdentityKind.Student))
                {
                    var completion = await classes.CompleteAsync(requestId, check.UserId,
                        device.Station.Direction, outcome, simulated, respondedAt, ct);
                    code = completion.Code ?? code;
                    message = completion.Message ?? message;
                }
            }
            if (!await passages.CompleteAsync(requestId, outcome, code, success, simulated,
                    affectsPresence, respondedAt, remark, ct))
                throw new InvalidOperationException("Gate operation no longer has a Requested row to complete.");
            if (affectsPresence && device.Station.Direction == GateDirection.Entry &&
                check.IdentityKind == GymAccessIdentityKind.Student && check.UserId is not null &&
                !await profiles.UpdateLastVisitAsync(check.UserId, respondedAt, ct))
                throw new InvalidOperationException("Student profile visit timestamp could not be updated.");
            await unitOfWork.CommitAsync(ct);
        }
        catch
        {
            await unitOfWork.RollbackAsync(CancellationToken.None);
            logger.LogCritical("Gate result persistence failed. RequestId={RequestId} VendorOutcome={Outcome}", requestId, outcome);
            throw;
        }
        return new(requestId, outcome, code, message, device.Station.Direction, check.UserId, simulated);
    }

    private static GatePassageResult Replay(GatePassageRecord record) => record.Outcome switch
    {
        "Succeeded" => new(record.RequestId, record.Outcome, record.ResultCode,
            "此請求已完成；未再次發送開門指令。", record.Direction, record.UserId,
            record.IsSimulated == true, true),
        "Failed" or "Denied" => new(record.RequestId, record.Outcome, record.ResultCode,
            "此請求已處理；未再次發送開門指令。", record.Direction, record.UserId,
            record.IsSimulated == true, true),
        _ => new(record.RequestId, "Unknown", "DUPLICATE_REQUEST_RESULT_UNCONFIRMED",
            "相同請求已存在且結果未確認，請勿重送開門指令。", record.Direction,
            record.UserId, record.IsSimulated == true, true)
    };

    private static string Identity(GymAccessEligibilityResult check)
    {
        if (check.IdentityKind == GymAccessIdentityKind.Staff) return "Staff";
        if (check.IdentityKind == GymAccessIdentityKind.Student) return "Student";
        if (check.ActiveRoles.Any(x => x is UserRoleCode.Admin or UserRoleCode.Staff or UserRoleCode.Instructor)) return "Staff";
        if (check.ActiveRoles.Contains(UserRoleCode.Student)) return "Student";
        return check.IdentityKind.ToString();
    }

    private static GatePassageResult RejectWithoutRecord(Guid id, GateDeviceAuthorization device, string code, string message) =>
        new(id, "Denied", code, message, device.Station.Direction);
}

