using Dapper;
using gym_system.Application.MembersUseCase.Queries.GetStudentMemberList;
using gym_system.Infrastructures.Connections;

namespace gym_system.Infrastructures.Queries.Students;

internal sealed class DapperStudentMemberListQueryService(ISqlConnectionFactory factory) : IStudentMemberListQueryService
{
    public async Task<StudentMemberListResult> QueryAsync(GetStudentMemberListQuery query, DateOnly today, CancellationToken ct)
    {
        // Materialize this request's projection once: count and page use the same rows.
        // No persistent data or lifecycle state is changed by this query.
        const string sql = """
            SELECT u.usr_id AS Id, u.usr_name AS Name, u.usr_phone AS Phone,
                   p.pass_id AS CurrentPassId, k.ticket_plan_kind_cname AS CurrentPlanName,
                   p.valid_status AS ValidStatus, state.DisplayValidState,
                   CONVERT(varchar(10), p.valid_edate, 23) AS ValidEndDate,
                   i.order_items_payment_state AS PaymentState
            INTO #StudentMemberList
            FROM dbo.sdt_profile profile
            INNER JOIN dbo.users u ON u.usr_id = profile.usr_id
            OUTER APPLY (
                SELECT TOP (1) pass.*
                FROM dbo.sdt_ticket_pass pass
                CROSS APPLY (SELECT CASE WHEN pass.valid_status = 'Active'
                    AND (pass.valid_edate IS NULL OR pass.valid_edate >= @today)
                    THEN 1 ELSE 0 END AS IsActive) priority
                WHERE pass.owner_id = u.usr_id
                ORDER BY priority.IsActive DESC,
                    CASE WHEN priority.IsActive = 1 THEN pass.create_dt END ASC,
                    CASE WHEN priority.IsActive = 1 THEN pass.pass_sn END ASC,
                    pass.create_dt DESC, pass.pass_sn DESC
            ) p
            LEFT JOIN dbo.ticket_plan_kind k ON k.ticket_plan_kind_code = p.ticket_plan_kind_code
            LEFT JOIN dbo.order_items i ON i.order_items_sn = p.order_items_sn
            CROSS APPLY (SELECT CASE
                WHEN p.valid_status = 'Active' AND p.valid_edate < @today THEN 'Expire'
                WHEN p.valid_status = 'Active' AND p.valid_edate >= @today
                    AND p.valid_edate < DATEADD(day, 8, @today) THEN 'Expiring'
                ELSE p.valid_status END AS DisplayValidState) state
            WHERE EXISTS (
                SELECT 1 FROM dbo.user_role r
                INNER JOIN dbo.bmc_role role ON role.bmc_role_id = r.bmc_role_id
                WHERE r.usr_id = u.usr_id AND role.bmc_role_code = 'Student'
            )
            AND (@keyword = '' OR u.usr_name LIKE @pattern ESCAPE '~' OR u.usr_phone LIKE @pattern ESCAPE '~')
            AND (@allValid = 1 OR state.DisplayValidState IN @validStates
                OR (@active = 1 AND state.DisplayValidState = 'Expiring'))
            AND (@allPayment = 1 OR i.order_items_payment_state IN @paymentStates);

            SELECT COUNT(*) FROM #StudentMemberList;
            SELECT * FROM #StudentMemberList
            ORDER BY CASE DisplayValidState
                WHEN 'Expiring' THEN 0 WHEN 'Active' THEN 1 WHEN 'UnActive' THEN 2
                WHEN 'Expire' THEN 3 WHEN 'Depleted' THEN 4 WHEN 'Cancelled' THEN 5 ELSE 6 END,
                CASE WHEN PaymentState = 'UnPaid' THEN 0 ELSE 1 END, Id
            OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY;
            DROP TABLE #StudentMemberList;
            """;
        var keyword = query.Keyword?.Trim() ?? "";
        var escaped = keyword.Replace("~", "~~").Replace("%", "~%").Replace("_", "~_").Replace("[", "~[");
        using var connection = factory.CreateConnection();
        using var results = await connection.QueryMultipleAsync(new CommandDefinition(sql, new
        {
            today = today.ToDateTime(TimeOnly.MinValue), keyword, pattern = $"%{escaped}%",
            allValid = query.ValidStates.Length == 0, active = query.ValidStates.Contains("Active"),
            // Use List rather than string[] because the existing Dapper string[] handler is JSON.
            validStates = query.ValidStates.ToList(), allPayment = query.PaymentStates.Length == 0,
            paymentStates = query.PaymentStates.ToList(), offset = (query.Page - 1) * query.PageSize,
            pageSize = query.PageSize
        }, cancellationToken: ct));
        var count = await results.ReadSingleAsync<int>();
        var items = (await results.ReadAsync<StudentMemberListItem>()).AsList();
        return new(items, query.Page, query.PageSize, count);
    }
}
