using gym_system.Application.TicketsUseCase.Services;
using gym_system.Domain.ClassAttendanceProcessing;
using gym_system.Domain.Entities.Devices;
using gym_system.Domain.Repositories;

namespace gym_system.Application.ClassAttendanceProcessing;

public sealed record ClassPreparation(bool Allowed, string Code, string Message);
public sealed record ClassCompletion(string? Code = null, string? Message = null);

public interface IClassAdmissionService
{
    Task LockAsync(CancellationToken ct);
    Task<ClassPreparation> PrepareEntryAsync(Guid requestId, string studentId, CancellationToken ct);
    Task<ClassCompletion> CompleteAsync(Guid requestId, string userId, GateDirection direction,
        string outcome, bool simulated, DateTime respondedAt, CancellationToken ct);
}

public sealed class ClassAdmissionService(IClassAttendanceRepository attendance,
    ICreditReservationRepository reservations, CreditReservationService credits,
    ITicketPassRepository passes, ICurrentTicketService currentTickets, IStudentProfileRepository profiles,
    IClock clock, ClassProcessingSettings settings, IClassProcessingRepository work) : IClassAdmissionService
{
    public Task LockAsync(CancellationToken ct) => attendance.LockCalendarAsync(ct);

    public async Task<ClassPreparation> PrepareEntryAsync(Guid requestId, string studentId, CancellationToken ct)
    {
        if (!settings.Enabled) return new(false, "CLASS_PROCESSING_NOT_ENABLED", "課程進場處理尚未啟用，請洽工作人員。");
        await attendance.LockCalendarAsync(ct);
        var now = clock.Now();
        await passes.LockOwnerAsync(studentId, ct);
        var courses = await attendance.GetSessionsAsync(DateOnly.FromDateTime(now), ct);
        var existing = await attendance.GetAttendancesAsync(studentId, DateOnly.FromDateTime(now), ct);
        var decision = ClassAdmissionPolicy.Select(now, courses,
            existing.Select(x => new AttendancePermit(x.SessionSn, x.Status is "CheckIn" or "CheckOut", x.Eligibility == "Eligible")).ToArray());
        if (!decision.Allowed)
        {
            if (existing.Any(x => x.Status == "Pending" && courses.Any(c => c.SessionSn == x.SessionSn
                && now > c.CutoffAt && now <= c.ReturnUntil)))
                return new(false, "CLASS_CONFIRMATION_PENDING", "此堂出席確認中，請稍後再試或洽工作人員。");
            return new(false, decision.Code, "目前不在可進場或回場的課程時段。");
        }
        var course = decision.Session!;
        if (!ClassPresenceHistory.At(await attendance.GetEventsAsync(studentId, DateOnly.FromDateTime(now), ct), now, requestId).Known)
            return new(false, "CLASS_GATE_RESULT_REVIEW", "先前進出結果尚未確認，請洽工作人員。");
        await attendance.EnsureJobsAsync(course, now, ct);
        var row = existing.SingleOrDefault(x => x.SessionSn == course.SessionSn);
        if (row?.Status == "NeedsReview") return new(false, "CLASS_ATTENDANCE_REVIEW", "此堂出席仍待確認，請洽工作人員。");

        if (decision.Kind == ClassAdmissionKind.Return)
        {
            if (row?.ConfirmedEligible != true || row.PassId is null)
                return new(false, "CLASS_RETURN_NOT_ELIGIBLE", "沒有此堂有效回場資格。");
            var source = await attendance.FindTicketAsync(studentId, row.PassId, ct);
            var holds = await reservations.GetByAttendanceAsync(row.AttendanceSn, ct);
            if (source?.UsableAt(now) != true || holds.Any(x => x.State == CreditReservationState.NeedsReview)
                || (source.Type == "PACK" && !holds.Any(x => x.State == CreditReservationState.Fixed)))
                return new(false, "CLASS_RETURN_REVIEW", "原出席票券或占用有異常，請洽工作人員。");
            await attendance.BindRequestAsync(requestId, studentId,
                new(row.AttendanceSn, holds.SingleOrDefault(x => x.State == CreditReservationState.Fixed)?.ReservationSn,
                    "Return", false, false), ct);
            return new(true, decision.Code, "符合原課程回場資格。");
        }

        if (await attendance.GetPriorUnsettledAsync(studentId, course.StartAt, ct) is long priorItem)
        {
            var progress = await work.GetSettlementProgressAsync(priorItem, studentId, now, ct);
            if (progress?.Running == true)
                return new(false, "PREVIOUS_SETTLEMENT_PROCESSING", "上一堂結算處理中，請稍後重新輸入電話並確認進場");
            if (progress?.State is "Failed" or "NeedsReview")
                return new(false, "PREVIOUS_SETTLEMENT_REVIEW", "上一堂結算未完成：" + progress.Message + " 請洽工作人員。");
            return new(false, "PREVIOUS_SETTLEMENT_PENDING", "上一堂尚未完成結算，請洽工作人員；不是票券不足。");
        }
        var current = await currentTickets.ReconcileAsync(studentId, now, "SYSTEM", ct);
        var ticket = current is null ? null : await attendance.FindTicketAsync(studentId, current.TicketId, ct);
        if (ticket?.UsableAt(now) != true) return new(false, "CLASS_NO_USABLE_TICKET", "目前沒有可用票券。");

        var createdPending = row is null || row.Status == "Cancelled";
        if (row is null)
            row = await attendance.InsertAttendanceAsync(new(0, studentId, course.SessionSn,
                "Pending", null, ticket.PassId, now, now), ct);
        else if (row.Status is "Pending" or "Cancelled")
            row = await attendance.UpdateAttendanceAsync(row, row with { Status = "Pending", Eligibility = null,
                PassId = ticket.PassId, UpdatedAt = now }, ct);
        else return new(false, "CLASS_ATTENDANCE_REVIEW", "出席狀態與時段不一致，請洽工作人員。");

        var history = await reservations.GetByAttendanceAsync(row.AttendanceSn, ct);
        var live = history.SingleOrDefault(x => x.HoldsCredit);
        if (live?.State is CreditReservationState.Fixed or CreditReservationState.NeedsReview)
            throw new InvalidOperationException("Pending attendance has a fixed/review hold; rollback preparation.");
        if (live is not null && (ticket.Type != "PACK" || live.PassSn != ticket.PassSn))
        {
            await reservations.UpdateAsync(live, live.Release(now, "TICKET_RESELECTED", "啟用順序變更，改以最新可用票券暫占。"), ct);
            live = null;
        }
        var createdHold = false;
        if (ticket.Type == "PACK" && live is null)
        {
            var result = await credits.ReserveAsync(studentId, row.AttendanceSn, ticket.PassSn,
                $"GATE:{requestId:N}:{row.AttendanceSn}:{ticket.PassSn}", now, requestId, ct: ct);
            live = result.Reservation;
            createdHold = result.Created;
        }
        await attendance.BindRequestAsync(requestId, studentId,
            new(row.AttendanceSn, live?.ReservationSn, decision.Kind!.Value.ToString(), createdPending, createdHold), ct);
        return new(true, decision.Code, "課程與票券驗證完成，已保留所需額度。");
    }

    public async Task<ClassCompletion> CompleteAsync(Guid requestId, string userId, GateDirection direction,
        string outcome, bool simulated, DateTime respondedAt, CancellationToken ct)
    {
        if (!settings.Enabled) return new();
        await attendance.LockCalendarAsync(ct);
        if (await profiles.FindByUserIdAsync(userId, ct) is null) return new();
        await passes.LockOwnerAsync(userId, ct);
        if (direction == GateDirection.Exit)
        {
            // A confirmed exit releases only temporary holds before that course's cutoff.
            if (outcome != "Succeeded" || simulated) return new();
            foreach (var row in await attendance.GetAttendancesAsync(userId, null, ct))
            {
                if (row.Status is not ("Pending" or "NeedsReview")) continue;
                var course = await attendance.LockSessionAsync(row.SessionSn, ct);
                if (course is null || respondedAt > course.CutoffAt) continue;
                foreach (var hold in await reservations.GetByAttendanceAsync(row.AttendanceSn, ct))
                    if (hold.State == CreditReservationState.Reserved || hold.ReviewFromState == CreditReservationState.Reserved)
                        await reservations.UpdateAsync(hold, hold.Release(clock.Now(), "EXIT_BEFORE_CUTOFF", "截止前成功離場，釋放暫占。"), ct);
                await attendance.UpdateAttendanceAsync(row, row with { Status = "Cancelled", Eligibility = null,
                    UpdatedAt = clock.Now(), CheckOutAt = respondedAt }, ct);
            }
            return new();
        }
        var context = await attendance.GetRequestContextAsync(requestId, userId, ct);
        if (context is null) return new();
        context.Validate();
        var original = await attendance.FindAttendanceAsync(context.AttendanceSn, ct)
            ?? throw new InvalidOperationException("Prepared attendance disappeared.");
        if (original.UserId != userId)
            throw new InvalidOperationException("Prepared attendance belongs to a different user.");
        var session = await attendance.LockSessionAsync(original.SessionSn, ct)
            ?? throw new InvalidOperationException("Prepared course disappeared.");
        var holdRecord = context.ReservationSn is long id ? await reservations.FindByIdAsync(id, ct) : null;
        if (context.ReservationSn is not null && (holdRecord is null || holdRecord.AttendanceSn != original.AttendanceSn
            || (context.CreatedReservation && holdRecord.GateRequestId != requestId)))
            throw new InvalidOperationException("Prepared reservation does not match the attendance or request.");
        var actualNow = clock.Now();
        if (outcome == "Unknown")
        {
            if (context.CreatedReservation && holdRecord?.State == CreditReservationState.Reserved)
                await reservations.UpdateAsync(holdRecord, holdRecord.RequireReview(actualNow, "GATE_RESULT_UNKNOWN", "開門結果不明，保留暫占。"), ct);
            if (original.Status == "Pending")
                await attendance.UpdateAttendanceAsync(original, original with { Status = "NeedsReview", UpdatedAt = actualNow,
                    Remark = "開門結果不明，不推定已入館。" }, ct);
            return new();
        }
        if (outcome != "Succeeded" || simulated)
        {
            if (context.CreatedReservation && holdRecord?.State == CreditReservationState.Reserved)
                await reservations.UpdateAsync(holdRecord, holdRecord.Release(actualNow, "GATE_NOT_OPENED", "此次未實際開門，釋放本次新建占用。"), ct);
            if (context.CreatedPending && original.Status == "Pending")
                await attendance.UpdateAttendanceAsync(original, original with { Status = "Cancelled", Eligibility = null, UpdatedAt = actualNow }, ct);
            return new();
        }
        if (session.Cancelled)
            return new("CLASS_CANCELLED_DURING_DISPATCH", "設備已開門，但課程已取消；不恢復預參加資格，請洽工作人員。");
        var deadline = context.Kind == "Return" ? session.ReturnUntil : session.CutoffAt;
        if (respondedAt > deadline && context.Kind != "Return")
        {
            if (holdRecord?.State == CreditReservationState.Reserved)
                await reservations.UpdateAsync(holdRecord, holdRecord.RequireReview(actualNow, "CLASS_ENTRY_LATE", "截止後才收到開門成功，不能追認截止前出席。"), ct);
            if (original.Status == "Pending")
                await attendance.UpdateAttendanceAsync(original, original with { Status = "NeedsReview", UpdatedAt = actualNow,
                    CheckInAt = respondedAt, Remark = "截止後才成功進場，待確認。" }, ct);
            return new("CLASS_ENTRY_LATE", "設備已開門，但已超過出席截止，請洽工作人員。");
        }
        if (original.Status != "Cancelled")
            await attendance.UpdateAttendanceAsync(original, original with { UpdatedAt = actualNow,
                CheckInAt = original.CheckInAt ?? respondedAt }, ct);
        return respondedAt > deadline
            ? new("CLASS_RETURN_LATE", "設備已開門，但已超過原回場期限，請洽工作人員。") : new();
    }
}
