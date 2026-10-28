using Dapper;
using gym_system.Domain.ClassAttendanceProcessing;
using gym_system.Domain.Repositories;
using gym_system.Infrastructures.Connections;

namespace gym_system.Infrastructures.ClassAttendanceProcessing;

internal sealed class SqlClassAttendanceRepository(ISqlSession sql, IScheduleSessionRepository schedules)
    : IClassAttendanceRepository
{
    private const string AttendanceColumns = """
        a.sdt_att_record_sn AS AttendanceSn,a.user_id AS UserId,a.cls_scdle_arnge_sn AS SessionSn,
        a.sdt_att_record_status AS Status,a.sdt_att_record_ticket_eligibility_state AS Eligibility,
        a.sdt_att_record_ticket_id AS PassId,a.sdt_att_record_create_dt AS CreatedAt,
        a.sdt_att_record_upd_date AS UpdatedAt,a.sdt_att_record_chk_in_time AS CheckInAt,
        a.sdt_att_record_chk_out_time AS CheckOutAt,a.sdt_att_record_remark AS Remark
        """;
    private CommandDefinition Command(string text, object? args, CancellationToken ct)
    {
        if (sql.Transaction is null) throw new InvalidOperationException("Attendance operations require a caller transaction.");
        return new(text, args, sql.Transaction, cancellationToken: ct);
    }
    public Task LockCalendarAsync(CancellationToken ct) => ClassAttendanceSqlLocks.LockCalendarAsync(sql, ct);

    public async Task<IReadOnlyList<CourseSessionWindow>> GetSessionsAsync(DateOnly day, CancellationToken ct)
        => (await sql.Connection.QueryAsync<CourseSessionWindow>(Command("""
            SELECT cls_scdle_arnge_sn AS SessionSn,cls_scdle_arnge_id AS SessionId,
                   cls_scdle_arnge_st AS StartAt,cls_scdle_arnge_et AS EndAt,
                   cls_scdle_arnge_instructor_id AS InstructorId,
                   CAST(CASE WHEN cls_scdle_status='Cancel' THEN 1 ELSE 0 END AS bit) AS Cancelled
            FROM dbo.cls_scdle_arnge WHERE cls_scdle_date=@day ORDER BY cls_scdle_arnge_st,cls_scdle_arnge_sn;
            """, new { day = day.ToDateTime(TimeOnly.MinValue) }, ct))).AsList();

    public async Task<CourseSessionWindow?> LockSessionAsync(int sessionSn, CancellationToken ct)
    {
        await schedules.AcquireSessionMutationLockAsync(sessionSn.ToString(System.Globalization.CultureInfo.InvariantCulture), ct);
        var session = await schedules.GetByIdAsync(sessionSn.ToString(System.Globalization.CultureInfo.InvariantCulture), ct);
        return session is null ? null : new(session.ArrangeSn, session.ArrangeId, session.StartAt,
            session.EndAt, session.InstructorId, session.Status == gym_system.Domain.Entities.ScheduleSessions.SessionStatus.Cancel);
    }

    public async Task<IReadOnlyList<StudentClassAttendance>> GetAttendancesAsync(string userId, DateOnly? day, CancellationToken ct)
    {
        var rows = (await sql.Connection.QueryAsync<StudentClassAttendance>(Command($"""
            SELECT {AttendanceColumns} FROM dbo.sdt_att_record a WITH (UPDLOCK,HOLDLOCK)
            INNER JOIN dbo.cls_scdle_arnge c ON c.cls_scdle_arnge_sn=a.cls_scdle_arnge_sn
            WHERE a.user_id=@userId AND (@day IS NULL OR c.cls_scdle_date=@day)
            ORDER BY a.sdt_att_record_sn;
            """, new { userId, day = day?.ToDateTime(TimeOnly.MinValue) }, ct))).AsList();
        foreach (var row in rows) row.Validate();
        return rows;
    }

    public async Task<StudentClassAttendance?> FindAttendanceAsync(int attendanceSn, CancellationToken ct)
    {
        var row = await sql.Connection.QuerySingleOrDefaultAsync<StudentClassAttendance>(Command($"""
            SELECT {AttendanceColumns} FROM dbo.sdt_att_record a WITH (UPDLOCK,HOLDLOCK)
            WHERE a.sdt_att_record_sn=@attendanceSn;
            """, new { attendanceSn }, ct));
        row?.Validate();
        return row;
    }

    public async Task<StudentClassAttendance> InsertAttendanceAsync(StudentClassAttendance value, CancellationToken ct)
    {
        value.Validate();
        if (value.AttendanceSn != 0) throw new InvalidOperationException("New attendance must not have an assigned ID.");
        await ValidateRelationsAsync(value, ct);
        var id = await sql.Connection.ExecuteScalarAsync<int>(Command("""
            INSERT dbo.sdt_att_record (user_id,cls_scdle_arnge_sn,sdt_att_record_status,
                sdt_att_record_ticket_eligibility_state,sdt_att_record_ticket_id,sdt_att_record_create_dt,
                sdt_att_record_upd_date,sdt_att_record_chk_in_time,sdt_att_record_chk_out_time,
                sdt_att_record_remark,sdt_att_record_upd_source,sdt_att_record_upd_by_staff)
            OUTPUT inserted.sdt_att_record_sn
            VALUES (@UserId,@SessionSn,@Status,@Eligibility,@PassId,@CreatedAt,@UpdatedAt,
                @CheckInAt,@CheckOutAt,@Remark,'System_IO','SYSTEM');
            """, value, ct));
        return (await FindAttendanceAsync(id, ct))!;
    }

    public async Task<StudentClassAttendance> UpdateAttendanceAsync(StudentClassAttendance original,
        StudentClassAttendance value, CancellationToken ct)
    {
        value.Validate();
        if (original.AttendanceSn <= 0 || original.AttendanceSn != value.AttendanceSn
            || original.UserId != value.UserId || original.SessionSn != value.SessionSn || original.CreatedAt != value.CreatedAt
            || await FindAttendanceAsync(original.AttendanceSn, ct) != original)
            throw new InvalidOperationException("Attendance identity changed or its original snapshot is stale.");
        await ValidateRelationsAsync(value, ct);
        var count = await sql.Connection.ExecuteAsync(Command("""
            UPDATE dbo.sdt_att_record SET sdt_att_record_status=@Status,
                sdt_att_record_ticket_eligibility_state=@Eligibility,sdt_att_record_ticket_id=@PassId,
                sdt_att_record_upd_date=@UpdatedAt,sdt_att_record_chk_in_time=@CheckInAt,
                sdt_att_record_chk_out_time=@CheckOutAt,sdt_att_record_remark=@Remark,
                sdt_att_record_upd_source='System_IO',sdt_att_record_upd_by_staff='SYSTEM'
            WHERE sdt_att_record_sn=@AttendanceSn AND user_id=@UserId AND cls_scdle_arnge_sn=@SessionSn;
            """, value, ct));
        if (count != 1) throw new InvalidOperationException("Attendance update did not affect exactly one row.");
        return (await FindAttendanceAsync(value.AttendanceSn, ct))!;
    }

    private async Task ValidateRelationsAsync(StudentClassAttendance value, CancellationToken ct)
    {
        var valid = await sql.Connection.ExecuteScalarAsync<bool>(Command("""
            SELECT CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.sdt_profile WHERE usr_id=@UserId)
                AND EXISTS (SELECT 1 FROM dbo.cls_scdle_arnge WHERE cls_scdle_arnge_sn=@SessionSn)
                AND (@PassId IS NULL OR EXISTS (SELECT 1 FROM dbo.sdt_ticket_pass WHERE pass_id=@PassId AND owner_id=@UserId))
                THEN 1 ELSE 0 END AS bit);
            """, value, ct));
        if (!valid) throw new InvalidOperationException("Attendance profile, session or ticket relation is invalid.");
    }

    public Task<AdmissionTicket?> FindTicketAsync(string userId, string passId, CancellationToken ct)
        => sql.Connection.QuerySingleOrDefaultAsync<AdmissionTicket>(Command("""
            SELECT p.pass_sn AS PassSn,p.pass_id AS PassId,p.owner_id AS OwnerId,
                p.ticket_plan_kind_code AS PlanCode,p.ticket_plan_kind_type AS Type,
                p.valid_status AS Status,i.order_items_payment_state AS PaymentState,
                p.credits_remaining AS CreditsRemaining,p.valid_sdate AS StartDate,p.valid_edate AS EndDate,
                i.order_items_paid_at AS PaidAt,p.update_dt AS ChangedAt,p.ended_at AS EndedAt,p.end_reason AS EndReason
            FROM dbo.sdt_ticket_pass p WITH (UPDLOCK,HOLDLOCK)
            INNER JOIN dbo.order_items i ON i.order_items_sn=p.order_items_sn
            WHERE p.owner_id=@userId AND p.pass_id=@passId;
            """, new { userId, passId }, ct));

    public async Task<IReadOnlyList<ClassGateEvent>> GetEventsAsync(string userId, DateOnly day, CancellationToken ct)
        => (await sql.Connection.QueryAsync<ClassGateEvent>(Command("""
            SELECT request_id AS RequestId,direction AS Direction,identity_kind AS IdentityKind,
                outcome AS Outcome,COALESCE(requested_at,device_record_dt) AS RequestedAt,
                responded_at AS RespondedAt,affects_presence AS AffectsPresence,result_code AS ResultCode
            FROM dbo.device_record WHERE usr_id=@userId
                AND (COALESCE(requested_at,device_record_dt)>=@start OR responded_at>=@start)
                AND COALESCE(requested_at,device_record_dt)<@finish
            ORDER BY COALESCE(responded_at,requested_at,device_record_dt),device_record_sn;
            """, new { userId, start = day.ToDateTime(TimeOnly.MinValue), finish = day.AddDays(1).ToDateTime(TimeOnly.MinValue) }, ct))).AsList();

    public async Task BindRequestAsync(Guid requestId, string userId, ClassGateContext context, CancellationToken ct)
    {
        context.Validate();
        if (context.ReservationSn is long reservationSn)
        {
            var valid = await sql.Connection.ExecuteScalarAsync<bool>(Command("""
                SELECT CAST(CASE WHEN EXISTS (
                    SELECT 1 FROM dbo.sdt_ticket_credit_reservation r WITH (UPDLOCK,HOLDLOCK)
                    WHERE r.reservation_sn=@reservationSn AND r.attendance_sn=@AttendanceSn
                      AND (@CreatedReservation=0 OR r.gate_request_id=@requestId)
                ) THEN 1 ELSE 0 END AS bit);
                """, new { reservationSn, context.AttendanceSn, context.CreatedReservation, requestId }, ct));
            if (!valid) throw new InvalidOperationException("Gate reservation does not match its attendance or request.");
        }
        var count = await sql.Connection.ExecuteAsync(Command("""
            UPDATE r SET attendance_sn=@AttendanceSn,reservation_sn=@ReservationSn,admission_kind=@Kind,
                created_pending=@CreatedPending,created_reservation=@CreatedReservation
            FROM dbo.device_record r INNER JOIN dbo.sdt_att_record a ON a.sdt_att_record_sn=@AttendanceSn
            WHERE r.request_id=@requestId AND r.usr_id=@userId AND a.user_id=@userId
                AND r.outcome='Requested' AND r.attendance_sn IS NULL;
            """, new { requestId, userId, context.AttendanceSn, context.ReservationSn,
                context.Kind, context.CreatedPending, context.CreatedReservation }, ct));
        if (count != 1) throw new InvalidOperationException("Gate request could not be bound to attendance.");
    }

    public async Task<ClassGateContext?> GetRequestContextAsync(Guid requestId, string userId, CancellationToken ct)
    {
        var row = await sql.Connection.QuerySingleOrDefaultAsync<GateContextRow>(Command("""
            SELECT attendance_sn AS AttendanceSn,reservation_sn AS ReservationSn,admission_kind AS Kind,
                created_pending AS CreatedPending,created_reservation AS CreatedReservation
            FROM dbo.device_record WITH (UPDLOCK,HOLDLOCK)
            WHERE request_id=@requestId AND usr_id=@userId AND attendance_sn IS NOT NULL;
            """, new { requestId, userId }, ct));
        if (row is null) return null;
        if (row.Kind is null || row.CreatedPending is null || row.CreatedReservation is null)
            throw new InvalidOperationException("Stored gate context is incomplete; cannot infer cleanup flags.");
        var context = new ClassGateContext(row.AttendanceSn, row.ReservationSn, row.Kind,
            row.CreatedPending.Value, row.CreatedReservation.Value);
        context.Validate();
        return context;
    }

    private sealed record GateContextRow(int AttendanceSn, long? ReservationSn, string? Kind,
        bool? CreatedPending, bool? CreatedReservation);

    public Task EnsureJobsAsync(CourseSessionWindow course, DateTime now, CancellationToken ct)
        => sql.Connection.ExecuteAsync(Command("""
            INSERT dbo.class_processing_job
                (cls_scdle_arnge_sn,phase,processing_state,class_start_at,class_end_at,
                 instructor_id,scheduled_at,created_at,updated_at)
            SELECT @SessionSn,p.phase,'Pending',@StartAt,@EndAt,@InstructorId,
                CASE p.phase WHEN 'A' THEN DATEADD(MINUTE,10,@StartAt) ELSE DATEADD(MINUTE,10,@EndAt) END,@now,@now
            FROM (VALUES ('A'),('B')) p(phase)
            WHERE NOT EXISTS (SELECT 1 FROM dbo.class_processing_job WITH (UPDLOCK,HOLDLOCK)
                              WHERE cls_scdle_arnge_sn=@SessionSn AND phase=p.phase);
            """, new { course.SessionSn, course.StartAt, course.EndAt, course.InstructorId, now }, ct));

    public async Task<long?> GetPriorUnsettledAsync(string userId, DateTime classStart, CancellationToken ct)
    {
        // Materialize B items for existing eligible attendance, but never mark B done here.
        await sql.Connection.ExecuteAsync(Command("""
            INSERT dbo.class_processing_item (job_sn,user_id,attendance_sn,processing_state,created_at,updated_at)
            SELECT j.job_sn,a.user_id,a.sdt_att_record_sn,'Pending',a.sdt_att_record_upd_date,a.sdt_att_record_upd_date
            FROM dbo.sdt_att_record a INNER JOIN dbo.class_processing_job j ON j.cls_scdle_arnge_sn=a.cls_scdle_arnge_sn AND j.phase='B'
            WHERE a.user_id=@userId AND j.class_start_at<@classStart
              AND a.sdt_att_record_status IN ('CheckIn','CheckOut') AND a.sdt_att_record_ticket_eligibility_state='Eligible'
              AND NOT EXISTS (SELECT 1 FROM dbo.class_processing_item i WITH (UPDLOCK,HOLDLOCK)
                              WHERE i.job_sn=j.job_sn AND i.user_id=a.user_id);
            """, new { userId, classStart }, ct));
        return await sql.Connection.QueryFirstOrDefaultAsync<long?>(Command("""
            SELECT TOP (1) i.item_sn FROM dbo.class_processing_item i
            INNER JOIN dbo.class_processing_job j ON j.job_sn=i.job_sn
            WHERE i.user_id=@userId AND j.phase='B' AND j.class_start_at<@classStart
                AND i.processing_state NOT IN ('Completed','Skipped')
            ORDER BY j.class_start_at,i.item_sn;
            """, new { userId, classStart }, ct));
    }

    public async Task<IReadOnlyList<string>> GetCandidatesAsync(int sessionSn, DateOnly day, CancellationToken ct, bool includeGateHistory = true)
        => (await sql.Connection.QueryAsync<string>(Command("""
            SELECT user_id FROM dbo.sdt_att_record WHERE cls_scdle_arnge_sn=@sessionSn
            UNION
            SELECT i.user_id FROM dbo.class_processing_item i
                INNER JOIN dbo.class_processing_job j ON j.job_sn=i.job_sn
                WHERE j.cls_scdle_arnge_sn=@sessionSn AND j.phase='A'
            UNION
            SELECT usr_id FROM dbo.device_record WHERE @includeGateHistory=1 AND usr_id IS NOT NULL AND identity_kind='Student'
                AND COALESCE(requested_at,device_record_dt)>=@start AND COALESCE(requested_at,device_record_dt)<@finish;
            """, new { sessionSn, includeGateHistory, start = day.ToDateTime(TimeOnly.MinValue), finish = day.AddDays(1).ToDateTime(TimeOnly.MinValue) }, ct))).AsList();
}
