using Dapper;
using gym_system.Domain.ClassAttendanceProcessing;
using gym_system.Infrastructures.Connections;

namespace gym_system.Infrastructures.ClassAttendanceProcessing;

internal sealed class SqlClassProcessingRepository(ISqlSession sql) : IClassProcessingRepository
{
    private const string JobColumns = """
        job_sn AS JobSn,cls_scdle_arnge_sn AS SessionSn,phase AS Phase,processing_state AS State,
        class_start_at AS StartAt,class_end_at AS EndAt,scheduled_at AS ScheduledAt,
        lease_token AS LeaseToken,lease_expires_at AS LeaseExpiresAt
        """;
    private CommandDefinition Command(string text, object? args, CancellationToken ct)
    {
        if (sql.Transaction is null) throw new InvalidOperationException("Class work operations require a transaction.");
        return new(text, ClassProcessingSqlParameters.Create(args), sql.Transaction, cancellationToken: ct);
    }
    public Task DiscoverJobsAsync(DateOnly startDate, DateTime now, CancellationToken ct)
        => sql.Connection.ExecuteAsync(Command("""
            INSERT dbo.class_processing_job (cls_scdle_arnge_sn,phase,processing_state,class_start_at,
                class_end_at,instructor_id,scheduled_at,created_at,updated_at)
            SELECT c.cls_scdle_arnge_sn,p.phase,'Pending',c.cls_scdle_arnge_st,c.cls_scdle_arnge_et,
                c.cls_scdle_arnge_instructor_id,
                CASE p.phase WHEN 'A' THEN DATEADD(MINUTE,10,c.cls_scdle_arnge_st)
                             ELSE DATEADD(MINUTE,10,c.cls_scdle_arnge_et) END,@now,@now
            FROM dbo.cls_scdle_arnge c CROSS JOIN (VALUES ('A'),('B')) p(phase)
            WHERE c.cls_scdle_date>=@start AND c.cls_scdle_date<=@today
                AND c.cls_scdle_status IN ('Open','Ongoing','Finished')
                AND NOT EXISTS (SELECT 1 FROM dbo.class_processing_job j WITH (UPDLOCK,HOLDLOCK)
                                WHERE j.cls_scdle_arnge_sn=c.cls_scdle_arnge_sn AND j.phase=p.phase);
            """, new { start = startDate.ToDateTime(TimeOnly.MinValue), today = now.Date, now }, ct));

    public async Task<IReadOnlyList<long>> GetDueJobsAsync(DateTime now, CancellationToken ct)
        => (await sql.Connection.QueryAsync<long>(Command("""
            SELECT job_sn FROM dbo.class_processing_job WHERE phase IN ('A','B') AND scheduled_at<@now
                AND ((processing_state IN ('Pending','Waiting','Failed') AND (next_attempt_at IS NULL OR next_attempt_at<=@now))
                    OR (processing_state='Running' AND lease_expires_at<=@now))
            ORDER BY scheduled_at,job_sn;
            """, new { now }, ct))).AsList();

    public async Task<ClassWorkJob?> TryLeaseAsync(long jobSn, Guid token, string owner, DateTime now, CancellationToken ct)
    {
        var count = await sql.Connection.ExecuteAsync(Command("""
            UPDATE dbo.class_processing_job SET processing_state='Running',lease_owner=@owner,lease_token=@token,
                lease_expires_at=DATEADD(MINUTE,2,@now),processing_attempts=processing_attempts+1,
                first_started_at=COALESCE(first_started_at,@now),last_started_at=@now,updated_at=@now,next_attempt_at=NULL
            WHERE job_sn=@jobSn AND phase IN ('A','B') AND scheduled_at<@now
                AND ((processing_state IN ('Pending','Waiting','Failed') AND (next_attempt_at IS NULL OR next_attempt_at<=@now))
                     OR (processing_state='Running' AND lease_expires_at<=@now));
            """, new { jobSn, token, owner, now }, ct));
        return count == 1 ? await ReadJobAsync(jobSn, ct) : null;
    }

    private Task<ClassWorkJob> ReadJobAsync(long jobSn, CancellationToken ct)
        => sql.Connection.QuerySingleAsync<ClassWorkJob>(Command(
            $"SELECT {JobColumns} FROM dbo.class_processing_job WITH (UPDLOCK,HOLDLOCK) WHERE job_sn=@jobSn;", new { jobSn }, ct));

    public async Task<ClassWorkJob> VerifyAndRenewLeaseAsync(long jobSn, Guid token, DateTime now, CancellationToken ct)
    {
        var row = await ReadJobAsync(jobSn, ct);
        if (row.State != "Running" || row.LeaseToken != token || row.LeaseExpiresAt <= now)
            throw new InvalidOperationException("Class processing lease expired or was replaced.");
        await sql.Connection.ExecuteAsync(Command("""
            UPDATE dbo.class_processing_job SET lease_expires_at=DATEADD(MINUTE,2,@now),updated_at=@now
            WHERE job_sn=@jobSn AND lease_token=@token AND processing_state='Running';
            """, new { jobSn, token, now }, ct));
        return row;
    }

