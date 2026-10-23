using gym_system.Domain.ClassAttendanceProcessing;
using gym_system.Domain.Repositories;

namespace gym_system.Application.ClassAttendanceProcessing;

public sealed record ReserveCreditResult(CreditReservation Reservation, bool Created);

/// <summary>
/// Capacity operations inside a transaction owned by the gate/A caller. No nested begin/commit.
/// The caller first locks the class/job and validates the class window and attendance evidence.
/// </summary>
public sealed class CreditReservationService(ICreditReservationRepository reservations, ITicketPassRepository passes)
{
    public async Task<ReserveCreditResult> ReserveAsync(string studentId, int attendanceSn, int passSn,
        string operationKey, DateTime at, Guid? gateRequestId = null, long? processingItemSn = null,
        long? previousReservationSn = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(studentId);
        var candidate = CreditReservation.Reserve(attendanceSn, passSn, operationKey, at,
            gateRequestId, processingItemSn, previousReservationSn);

        await passes.LockOwnerAsync(studentId, ct);
        var pass = await reservations.LockPassAsync(studentId, passSn, ct)
            ?? throw new InvalidOperationException("占用票券不存在或不屬於此學生。");
        if (!await reservations.AttendanceBelongsToAsync(attendanceSn, studentId, ct))
            throw new InvalidOperationException("出席紀錄不存在或不屬於此學生。");

        var existing = await reservations.FindByKeyAsync(operationKey, ct);
        if (existing is not null)
        {
            if (existing.AttendanceSn != attendanceSn || existing.PassSn != passSn
                || existing.GateRequestId != gateRequestId || existing.ProcessingItemSn != processingItemSn
                || existing.PreviousReservationSn != previousReservationSn)
                throw new InvalidOperationException("相同冪等鍵不能用於不同占用內容。");
            // Returning history does not grant entry. In particular, Released must not be revived.
            return new(existing, false);
        }

        var attendanceHistory = await reservations.GetByAttendanceAsync(attendanceSn, ct);
        if (attendanceHistory.Any(x => x.HoldsCredit || x.State == CreditReservationState.Consumed))
            throw new InvalidOperationException("此出席已有有效占用或已結算，不可重複占用。");
        if (previousReservationSn is long previousId)
        {
            var previous = attendanceHistory.SingleOrDefault(x => x.ReservationSn == previousId);
            if (previous?.State != CreditReservationState.Released)
                throw new InvalidOperationException("前一筆必須是同一出席的已釋放占用。");
        }

        ValidatePass(pass, at);

        var held = (await reservations.GetByPassAsync(passSn, ct))
            .Where(x => x.HoldsCredit).Sum(x => (long)x.ReservedCredits);
        if (pass.CreditsRemaining - held < candidate.ReservedCredits)
            throw new InvalidOperationException("票券可占用堂數不足。");
        return new(await reservations.InsertAsync(candidate, ct), true);
    }

    public Task<CreditReservation> FixAsync(string studentId, long reservationSn, DateTime at, CancellationToken ct = default)
        => ChangeAsync(studentId, reservationSn, x => x.Fix(at), ct, at);

    public Task<CreditReservation> ReleaseAsync(string studentId, long reservationSn, DateTime at,
        string code, string reason, CancellationToken ct = default)
        => ChangeAsync(studentId, reservationSn, x => x.Release(at, code, reason), ct);

    public Task<CreditReservation> RequireReviewAsync(string studentId, long reservationSn, DateTime at,
        string code, string reason, CancellationToken ct = default)
        => ChangeAsync(studentId, reservationSn, x => x.RequireReview(at, code, reason), ct);

    private async Task<CreditReservation> ChangeAsync(string studentId, long reservationSn,
        Func<CreditReservation, CreditReservation> change, CancellationToken ct, DateTime? validatePassAt = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(studentId);
        await passes.LockOwnerAsync(studentId, ct);
        // Discovery is not an authorization decision; lock pass before attendance/reservation.
        var passSn = await reservations.FindPassSnAsync(reservationSn, studentId, ct)
            ?? throw new KeyNotFoundException("占用不存在或不屬於此學生。");
        var pass = await reservations.LockPassAsync(studentId, passSn, ct)
            ?? throw new InvalidOperationException("占用票券不存在或不屬於此學生。");
        var original = await reservations.FindByIdAsync(reservationSn, ct)
            ?? throw new KeyNotFoundException("占用不存在。");
        if (original.PassSn != passSn || !await reservations.AttendanceBelongsToAsync(original.AttendanceSn, studentId, ct))
            throw new InvalidOperationException("占用關聯與學生不一致。");
        if (validatePassAt is DateTime at)
        {
            ValidatePass(pass, at);
            var held = (await reservations.GetByPassAsync(passSn, ct))
                .Where(x => x.HoldsCredit).Sum(x => (long)x.ReservedCredits);
            if (held > pass.CreditsRemaining)
                throw new InvalidOperationException("票券額度已改變，不能直接固定占用。");
        }
        var replacement = change(original);
        return await reservations.UpdateAsync(original, replacement, ct);
    }

    private static void ValidatePass(CreditPassCapacity pass, DateTime at)
    {
        var today = DateOnly.FromDateTime(at);
        var single = pass.PlanCode.Equals("SINGLE", StringComparison.OrdinalIgnoreCase);
        if (!pass.ValidStatus.Equals("Active", StringComparison.Ordinal)
            || !pass.PaymentState.Equals("Paid", StringComparison.Ordinal)
            || !pass.TicketType.Equals("PACK", StringComparison.Ordinal)
            || (!single && (pass.ValidStartDate is null || pass.ValidEndDate is null))
            || (pass.ValidStartDate is DateTime start && DateOnly.FromDateTime(start) > today)
            || (pass.ValidEndDate is DateTime end && DateOnly.FromDateTime(end) < today))
            throw new InvalidOperationException("票券目前不符合堂數占用條件。");
    }
}
