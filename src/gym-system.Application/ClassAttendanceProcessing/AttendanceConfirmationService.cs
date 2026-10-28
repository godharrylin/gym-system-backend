using gym_system.Application.TicketsUseCase.Services;
using gym_system.Domain.ClassAttendanceProcessing;
using gym_system.Domain.Enums;
using gym_system.Domain.Repositories;

namespace gym_system.Application.ClassAttendanceProcessing;

public sealed record AttendanceConfirmationResult(string State, int? AttendanceSn,
    string Code, string Message, long? BlockedBy = null);

/// <summary>A only. All writes participate in the coordinator's per-student transaction and lease.</summary>
public sealed class AttendanceConfirmationService(IClassAttendanceRepository attendance,
    ICreditReservationRepository reservations, CreditReservationService credits,
    ITicketPassRepository passes, ICurrentTicketService currentTickets, IStudentProfileRepository profiles,
    IUserRoleRepository roles, IClock clock)
{
    public async Task<AttendanceConfirmationResult> ConfirmAsync(CourseSessionWindow course,
        ClassWorkItem item, CancellationToken ct)
    {
        var now = clock.Now();
        if (now <= course.CutoffAt) throw new InvalidOperationException("A cannot seal the inclusive cutoff before it has passed.");
        if (await profiles.FindByUserIdAsync(item.UserId, ct) is null)
            return new("NeedsReview", null, "STUDENT_PROFILE_MISSING", "缺少學生 profile，不能推定出席或票券不足。");
        await passes.LockOwnerAsync(item.UserId, ct);
        var rows = await attendance.GetAttendancesAsync(item.UserId, DateOnly.FromDateTime(course.StartAt), ct);
        var row = rows.SingleOrDefault(x => x.SessionSn == course.SessionSn);
        if (row?.Status is "CheckIn" or "CheckOut")
        {
            var holds = await reservations.GetByAttendanceAsync(row.AttendanceSn, ct);
            if (row.Eligibility == "Ineligible" && !holds.Any(x => x.HoldsCredit || x.State == CreditReservationState.Consumed))
                return new("Completed", row.AttendanceSn, "ATTENDANCE_INELIGIBLE", "既有無資格留館事實不補結算。");
            var frozen = row.PassId is string passId ? await attendance.FindTicketAsync(item.UserId, passId, ct) : null;
            var fixedHolds = holds.Where(x => x.State is CreditReservationState.Fixed or CreditReservationState.Consumed).ToArray();
            if (row.Eligibility != "Eligible" || frozen is null
                || holds.Any(x => x.State is CreditReservationState.Reserved or CreditReservationState.NeedsReview)
                || (frozen.Type == "PACK" && (fixedHolds.Length != 1 || fixedHolds[0].PassSn != frozen.PassSn))
                || (frozen.Type == "M_PASS" && fixedHolds.Length != 0)
                || frozen.Type is not ("PACK" or "M_PASS"))
                return await Review(row, "CONFIRMED_ATTENDANCE_INCONSISTENT", "固定出席的資格、票券或占用不一致，不能直接宣告完成。", now, ct);
            return new("Completed", row.AttendanceSn, "ATTENDANCE_ALREADY_CONFIRMED", "既有固定出席不重新選票或重占。");
        }
        if (row?.Status == "NeedsReview")
            return new("NeedsReview", row.AttendanceSn, "ATTENDANCE_REVIEW_REQUIRED", "既有出席待確認，不自動覆寫。");
        var history = await attendance.GetEventsAsync(item.UserId, DateOnly.FromDateTime(course.StartAt), ct);
        var presence = ClassPresenceHistory.At(history, course.CutoffAt);
        if (!presence.Known || (row is { Status: "Pending" } && history.Count == 0))
            return await Review(row, presence.Reason ?? "GATE_HISTORY_INCOMPLETE", "原截止歷史不足或結果不明，保留占用待確認。", now, ct);
        if (course.Cancelled || !presence.Inside || !presence.Student)
        {
            if (row is not null)
            {
                if ((await reservations.GetByAttendanceAsync(row.AttendanceSn, ct))
                    .Any(x => x.State is CreditReservationState.Fixed or CreditReservationState.Consumed or CreditReservationState.NeedsReview))
                    return await Review(row, "RESERVATION_STATE_CONFLICT", "未確認出席仍有固定／待確認占用，不能自動取消或隱藏占用。", now, ct);
                await ReleaseTemporaryAsync(row, now, "NOT_PRESENT_AT_CUTOFF", "截止時不具學生在館狀態。", ct);
                row = await attendance.UpdateAttendanceAsync(row, row with { Status = "Cancelled", Eligibility = null,
                    UpdatedAt = now, Remark = "截止時未在館／非學生入場身分，不成立此堂出席。" }, ct);
            }
            return new("Skipped", row?.AttendanceSn, "NOT_PRESENT_AT_CUTOFF", "原截止時未以學生身分在館。");
        }
        var activeRoles = (await roles.GetActiveRolesAsync(item.UserId, ct)).Where(x => x.IsActive).Select(x => x.RoleCode).ToArray();
        if (!activeRoles.Contains(UserRoleCode.Student) || activeRoles.Any(x => x is UserRoleCode.Admin or UserRoleCode.Staff or UserRoleCode.Instructor))
            return await Review(row, "STUDENT_ROLE_CHANGED", "入館後角色狀態已改變，須核對當時身分。", now, ct);
        var dependency = await attendance.GetPriorUnsettledAsync(item.UserId, course.StartAt, ct);
        if (dependency is not null)
            return new("Blocked", row?.AttendanceSn, "PREVIOUS_SETTLEMENT_PENDING", "上一堂未結算，只暫停此學生，不視為無票。", dependency);
        // The current lifecycle service cannot reconstruct past-date mutations. Never backdate it
        // and overwrite today's projection. Missing historical ticket evidence requires review.
        if (now.Date != course.StartAt.Date)
            return await Review(row, "HISTORICAL_TICKET_STATE_UNAVAILABLE", "跨日期補跑缺乏足夠票券歷史，不能以今天方案補原截止資格。", now, ct);
        var prior = row?.PassId is string oldPass ? await attendance.FindTicketAsync(item.UserId, oldPass, ct) : null;
        if (prior is not null && prior.Status is "Cancelled" or "Depleted"
            && (prior.EndedAt is null || prior.EndedAt > course.CutoffAt))
            return await Review(row, "TICKET_CHANGED_AFTER_CUTOFF", "原票在截止後才結束，不能用現在狀態替代原資格。", now, ct);

        var snapshot = await currentTickets.ReconcileAsync(item.UserId, now, "SYSTEM", ct);
        var ticket = snapshot is null ? null : await attendance.FindTicketAsync(item.UserId, snapshot.TicketId, ct);
        if (snapshot is not null && (ticket is null || ticket.PaidAt is null
            || ticket.PaymentState != "Paid" || ticket.Type is not ("PACK" or "M_PASS")))
            return await Review(row, "TICKET_DATA_INCONSISTENT", "目前票券的付款或類型資料不完整，不能視為學生無票。", now, ct);
        if (ticket is { PaidAt: DateTime paid } && paid > course.CutoffAt)
            return await Review(row, "TICKET_PAID_AFTER_CUTOFF", "補跑時的票券在截止後才付款，不能回填過去資格。", now, ct);
        if (row is null)
            row = await attendance.InsertAttendanceAsync(new(0, item.UserId, course.SessionSn, "Pending", null,
                ticket?.PassId, now, now, Remark: "系統依原截止歷史辨識留館／續堂。"), ct);

        var live = (await reservations.GetByAttendanceAsync(row.AttendanceSn, ct)).SingleOrDefault(x => x.HoldsCredit);
        if (live?.State is CreditReservationState.Fixed or CreditReservationState.NeedsReview)
            return await Review(row, "RESERVATION_STATE_CONFLICT", "尚未確認的出席存在固定／待確認占用。", now, ct);
        if (live is not null && (ticket?.UsableAt(course.CutoffAt) != true || ticket.Type != "PACK" || ticket.PassSn != live.PassSn))
        {
            await reservations.UpdateAsync(live, live.Release(now, "A_TICKET_RESELECTED", "截止整理後改用其他票券或確認無可用票券。"), ct);
            live = null;
        }
        if (ticket?.UsableAt(course.CutoffAt) != true)
        {
            row = await attendance.UpdateAttendanceAsync(row, row with { Status = "CheckIn", Eligibility = "Ineligible",
                PassId = null, UpdatedAt = now, Remark = "留館事實：截止時無可用票券；不欠堂、不補結算、不授予回場。" }, ct);
            return new("Completed", row.AttendanceSn, "ATTENDANCE_INELIGIBLE", "保留留館事實，不扣負數、不補結算。");
        }
        if (ticket.Type == "PACK")
        {
            var held = (await reservations.GetByPassAsync(ticket.PassSn, ct)).Where(x => x.HoldsCredit).Sum(x => (long)x.ReservedCredits);
            if (ticket.CreditsRemaining - held < (live is null ? 1 : 0))
            {
                if (live is not null) return await Review(row, "RESERVED_CAPACITY_CHANGED", "既有占用額度被改動，保留待確認。", now, ct);
                row = await attendance.UpdateAttendanceAsync(row, row with { Status = "CheckIn", Eligibility = "Ineligible",
                    PassId = ticket.PassId, UpdatedAt = now, Remark = "留館但額度不足，不欠堂、不補結算。" }, ct);
                return new("Completed", row.AttendanceSn, "ATTENDANCE_INELIGIBLE", "額度不足留館，不授予回場。");
            }
            if (live is null)
                live = (await credits.ReserveAsync(item.UserId, row.AttendanceSn, ticket.PassSn,
                    $"A:{item.ItemSn}:{ticket.PassSn}", now, processingItemSn: item.ItemSn, ct: ct)).Reservation;
            await credits.FixAsync(item.UserId, live.ReservationSn!.Value, now, ct);
        }
        row = await attendance.UpdateAttendanceAsync(row, row with { Status = "CheckIn", Eligibility = "Eligible",
            PassId = ticket.PassId, UpdatedAt = now }, ct);
        return new("Completed", row.AttendanceSn, "ATTENDANCE_CONFIRMED", "依原截止歷史確認出席並固定使用票券。");
    }

    private async Task ReleaseTemporaryAsync(StudentClassAttendance row, DateTime now, string code, string message, CancellationToken ct)
    {
        foreach (var hold in await reservations.GetByAttendanceAsync(row.AttendanceSn, ct))
        {
            if (hold.State is CreditReservationState.Fixed or CreditReservationState.Consumed || hold.ReviewFromState == CreditReservationState.Fixed)
                throw new InvalidOperationException("Unconfirmed attendance has fixed/consumed credits; do not release automatically.");
            if (hold.State == CreditReservationState.Reserved)
                await reservations.UpdateAsync(hold, hold.Release(now, code, message), ct);
        }
    }

    private async Task<AttendanceConfirmationResult> Review(StudentClassAttendance? row, string code, string message, DateTime now, CancellationToken ct)
    {
        if (row is not null)
        {
            foreach (var hold in await reservations.GetByAttendanceAsync(row.AttendanceSn, ct))
                if (hold.State == CreditReservationState.Reserved)
                    await reservations.UpdateAsync(hold, hold.RequireReview(now, code, message), ct);
            if (row.Status is "Pending" or "Cancelled")
                row = await attendance.UpdateAttendanceAsync(row, row with { Status = "NeedsReview", UpdatedAt = now, Remark = message }, ct);
        }
        return new("NeedsReview", row?.AttendanceSn, code, message);
    }
}
