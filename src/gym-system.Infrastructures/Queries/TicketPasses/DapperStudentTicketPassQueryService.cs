using Dapper;
using gym_system.Application.TicketsUseCase.Queries;
using gym_system.Infrastructures.Connections;

namespace gym_system.Infrastructures.Queries.TicketPasses
{
    internal sealed class DapperStudentTicketPassQueryService : IStudentTicketPassQueryService
    {
        private readonly ISqlConnectionFactory _factory;

        public DapperStudentTicketPassQueryService(ISqlConnectionFactory factory)
        {
            _factory = factory;
        }

        public async Task<IReadOnlyList<StudentTicketPassResult>> GetByStudentIdAsync(
            string studentId,
            CancellationToken ct)
        {
            const string sql = """
                SELECT
                    p.pass_id AS PassId,
                    p.ticket_plan_kind_code AS PlanCode,
                    COALESCE(k.ticket_plan_kind_cname, i.order_items_name) AS PlanName,
                    p.ticket_plan_kind_type AS PlanType,
                    p.valid_status AS ValidStatus,
                    i.order_items_payment_state AS PaymentStatus,
                    i.order_items_unit_price AS UnitPrice,
                    i.order_items_paid_at AS PaidAt,
                    p.valid_sdate AS ValidStartDate,
                    p.valid_edate AS ValidEndDate,
                    p.credits_total AS CreditsTotal,
                    p.credits_remaining AS CreditsRemaining,
                    CAST(CASE
                        WHEN p.renewed_from_pass_sn IS NOT NULL
                         AND p.valid_status = 'UnActive'
                         AND
                         (
                             p.ticket_plan_kind_type <> 'PACK'
                             OR p.credits_remaining = p.credits_total
                         )
                        THEN 1 ELSE 0
                    END AS bit) AS CanCancel
                FROM dbo.sdt_ticket_pass AS p
                INNER JOIN dbo.order_items AS i
                    ON i.order_items_sn = p.order_items_sn
                LEFT JOIN dbo.ticket_plan_kind AS k
                    ON k.ticket_plan_kind_code = p.ticket_plan_kind_code
                WHERE p.owner_id = @studentId
                  AND i.order_items_payment_state = 'Paid'
                  AND i.order_items_paid_at IS NOT NULL
                ORDER BY i.order_items_paid_at DESC, p.pass_sn DESC;
                """;

            using var connection = _factory.CreateConnection();
            var rows = await connection.QueryAsync<StudentTicketPassResult>(
                new CommandDefinition(
                    sql,
                    new { studentId },
                    cancellationToken: ct));
            return rows.AsList();
        }
    }
}
