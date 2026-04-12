using Dapper;
using gym_system.Application.TicketPlansUseCase.Queries;
using gym_system.Infrastructures.Connections;
using Microsoft.EntityFrameworkCore.Update.Internal;

namespace gym_system.Infrastructures.Queries.TicketPlans
{
    internal sealed class DapperTicketPlanCatalogQueryService : ITicketPlanCatalogQuerySerivce
    {
        private readonly ISqlConnectionFactory _factory;

        public DapperTicketPlanCatalogQueryService(ISqlConnectionFactory factory)
        {
            _factory = factory;
        }

        public async Task<IReadOnlyList<TicketPlanResult>> GetActiveTicketPlansAsync(CancellationToken ct)
        {
            const string sql = """
                SELECT
                    k.ticket_plan_kind_code AS Id,
                    k.ticket_plan_kind_cname AS Name,
                    k.ticket_plan_kind_price AS Price,
                    k.ticket_plan_kind_default_expire_days AS Days,
                    k.ticket_plan_kind_default_credit AS Sessions,
                    CASE ticket_plan_kind_type
                        WHEN 'PACK' THEN 'SESSION'
                        WHEN 'M_PASS' THEN 'MONTHLY'
                        ELSE ticket_plan_kind_type
                        END AS Type,
                    COALESCE(
                        '[' + STRING_AGG('"' + r.plan_rule_code + '"', ',') + ']',
                        '[]'
                    ) AS Tags
                FROM dbo.ticket_plan_kind k
                LEFT JOIN dbo.ticket_plan_kind_rule kr
                    ON kr.ticket_plan_kind_sn = k.ticket_plan_kind_sn
                LEFT JOIN dbo.plan_rule r
                    ON r.plan_rule_sn = kr.plan_rule_sn
                   AND r.plan_rule_is_active = 'Y'
                WHERE k.ticket_plan_kind_default_is_active = 'Y'
                GROUP BY
                    k.ticket_plan_kind_sn,
                    k.ticket_plan_kind_code,
                    k.ticket_plan_kind_type,
                    k.ticket_plan_kind_cname,
                    k.ticket_plan_kind_price,
                    k.ticket_plan_kind_default_credit,
                    k.ticket_plan_kind_default_expire_days
                ORDER BY k.ticket_plan_kind_sn;
            """;

            using (var conn = _factory.CreateConnection())
            {
                var command = new CommandDefinition(sql, cancellationToken: ct);
                var rows = await conn.QueryAsync<TicketPlanResult>(command);
                return rows.AsList();
            }
                
        }
    }
}
