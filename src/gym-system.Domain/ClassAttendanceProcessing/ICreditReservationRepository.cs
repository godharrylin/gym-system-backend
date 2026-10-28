namespace gym_system.Domain.ClassAttendanceProcessing;

/// <summary>Every operation participates in the caller's SQL transaction; reads used for decisions lock rows.</summary>
public interface ICreditReservationRepository
{
    Task<CreditPassCapacity?> LockPassAsync(string studentId, int passSn, CancellationToken ct);
    Task<int?> FindPassSnAsync(long reservationSn, string studentId, CancellationToken ct);
    Task<bool> AttendanceBelongsToAsync(int attendanceSn, string studentId, CancellationToken ct);
    Task<CreditReservation?> FindByKeyAsync(string key, CancellationToken ct);
    Task<CreditReservation?> FindByIdAsync(long reservationSn, CancellationToken ct);
    Task<IReadOnlyList<CreditReservation>> GetByAttendanceAsync(int attendanceSn, CancellationToken ct);
    Task<IReadOnlyList<CreditReservation>> GetByPassAsync(int passSn, CancellationToken ct);
    Task<CreditReservation> InsertAsync(CreditReservation reservation, CancellationToken ct);
    Task<CreditReservation> UpdateAsync(CreditReservation original, CreditReservation replacement, CancellationToken ct);
}

public sealed record CreditPassCapacity(
    int PassSn, string OwnerId, string ValidStatus, string PaymentState, string TicketType,
    int CreditsRemaining, DateTime? ValidStartDate, DateTime? ValidEndDate, string PlanCode);
