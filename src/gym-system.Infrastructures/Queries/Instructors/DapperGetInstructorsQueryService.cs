using Dapper;
using gym_system.Application.InstructorUseCase.Queries;
using gym_system.Infrastructures.Connections;

namespace gym_system.Infrastructures.Queries.Instructors
{
    internal sealed class DapperGetInstructorsQueryService : IInstructorQueryService 
    {
        private readonly ISqlConnectionFactory _connectionFactory;
        public DapperGetInstructorsQueryService(ISqlConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }


        public async Task<IReadOnlyList<InstrucotrResult>> GetInstructorsAsync(CancellationToken ct)
        {
            const string sql = """
                    SELECT
                        u.usr_id
                        ,u.usr_name
                        ,u.usr_phone
                        ,ur.user_role_is_active
                    FROM dbo.users AS u
                    INNER JOIN dbo.user_role AS ur
                        ON ur.usr_id = u.usr_id
                    INNER JOIN dbo.bmc_role AS br
                        ON br.bmc_role_id = ur.bmc_role_id
                    WHERE br.bmc_role_code = @roleCode
                    ORDER BY u.usr_id;
                """;

            using var conn = _connectionFactory.CreateConnection();
            var cmd = new CommandDefinition(
                sql,
                new { roleCode = "Instructor" },
                cancellationToken: ct
            );

            var rows = await conn.QueryAsync<InstrucotrResult>(cmd);
            return rows.AsList();
        }
    }
}
