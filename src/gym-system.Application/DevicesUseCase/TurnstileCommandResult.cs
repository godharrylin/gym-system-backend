namespace gym_system.Application.DevicesUseCase;

public enum TurnstileOutcome { Succeeded, Failed, Unknown }

// Transport-independent contract. A successful command is not proof of actual passage.
public sealed record TurnstileCommandResult(
    TurnstileOutcome Outcome, bool IsSimulated, string Code, string Message,
    ControllerStatusSnapshot? Snapshot = null)
{
    public bool Succeeded => Outcome == TurnstileOutcome.Succeeded;

    public static TurnstileCommandResult Accepted(bool simulated, ControllerStatusSnapshot? snapshot = null) => new(
        TurnstileOutcome.Succeeded, simulated, snapshot is null ? "COMMAND_ACCEPTED" : "STATUS_QUERY_SUCCEEDED",
        simulated
            ? snapshot is null ? "模擬成功，未控制真實設備。" : "模擬狀態查詢成功，資料不是實際設備狀態。"
            : snapshot is null ? "控制器確認開門指令執行成功；未確認實際通行。" : "本次控制器狀態查詢成功；不代表持續在線或實際通行。",
        snapshot);

    public static TurnstileCommandResult FromFailure(DeviceCommandException error) => new(
        error.Code == "CONTROLLER_REJECTED" ? TurnstileOutcome.Failed : TurnstileOutcome.Unknown,
        false, error.Code, error.Message);

    public static bool IsValidTarget(int controllerSN, int? doorNo = null) =>
        controllerSN is >= 100000000 and <= 999999999 && (doorNo is null || doorNo is >= 1 and <= 4);

    public static TurnstileCommandResult InvalidTarget(bool simulated) => new(
        TurnstileOutcome.Failed, simulated, "INVALID_DEVICE_TARGET", "控制器序號或接點無效，未發送指令。");
}