    public async Task<IReadOnlyList<ClassWorkItem>> EnsureItemsAsync(long jobSn, IReadOnlyList<string> users, DateTime now, CancellationToken ct)
    {
        foreach (var user in users.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
            await sql.Connection.ExecuteAsync(Command("""
                INSERT dbo.class_processing_item (job_sn,user_id,processing_state,created_at,updated_at)
                SELECT @jobSn,@user,'Pending',@now,@now
                WHERE NOT EXISTS (SELECT 1 FROM dbo.class_processing_item WITH (UPDLOCK,HOLDLOCK) WHERE job_sn=@jobSn AND user_id=@user);
                """, new { jobSn, user, now }, ct));
        return (await sql.Connection.QueryAsync<ClassWorkItem>(Command("""
            SELECT item_sn AS ItemSn,job_sn AS JobSn,user_id AS UserId,attendance_sn AS AttendanceSn,processing_state AS State,
                processing_attempts AS Attempts
            FROM dbo.class_processing_item WITH (UPDLOCK,HOLDLOCK) WHERE job_sn=@jobSn ORDER BY user_id,item_sn;
            """, new { jobSn }, ct))).AsList();
    }

    public async Task<ClassWorkItem?> BeginItemAsync(ClassWorkItem original, DateTime now, CancellationToken ct)
        => await sql.Connection.QuerySingleOrDefaultAsync<ClassWorkItem>(Command("""
            UPDATE dbo.class_processing_item SET processing_state='Running',
                processing_attempts=processing_attempts+1,first_started_at=COALESCE(first_started_at,@now),
                last_started_at=@now,updated_at=@now,next_attempt_at=NULL,blocked_by_item_sn=NULL
            OUTPUT inserted.item_sn AS ItemSn,inserted.job_sn AS JobSn,inserted.user_id AS UserId,
                inserted.attendance_sn AS AttendanceSn,inserted.processing_state AS State,inserted.processing_attempts AS Attempts
            WHERE item_sn=@ItemSn AND job_sn=@JobSn AND user_id=@UserId
                AND processing_state=@State AND processing_attempts=@Attempts
                AND processing_state NOT IN ('Completed','Skipped','NeedsReview')
                AND (next_attempt_at IS NULL OR next_attempt_at<=@now);
            """, new { original.ItemSn, original.JobSn, original.UserId, original.State, original.Attempts, now }, ct));

    public Task<SettlementProgress?> GetSettlementProgressAsync(long itemSn, string userId, DateTime now, CancellationToken ct)
        => sql.Connection.QuerySingleOrDefaultAsync<SettlementProgress>(Command("""
            SELECT i.processing_state AS State,i.result_code AS Code,i.result_message AS Message,
                CAST(CASE WHEN i.processing_state='Running' AND j.processing_state='Running'
                    AND j.lease_expires_at>@now THEN 1 ELSE 0 END AS bit) AS Running
            FROM dbo.class_processing_item i INNER JOIN dbo.class_processing_job j ON j.job_sn=i.job_sn
            WHERE i.item_sn=@itemSn AND i.user_id=@userId AND j.phase='B';
            """, new { itemSn, userId, now }, ct));

    public async Task CompleteItemAsync(ClassWorkItem original, string state, int? attendanceSn, string code,
        string message, long? blockedBy, DateTime now, CancellationToken ct)
    {
        if (state is not ("Completed" or "Skipped" or "Blocked" or "NeedsReview" or "Failed")
            || string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(message)
            || code.Length > 60 || message.Length > 1000 || ((state == "Blocked") != (blockedBy is not null)))
            throw new ArgumentException("Invalid class processing result.");
        var count = await sql.Connection.ExecuteAsync(Command("""
            UPDATE dbo.class_processing_item
            SET processing_state=@newState,attendance_sn=@attendanceSn,result_code=@code,result_message=@message,
                blocked_by_item_sn=@blockedBy,
                processing_attempts=processing_attempts+CASE
                    WHEN processing_state='Running' AND @newState='Blocked' THEN -1
                    WHEN processing_state='Running' THEN 0 ELSE 1 END,
                first_started_at=COALESCE(first_started_at,@now),last_started_at=@now,updated_at=@now,
                completed_at=CASE WHEN @newState IN ('Completed','Skipped') THEN @now ELSE NULL END,
                next_attempt_at=CASE WHEN @newState='Failed' THEN DATEADD(SECOND,15,@now) ELSE NULL END
            WHERE item_sn=@ItemSn AND job_sn=@JobSn AND user_id=@UserId AND processing_state=@expectedState
                AND processing_attempts=@Attempts
                AND processing_state NOT IN ('Completed','Skipped','NeedsReview');
            """, new { original.ItemSn, original.JobSn, original.UserId, original.Attempts, expectedState = original.State,
                newState = state, attendanceSn, code, message, blockedBy, now }, ct));
        if (count != 1) throw new InvalidOperationException("Class item is stale or already terminal.");
    }

    public async Task FinishBatchAsync(long jobSn, Guid token, DateTime now, CancellationToken ct)
    {
        await VerifyAndRenewLeaseAsync(jobSn, token, now, ct);
        // NeedsReview is not considered complete; Blocked waits only for this student's dependency.
        await sql.Connection.ExecuteAsync(Command("""
            DECLARE @state varchar(20)=CASE
                WHEN EXISTS (SELECT 1 FROM dbo.class_processing_item WHERE job_sn=@jobSn
                    AND processing_state IN ('Pending','Running','Blocked','Failed')) THEN 'Waiting'
                WHEN EXISTS (SELECT 1 FROM dbo.class_processing_item WHERE job_sn=@jobSn AND processing_state='NeedsReview') THEN 'NeedsReview'
                ELSE 'Completed' END;
            UPDATE dbo.class_processing_job SET processing_state=@state,lease_owner=NULL,lease_token=NULL,lease_expires_at=NULL,
                completed_at=CASE WHEN @state='Completed' THEN @now ELSE NULL END,
                next_attempt_at=CASE WHEN @state='Waiting' THEN DATEADD(SECOND,15,@now) ELSE NULL END,updated_at=@now
            WHERE job_sn=@jobSn AND lease_token=@token AND processing_state='Running';
            """, new { jobSn, token, now }, ct));
    }
}
