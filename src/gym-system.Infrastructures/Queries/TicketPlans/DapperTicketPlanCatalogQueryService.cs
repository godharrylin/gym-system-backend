using Dapper;
using gym_system.Application.TicketPlansUseCase.Queries;
using gym_system.Infrastructures.Connections;

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
                ticket_plan_kind_code AS Id,
                ticket_plan_kind_cname AS Name,
                ticket_plan_kind_price AS Price,
                ticket_plan_kind_default_expire_days AS Days,
                ticket_plan_kind_default_credit AS Sessions,
                CASE ticket_plan_kind_type
                    WHEN 'PACK' THEN 'SESSION'
                    WHEN 'M_PASS' THEN 'MONTHLY'
                    ELSE ticket_plan_kind_type
                    END AS Type
                FROM dbo.ticket_plan_kind
                WHERE ticket_plan_kind_default_is_active = 'Y'
                ORDER BY ticket_plan_kind_sn;
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
