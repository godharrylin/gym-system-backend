using gym_system.Domain.ClassAttendanceProcessing;
using gym_system.Domain.Repositories;

namespace gym_system.Application.ClassAttendanceProcessing;

/// <summary>Shared settlement core. Caller atomically completes the B item in the same transaction.
/// Never starts a transaction or activates a successor. Manual callers must use this core too.</summary>
public sealed class ClassSettlementService(IClassAttendanceRepository attendance,
    ICreditReservationRepository reservations, IClassSettlementRepository settlement,
    ITicketPassRepository passes, IClock clock)
{
    public async Task<AttendanceConfirmationResult> SettleAsync(CourseSessionWindow course,
        ClassWorkItem item, CancellationToken ct, string source = "Auto_Settlement", string operatorId = "SYSTEM")
    {
        if (source is not ("Auto_Settlement" or "Manual") || string.IsNullOrWhiteSpace(operatorId) || operatorId.Length > 50)
            throw new ArgumentException("Invalid settlement source/operator.");
        var now = clock.Now();
        if (now <= course.ReturnUntil) throw new InvalidOperationException("B cannot settle the inclusive return window.");
        await passes.LockOwnerAsync(item.UserId, ct);
        var rows = await attendance.GetAttendancesAsync(item.UserId, DateOnly.FromDateTime(course.StartAt), ct);
        var row = rows.SingleOrDefault(x => x.SessionSn == course.SessionSn);
        if (now.Date != course.StartAt.Date)
            return await Review(row, "CROSS_DAY_SETTLEMENT_REVIEW", "跨日未完成結算，保留原資料交人工確認，不自動補扣。", now, ct);
        var dependency = await attendance.GetPriorUnsettledAsync(item.UserId, course.StartAt, ct);
        if (dependency is not null)
            return new("Blocked", row?.AttendanceSn, "PREVIOUS_SETTLEMENT_PENDING", "上一堂結算尚未完成，依原順序暫停此學生。", dependency);
        var confirmation = await settlement.FindConfirmationAsync(course.SessionSn, item.UserId, ct);
        if (confirmation is null)
            return await Review(row, "ATTENDANCE_CONFIRMATION_MISSING", "找不到原出席確認工作，不推測出席。", now, ct);
        if (confirmation.State is not ("Completed" or "Skipped"))
            return new(confirmation.State == "NeedsReview" ? "NeedsReview" : "Blocked", row?.AttendanceSn,
                "ATTENDANCE_CONFIRMATION_PENDING", "出席確認尚未完成，不推測出席或扣堂。", 
                confirmation.State == "NeedsReview" ? null : confirmation.ItemSn);
        if (row is null)
            return confirmation.State == "Skipped"
                ? new("Skipped", null, "NO_CONFIRMED_ATTENDANCE", "原截止未出席，不扣堂。")
                : new("NeedsReview", null, "CONFIRMED_ATTENDANCE_MISSING", "已完成出席工作但找不到出席資料。");

        // Owner and attendance locks precede usage inspection. No SQL uniqueness rule is assumed.
        var usage = await settlement.GetUsageAsync(row.AttendanceSn, ct);
        var holds = await reservations.GetByAttendanceAsync(row.AttendanceSn, ct);
        if (item.AttendanceSn is int expected && expected != row.AttendanceSn)
            return await Review(row, "SETTLEMENT_ATTENDANCE_MISMATCH", "工作與出席關聯不一致。", now, ct);
        if (row.Status == "Cancelled" || row.Eligibility == "Ineligible")
        {
            if (usage.Count != 0 || holds.Any(x => x.HoldsCredit || x.State == CreditReservationState.Consumed))
                return await Review(row, "INELIGIBLE_SETTLEMENT_CONFLICT", "無資格／取消出席仍有核銷或有效占用。", now, ct);
            return new("Skipped", row.AttendanceSn, "NO_ELIGIBLE_ATTENDANCE", "不欠堂、不補結算。");
        }
        if (!row.ConfirmedEligible || row.PassId is null || course.Cancelled)
            return await Review(row, "SETTLEMENT_ATTENDANCE_REVIEW", "出席狀態不符合正式結算條件。", now, ct);
        var ticket = await attendance.FindTicketAsync(item.UserId, row.PassId, ct);
        if (ticket is null || ticket.PaymentState != "Paid" || ticket.PaidAt is null
            || ticket.PaidAt > course.CutoffAt || ticket.Type is not ("PACK" or "M_PASS"))
            return await Review(row, "SETTLEMENT_TICKET_CONFLICT", "固定票券、付款或類型不完整，不改扣其他票券。", now, ct);
        if (ticket.Type == "M_PASS")
        {
            if (!ticket.UsableAt(course.CutoffAt) || usage.Count != 0
                || holds.Any(x => x.HoldsCredit || x.State == CreditReservationState.Consumed))
                return await Review(row, "MONTHLY_SETTLEMENT_CONFLICT", "月票資料或堂數占用不一致。", now, ct);
            return new("Completed", row.AttendanceSn, "MONTHLY_SETTLED", "月票結算完成，不新增零堂數核銷。");
        }
        var live = holds.Where(x => x.HoldsCredit || x.State == CreditReservationState.Consumed).ToArray();
        if (live.Length != 1 || live[0].PassSn != ticket.PassSn)
            return await Review(row, "SETTLEMENT_RESERVATION_CONFLICT", "固定占用與原出席票券不一致。", now, ct);
        var hold = live[0];
        if (usage.Count != 0)
        {
            var u = usage.Count == 1 ? usage[0] : null;
            if (u is not null && u.Action == "Consume" && u.Source is "Manual" or "Auto_Settlement"
                && u.PassId == ticket.PassId && u.AttendanceSn == row.AttendanceSn && u.Credits == 1
                && u.Before > 0 && u.After == u.Before - 1 && u.RecordedAt is not null
                && u.BusinessAt == course.EndAt && u.ReversedUsageSn is null
                && hold.State == CreditReservationState.Consumed && hold.UsageSn == u.UsageSn)
                return new("Completed", row.AttendanceSn, "SETTLEMENT_ALREADY_CONSUMED", "既有正式核銷與占用一致，不重複扣堂。");
            return await Review(row, "SETTLEMENT_USAGE_CONFLICT", "既有核銷、沖銷或占用關聯不一致，不重扣。", now, ct);
        }
        if (hold.State != CreditReservationState.Fixed || !ticket.UsableAt(course.CutoffAt)
            || (await reservations.GetByPassAsync(ticket.PassSn, ct)).Where(x => x.HoldsCredit)
                .Sum(x => (long)x.ReservedCredits) > ticket.CreditsRemaining)
            return await Review(row, "FIXED_CREDIT_CHANGED", "固定後票券或額度有衝突，保留占用交人工，不扣負數。", now, ct);

        var usageSn = await settlement.DebitAsync(ticket, row.AttendanceSn, course.EndAt, now, source, operatorId, ct);
        await reservations.UpdateAsync(hold, hold.Consume(now, usageSn), ct);
        return new("Completed", row.AttendanceSn, "CLASS_SETTLED", "本堂扣除一堂並完成核銷，不啟用後續票券。");
    }

    private async Task<AttendanceConfirmationResult> Review(StudentClassAttendance? row,
        string code, string message, DateTime now, CancellationToken ct)
    {
        if (row is not null)
            foreach (var hold in await reservations.GetByAttendanceAsync(row.AttendanceSn, ct))
                if (hold.State is CreditReservationState.Reserved or CreditReservationState.Fixed)
                    await reservations.UpdateAsync(hold, hold.RequireReview(now, code, message), ct);
        return new("NeedsReview", row?.AttendanceSn, code, message);
    }
}
