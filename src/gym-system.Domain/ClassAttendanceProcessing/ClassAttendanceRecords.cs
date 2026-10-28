namespace gym_system.Domain.ClassAttendanceProcessing;

public sealed record StudentClassAttendance(int AttendanceSn, string UserId, int SessionSn,
    string Status, string? Eligibility, string? PassId, DateTime CreatedAt, DateTime UpdatedAt,
    DateTime? CheckInAt = null, DateTime? CheckOutAt = null, string? Remark = null)
{
    public bool ConfirmedEligible => Status is "CheckIn" or "CheckOut" && Eligibility == "Eligible";
    public void Validate()
    {
        if (AttendanceSn < 0 || SessionSn <= 0 || string.IsNullOrWhiteSpace(UserId) || UserId.Length > 50)
            throw new ArgumentException("Invalid attendance identity.");
        if (Status is not ("Pending" or "CheckIn" or "CheckOut" or "Cancelled" or "NeedsReview")
            || Eligibility is not (null or "Eligible" or "Ineligible"))
            throw new ArgumentException("Unknown attendance state.");
        if ((Eligibility == "Eligible" && string.IsNullOrWhiteSpace(PassId)) || PassId?.Length > 50
            || UpdatedAt < CreatedAt)
            throw new ArgumentException("Attendance ticket/time snapshot is inconsistent.");
    }
}

public sealed record AdmissionTicket(int PassSn, string PassId, string OwnerId, string PlanCode,
    string Type, string Status, string PaymentState, int? CreditsRemaining,
    DateTime? StartDate, DateTime? EndDate, DateTime? PaidAt, DateTime? ChangedAt, DateTime? EndedAt, string? EndReason)
{
    public bool UsableAt(DateTime at) => Status == "Active" && PaymentState == "Paid"
        && PaidAt is DateTime paid && paid <= at
        && (Type == "M_PASS" || (Type == "PACK" && CreditsRemaining > 0))
        && (PlanCode == "SINGLE" || (StartDate is not null && EndDate is not null))
        && (StartDate is null || StartDate.Value.Date <= at.Date)
        && (EndDate is null || EndDate.Value.Date >= at.Date);
}

public sealed record ClassGateContext(int AttendanceSn, long? ReservationSn, string Kind,
    bool CreatedPending, bool CreatedReservation)
{
    public void Validate()
    {
        if (AttendanceSn <= 0 || ReservationSn is <= 0
            || Kind is not ("RegularEntry" or "Return" or "PreEntry")
            || (CreatedReservation && ReservationSn is null)
            || (Kind == "Return" && (CreatedPending || CreatedReservation)))
            throw new ArgumentException("Invalid class admission context.");
    }
}
public sealed record ClassGateEvent(Guid? RequestId, int Direction, string? IdentityKind,
    string? Outcome, DateTime RequestedAt, DateTime? RespondedAt, bool AffectsPresence, string? ResultCode);

public sealed record ClassWorkItem(long ItemSn, long JobSn, string UserId, int? AttendanceSn, string State, int Attempts);
public sealed record ClassWorkJob(long JobSn, int SessionSn, string Phase, string State,
    DateTime StartAt, DateTime EndAt, DateTime ScheduledAt, Guid? LeaseToken, DateTime? LeaseExpiresAt);

public interface IClassAttendanceRepository
{
    Task LockCalendarAsync(CancellationToken ct);
    Task<IReadOnlyList<CourseSessionWindow>> GetSessionsAsync(DateOnly day, CancellationToken ct);
    Task<CourseSessionWindow?> LockSessionAsync(int sessionSn, CancellationToken ct);
    Task<IReadOnlyList<StudentClassAttendance>> GetAttendancesAsync(string userId, DateOnly? day, CancellationToken ct);
    Task<StudentClassAttendance?> FindAttendanceAsync(int attendanceSn, CancellationToken ct);
    Task<StudentClassAttendance> InsertAttendanceAsync(StudentClassAttendance value, CancellationToken ct);
    Task<StudentClassAttendance> UpdateAttendanceAsync(StudentClassAttendance original, StudentClassAttendance value, CancellationToken ct);
    Task<AdmissionTicket?> FindTicketAsync(string userId, string passId, CancellationToken ct);
    Task<IReadOnlyList<ClassGateEvent>> GetEventsAsync(string userId, DateOnly day, CancellationToken ct);
    Task BindRequestAsync(Guid requestId, string userId, ClassGateContext context, CancellationToken ct);
    Task<ClassGateContext?> GetRequestContextAsync(Guid requestId, string userId, CancellationToken ct);
    Task EnsureJobsAsync(CourseSessionWindow course, DateTime now, CancellationToken ct);
    Task<long?> GetPriorUnsettledAsync(string userId, DateTime classStart, CancellationToken ct);
    Task<IReadOnlyList<string>> GetCandidatesAsync(int sessionSn, DateOnly day, CancellationToken ct, bool includeGateHistory = true);
}
