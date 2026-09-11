using Dapper;
using gym_system.Application.TicketPlansUseCase.Queries;
using gym_system.Infrastructures.Connections;

namespace gym_system.Infrastructures.Queries.TicketPlans
{
    internal sealed class DapperStudentTicketPurchaseHistoryQueryService : IStudentTicketPurchaseHistoryQueryService
    {
        private readonly ISqlConnectionFactory _factory;

        public DapperStudentTicketPurchaseHistoryQueryService(ISqlConnectionFactory factory)
        {
            _factory = factory;
        }

        public async Task<bool> HasPurchasedTicketPlanAsync(
            string studentId,
            string ticketPlanCode,
            CancellationToken ct)
        {
            const string sql = """
                SELECT
                    CAST(
                        CASE WHEN EXISTS (
                            SELECT 1
                            FROM dbo.sdt_ticket_pass AS pass
                            WHERE pass.owner_id = @studentId
                                AND pass.ticket_plan_kind_code = @ticketPlanCode
                        )
                        THEN 1 ELSE 0 END
                    AS bit);
                """;

            using var conn = _factory.CreateConnection();
            return await conn.QuerySingleAsync<bool>(
                new CommandDefinition(
                    sql,
                    new
                    {
                        studentId,
                        ticketPlanCode
                    },
                    cancellationToken: ct));
        }
    }
}
