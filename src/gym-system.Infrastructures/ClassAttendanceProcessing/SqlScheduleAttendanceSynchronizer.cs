using Dapper;
using gym_system.Application.ClassAttendanceProcessing;
using gym_system.Domain.ClassAttendanceProcessing;
using gym_system.Domain.Entities.ScheduleSessions;
using gym_system.Domain.Repositories;
using gym_system.Infrastructures.Connections;

namespace gym_system.Infrastructures.ClassAttendanceProcessing;

internal sealed class SqlScheduleAttendanceSynchronizer(
    ISqlSession sql, ITicketPassRepository passes, ICreditReservationRepository reservations)
    : IScheduleAttendanceSynchronizer
{
    private CommandDefinition Command(string text, object? parameters, CancellationToken ct)
    {
        if (sql.Transaction is null) throw new InvalidOperationException("Schedule synchronization requires the caller transaction.");
        return new(text, parameters, sql.Transaction, cancellationToken: ct);
    }

    public async Task SynchronizeAsync(ScheduleSession session, DateTime changedAt, string operatorId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operatorId);
        if (session.ArrangeSn <= 0 || string.IsNullOrWhiteSpace(session.InstructorId))
            throw new InvalidOperationException("An existing session and instructor are required.");
        session.EnsureEditable(changedAt);
        // Both schedule API routes already acquired the canonical session application lock.
        var parameters = new
        {
            SessionSn = session.ArrangeSn, session.StartAt, session.EndAt,
            session.InstructorId, changedAt, operatorId,
            Cancelled = session.Status == SessionStatus.Cancel
        };
        var started = await sql.Connection.ExecuteScalarAsync<int>(Command("""
            SELECT COUNT(*) FROM dbo.class_processing_job WITH (UPDLOCK, HOLDLOCK)
            WHERE cls_scdle_arnge_sn=@SessionSn AND processing_state NOT IN ('Pending','Cancelled');
            """, parameters, ct));
        if (started != 0) throw new InvalidOperationException("場次工作已開始處理，不能以排課編輯覆寫出席／結算依據。");

        if (session.Status == SessionStatus.Cancel)
        {
            var people = (await sql.Connection.QueryAsync<Person>(Command("""
                SELECT sdt_att_record_sn AS AttendanceSn, user_id AS UserId
                FROM dbo.sdt_att_record WHERE cls_scdle_arnge_sn=@SessionSn
                ORDER BY user_id, sdt_att_record_sn;
                """, parameters, ct))).AsList();
            foreach (var person in people)
            {
                await passes.LockOwnerAsync(person.UserId, ct);
                var passIds = await sql.Connection.QueryAsync<int>(Command("""
                    SELECT DISTINCT pass_sn FROM dbo.sdt_ticket_credit_reservation
                    WHERE attendance_sn=@AttendanceSn ORDER BY pass_sn;
                    """, person, ct));
                foreach (var passSn in passIds)
                    if (await reservations.LockPassAsync(person.UserId, passSn, ct) is null)
                        throw new InvalidOperationException("預參加票券關聯異常，無法直接取消排課。");
                var status = await sql.Connection.QuerySingleAsync<string>(Command("""
                    SELECT sdt_att_record_status FROM dbo.sdt_att_record WITH (UPDLOCK,HOLDLOCK)
                    WHERE sdt_att_record_sn=@AttendanceSn AND user_id=@UserId;
                    """, person, ct));
                if (status is not ("Pending" or "Cancelled" or "NeedsReview"))
                    throw new InvalidOperationException("場次已有正式出席，不可由開課前取消流程直接釋放。");
                var history = await reservations.GetByAttendanceAsync(person.AttendanceSn, ct);
                foreach (var hold in history)
                {
                    if (hold.State == CreditReservationState.Consumed || hold.State == CreditReservationState.Fixed
                        || hold.ReviewFromState == CreditReservationState.Fixed)
                        throw new InvalidOperationException("場次已有固定／扣堂占用，須另行處理更正。");
                    if (hold.HoldsCredit)
                        await reservations.UpdateAsync(hold, hold.Release(changedAt,
                            "CLASS_CANCELLED", "課程於開課前取消，取消預參加並釋放暫占。"), ct);
                }
                await sql.Connection.ExecuteAsync(Command("""
                    UPDATE dbo.sdt_att_record
                    SET sdt_att_record_status='Cancelled', sdt_att_record_ticket_eligibility_state=NULL,
                        sdt_att_record_upd_date=@changedAt, sdt_att_record_upd_source='System_ScheduleChange',
                        sdt_att_record_upd_by_staff=@operatorId,
                        sdt_att_record_remark=CONCAT(sdt_att_record_remark,N' [CLASS_CANCELLED] 課程開課前取消。')
                    WHERE sdt_att_record_sn=@AttendanceSn AND user_id=@UserId;
                    """, new { person.AttendanceSn, person.UserId, changedAt, operatorId }, ct));
            }
            await sql.Connection.ExecuteAsync(Command("""
                UPDATE i SET processing_state='Skipped', blocked_by_item_sn=NULL, next_attempt_at=NULL,
                    completed_at=@changedAt, updated_at=@changedAt,
                    result_code='CLASS_CANCELLED', result_message=N'課程於開課前取消。'
                FROM dbo.class_processing_item i
                INNER JOIN dbo.class_processing_job j ON j.job_sn=i.job_sn
                WHERE j.cls_scdle_arnge_sn=@SessionSn AND i.processing_state NOT IN ('Completed','Skipped');
                """, parameters, ct));
        }

        // Preserve Pending/holds on time or teacher changes. Never change original gate events.
        // An unprocessed cancelled job can be re-opened, but its cancelled attendance is not revived here.
        await sql.Connection.ExecuteAsync(Command("""
            UPDATE dbo.class_processing_job
            SET class_start_at=@StartAt, class_end_at=@EndAt, instructor_id=@InstructorId,
                scheduled_at=CASE phase WHEN 'A' THEN DATEADD(MINUTE,10,@StartAt) ELSE DATEADD(MINUTE,10,@EndAt) END,
                processing_state=CASE WHEN @Cancelled=1 THEN 'Cancelled' ELSE 'Pending' END,
                next_attempt_at=NULL, updated_at=@changedAt,
                last_error_code=CASE WHEN @Cancelled=1 THEN 'CLASS_CANCELLED' ELSE NULL END,
                last_error_message=CASE WHEN @Cancelled=1 THEN N'課程於開課前取消。' ELSE NULL END
            WHERE cls_scdle_arnge_sn=@SessionSn AND processing_state IN ('Pending','Cancelled');

            INSERT dbo.class_processing_job
                (cls_scdle_arnge_sn,phase,processing_state,class_start_at,class_end_at,instructor_id,
                 scheduled_at,created_at,updated_at)
            SELECT @SessionSn,p.phase,CASE WHEN @Cancelled=1 THEN 'Cancelled' ELSE 'Pending' END,
                   @StartAt,@EndAt,@InstructorId,
                   CASE p.phase WHEN 'A' THEN DATEADD(MINUTE,10,@StartAt) ELSE DATEADD(MINUTE,10,@EndAt) END,
                   @changedAt,@changedAt
            FROM (VALUES ('A'),('B')) p(phase)
            WHERE NOT EXISTS (SELECT 1 FROM dbo.class_processing_job WITH (UPDLOCK,HOLDLOCK)
                              WHERE cls_scdle_arnge_sn=@SessionSn AND phase=p.phase);
            """, parameters, ct));
    }

    private sealed class Person
    {
        public int AttendanceSn { get; set; }
        public string UserId { get; set; } = "";
    }
}
