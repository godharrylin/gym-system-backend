using Dapper;
using gym_system.Domain.Entities.Members;
using gym_system.Domain.Repositories;
using gym_system.Infrastructures.Connections;

namespace gym_system.Infrastructures;

internal sealed class SqlStudentProfileRepository : IStudentProfileRepository
{
    private readonly ISqlSession _session;

    public SqlStudentProfileRepository(ISqlSession session)
    {
        _session = session;
    }

    public async Task AddAsync(StudentProfile profile, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO dbo.sdt_profile (usr_id)
            VALUES (@UserId)
            """;

        var affected = await _session.Connection.ExecuteAsync(
            new CommandDefinition(
                sql,
                new { profile.UserId },
                transaction: _session.Transaction,
                cancellationToken: ct));

        if (affected != 1)
        {
            throw new InvalidOperationException("建立學生 Profile 失敗");
        }
    }

    public async Task AddRangeAsync(IReadOnlyList<StudentProfile> profiles, CancellationToken ct)
    {
        foreach (var profile in profiles)
        {
            await AddAsync(profile, ct);
        }
    }

    public async Task<StudentProfile?> FindByUserIdAsync(string userId, CancellationToken ct)
    {
        const string sql = """
            SELECT
                p.usr_id,
                p.sdt_profile_cur_visit_at,
                p.sdt_cur_ticket_id,
                p.sdt_cur_ticket_type,
                p.sdt_cur_ticket_valid_state,
                p.sdt_cur_ticket_payment_state,
                p.sdt_cur_ticket_remain_count,
                p.sdt_cur_ticket_expire_dt,
                p.sdt_cur_ticket_up_dt
            FROM dbo.sdt_profile AS p
            WHERE p.usr_id = @userId
            """;

        var row = await _session.Connection.QueryFirstOrDefaultAsync<StudentProfileRow>(
            new CommandDefinition(
                sql,
                new { userId },
                transaction: _session.Transaction,
                cancellationToken: ct));

        if (row is null)
        {
            return null;
        }

        CurrentTicketSnapshot? currentTicket = null;
        if (!string.IsNullOrWhiteSpace(row.sdt_cur_ticket_id))
        {
            currentTicket = new CurrentTicketSnapshot
            {
                TicketId = row.sdt_cur_ticket_id,
                TicketType = row.sdt_cur_ticket_type ?? string.Empty,
                TicketValidState = row.sdt_cur_ticket_valid_state ?? string.Empty,
                TicketPaymentState = row.sdt_cur_ticket_payment_state ?? string.Empty,
                TicketRemainCount = row.sdt_cur_ticket_remain_count,
                TicketExpireDate = row.sdt_cur_ticket_expire_dt is null
                    ? null
                    : DateOnly.FromDateTime(row.sdt_cur_ticket_expire_dt.Value),
                UpdatedAt = row.sdt_cur_ticket_up_dt ?? DateTime.MinValue
            };
        }

        return StudentProfile.Rehydrate(
            row.usr_id,
            row.sdt_profile_cur_visit_at,
            currentTicket);
    }

    public async Task<bool> UpdateLastVisitAsync(
        string userId,
        DateTime lastVisitAt,
        CancellationToken ct)
    {
        const string sql = """
            UPDATE dbo.sdt_profile
            SET sdt_profile_cur_visit_at = @lastVisitAt
            WHERE usr_id = @userId
            """;

        var affected = await _session.Connection.ExecuteAsync(
            new CommandDefinition(
                sql,
                new { userId, lastVisitAt },
                transaction: _session.Transaction,
                cancellationToken: ct));

        return affected == 1;
    }

    public async Task<bool> UpdateCurrentTicketAsync(
        string userId,
        CurrentTicketSnapshot snapshot,
        CancellationToken ct)
    {
        const string sql = """
            UPDATE dbo.sdt_profile
            SET sdt_cur_ticket_id = @TicketId,
                sdt_cur_ticket_type = @TicketType,
                sdt_cur_ticket_valid_state = @TicketValidState,
                sdt_cur_ticket_payment_state = @TicketPaymentState,
                sdt_cur_ticket_remain_count = @TicketRemainCount,
                sdt_cur_ticket_expire_dt = @TicketExpireDate,
                sdt_cur_ticket_up_dt = @UpdatedAt
            WHERE usr_id = @UserId
            """;

        var affected = await _session.Connection.ExecuteAsync(
            new CommandDefinition(
                sql,
                new
                {
                    UserId = userId,
                    snapshot.TicketId,
                    snapshot.TicketType,
                    snapshot.TicketValidState,
                    snapshot.TicketPaymentState,
                    snapshot.TicketRemainCount,
                    TicketExpireDate = snapshot.TicketExpireDate?.ToDateTime(TimeOnly.MinValue),
                    snapshot.UpdatedAt
                },
                transaction: _session.Transaction,
                cancellationToken: ct));

        return affected == 1;
    }

    private sealed class StudentProfileRow
    {
        public string usr_id { get; init; } = string.Empty;
        public DateTime? sdt_profile_cur_visit_at { get; init; }
        public string? sdt_cur_ticket_id { get; init; }
        public string? sdt_cur_ticket_type { get; init; }
        public string? sdt_cur_ticket_valid_state { get; init; }
        public string? sdt_cur_ticket_payment_state { get; init; }
        public int? sdt_cur_ticket_remain_count { get; init; }
        public DateTime? sdt_cur_ticket_expire_dt { get; init; }
        public DateTime? sdt_cur_ticket_up_dt { get; init; }
    }
}
