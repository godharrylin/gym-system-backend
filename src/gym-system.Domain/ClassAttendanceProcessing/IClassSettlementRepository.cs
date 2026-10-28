namespace gym_system.Domain.ClassAttendanceProcessing;

public sealed record ClassTicketUsage(int UsageSn, string? PassId, int? AttendanceSn,
    string? Action, string? Source, int? Credits, int? Before, int? After,
    DateTime? BusinessAt, DateTime? RecordedAt, int? ReversedUsageSn);

/// <summary>Caller owns the calendar/session/owner locks and the settlement transaction.</summary>
public interface IClassSettlementRepository
{
    Task<ClassWorkItem?> FindConfirmationAsync(int sessionSn, string userId, CancellationToken ct);
    Task<IReadOnlyList<ClassTicketUsage>> GetUsageAsync(int attendanceSn, CancellationToken ct);
    Task<int> DebitAsync(AdmissionTicket original, int attendanceSn, DateTime businessAt,
        DateTime recordedAt, string source, string operatorId, CancellationToken ct);
}
