using Dapper;
using gym_system.Application.TicketPlansUseCase.Queries;
using gym_system.Infrastructures.Connections;

namespace gym_system.Infrastructures.Queries.TicketPlans
{
    internal sealed class DapperTicketPlanCatalogQueryService : ITicketPlanCatalogQueryService
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
                    k.ticket_plan_family_code AS FamilyCode,
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
                    ) AS Tags,
                    COALESCE(
                        '[' + STRING_AGG(
                            CASE WHEN r.plan_rule_code IN ('NEW_ONLY', 'RENEWAL')
                                THEN '"' + r.plan_rule_code + '"'
                            END,
                            ',') + ']',
                        '[]'
                    ) AS EligibilityRuleCodes
                FROM dbo.ticket_plan_kind k
                LEFT JOIN dbo.ticket_plan_kind_rule kr
                    ON kr.ticket_plan_kind_sn = k.ticket_plan_kind_sn
                   AND kr.ticket_plan_kind_rule_is_enabled = 'Y'
                LEFT JOIN dbo.plan_rule r
                    ON r.plan_rule_sn = kr.plan_rule_sn
                   AND r.plan_rule_is_active = 'Y'
                WHERE k.ticket_plan_kind_default_is_active = 'Y'
                  AND NOT EXISTS
                  (
                      SELECT 1
                      FROM dbo.ticket_plan_kind_rule restrictiveKr
                      INNER JOIN dbo.plan_rule restrictiveRule
                          ON restrictiveRule.plan_rule_sn = restrictiveKr.plan_rule_sn
                      WHERE restrictiveKr.ticket_plan_kind_sn = k.ticket_plan_kind_sn
                        AND restrictiveRule.plan_rule_code IN ('NEW_ONLY', 'RENEWAL')
                        AND
                        (
                            restrictiveKr.ticket_plan_kind_rule_is_enabled <> 'Y'
                            OR restrictiveRule.plan_rule_is_active <> 'Y'
                        )
                  )
                  AND NOT EXISTS
                  (
                      SELECT 1
                      FROM dbo.ticket_plan_kind_rule hiddenKr
                      INNER JOIN dbo.plan_rule hiddenRule
                          ON hiddenRule.plan_rule_sn = hiddenKr.plan_rule_sn
                      WHERE hiddenKr.ticket_plan_kind_sn = k.ticket_plan_kind_sn
                        AND hiddenKr.ticket_plan_kind_rule_is_enabled = 'Y'
                        AND hiddenRule.plan_rule_is_active = 'Y'
                        AND hiddenRule.plan_rule_code = 'HIDDEN'
                  )
                GROUP BY
                    k.ticket_plan_kind_sn,
                    k.ticket_plan_kind_code,
                    k.ticket_plan_kind_type,
                    k.ticket_plan_family_code,
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
