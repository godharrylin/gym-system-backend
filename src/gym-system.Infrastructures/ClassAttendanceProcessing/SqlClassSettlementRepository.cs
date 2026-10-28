using Dapper;
using gym_system.Domain.ClassAttendanceProcessing;
using gym_system.Infrastructures.Connections;

namespace gym_system.Infrastructures.ClassAttendanceProcessing;

internal sealed class SqlClassSettlementRepository(ISqlSession sql) : IClassSettlementRepository
{
    private CommandDefinition Command(string text, object args, CancellationToken ct)
    {
        if (sql.Transaction is null) throw new InvalidOperationException("Settlement requires a caller transaction.");
        return new(text, ClassProcessingSqlParameters.Create(args), sql.Transaction, cancellationToken: ct);
    }

    public Task<ClassWorkItem?> FindConfirmationAsync(int sessionSn, string userId, CancellationToken ct)
        => sql.Connection.QuerySingleOrDefaultAsync<ClassWorkItem>(Command("""
            SELECT i.item_sn AS ItemSn,i.job_sn AS JobSn,i.user_id AS UserId,i.attendance_sn AS AttendanceSn,
                i.processing_state AS State,i.processing_attempts AS Attempts
            FROM dbo.class_processing_item i WITH (UPDLOCK,HOLDLOCK)
            INNER JOIN dbo.class_processing_job j ON j.job_sn=i.job_sn
            WHERE j.cls_scdle_arnge_sn=@sessionSn AND j.phase='A' AND i.user_id=@userId;
            """, new { sessionSn, userId }, ct));

    public async Task<IReadOnlyList<ClassTicketUsage>> GetUsageAsync(int attendanceSn, CancellationToken ct)
        => (await sql.Connection.QueryAsync<ClassTicketUsage>(Command("""
            SELECT usage_sn AS UsageSn,pass_id AS PassId,attendance_sn AS AttendanceSn,usage_action AS Action,
                usage_type AS Source,deducted_credits AS Credits,credits_before_snapshot AS [Before],
                rem_credits_snapshot AS [After],usage_dt AS BusinessAt,recorded_at AS RecordedAt,
                reversed_usage_sn AS ReversedUsageSn
            FROM dbo.sdt_ticket_usage_log WITH (UPDLOCK,HOLDLOCK) WHERE attendance_sn=@attendanceSn ORDER BY usage_sn;
            """, new { attendanceSn }, ct))).AsList();

    public async Task<int> DebitAsync(AdmissionTicket original, int attendanceSn, DateTime businessAt,
        DateTime recordedAt, string source, string operatorId, CancellationToken ct)
    {
        if (original.Type != "PACK" || original.CreditsRemaining is not > 0
            || source is not ("Manual" or "Auto_Settlement") || string.IsNullOrWhiteSpace(operatorId)
            || operatorId.Length > 50 || businessAt > recordedAt || businessAt.Date != recordedAt.Date)
            throw new InvalidOperationException("Invalid course debit data.");
        if ((await GetUsageAsync(attendanceSn, ct)).Count != 0)
            throw new InvalidOperationException("Attendance already has usage; revalidate instead of debiting.");
        var count = await sql.Connection.ExecuteAsync(Command("""
            UPDATE dbo.sdt_ticket_pass SET credits_remaining=credits_remaining-1,
                valid_status=CASE WHEN credits_remaining=1 THEN 'Depleted' ELSE valid_status END,
                ended_at=CASE WHEN credits_remaining=1 THEN @businessAt ELSE ended_at END,
                end_reason=CASE WHEN credits_remaining=1 THEN 'Depleted' ELSE end_reason END,
                update_dt=@recordedAt,update_pn=@operatorId
            WHERE pass_sn=@PassSn AND pass_id=@PassId AND owner_id=@OwnerId
                AND valid_status='Active' AND credits_remaining=@CreditsRemaining AND credits_remaining>0;
            """, new { original.PassSn, original.PassId, original.OwnerId, original.CreditsRemaining,
                businessAt, recordedAt, operatorId }, ct));
        if (count != 1) throw new InvalidOperationException("Fixed ticket balance/state changed; debit rolled back.");
        var usage = await sql.Connection.ExecuteScalarAsync<int>(Command("""
            INSERT dbo.sdt_ticket_usage_log (pass_id,usage_dt,usage_type,deducted_credits,
                rem_credits_snapshot,operator_id,remark,attendance_sn,usage_action,credits_before_snapshot,recorded_at)
            OUTPUT inserted.usage_sn
            VALUES (@PassId,@businessAt,@source,1,@after,@operatorId,N'課程結算',@attendanceSn,'Consume',@CreditsRemaining,@recordedAt);
            """, new { original.PassId, original.CreditsRemaining, after = original.CreditsRemaining - 1,
                attendanceSn, businessAt, recordedAt, source, operatorId }, ct));
        // Update only the same projected ticket. Do not activate or replace it with a queued pass.
        await sql.Connection.ExecuteAsync(Command("""
            UPDATE dbo.sdt_profile SET sdt_cur_ticket_remain_count=@after,
                sdt_cur_ticket_valid_state=CASE WHEN @after=0 THEN 'Depleted' ELSE sdt_cur_ticket_valid_state END,
                sdt_cur_ticket_up_dt=@recordedAt
            WHERE usr_id=@OwnerId AND sdt_cur_ticket_id=@PassId;
            """, new { original.OwnerId, original.PassId, after = original.CreditsRemaining - 1, recordedAt }, ct));
        return usage;
    }
}
