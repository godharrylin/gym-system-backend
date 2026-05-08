using Dapper;
using gym_system.Application.ClassesUseCase.Queries;
using gym_system.Infrastructures.Connections;
using System.Text;

namespace gym_system.Infrastructures.Queries.Classes
{
    internal sealed class DapperClassCatalogQueryService : IClassCatalogQueryService
    {
        private readonly ISqlConnectionFactory _connectionFactory;

        public DapperClassCatalogQueryService(ISqlConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task<IReadOnlyList<ClassResult>> GetClassesAsync(bool? isActive, CancellationToken ct)
        {
            var sql = new StringBuilder();
            sql.Append("""
                SELECT
                    c.class_sn,
                    COALESCE(c.class_name, ''),
                    COALESCE(u.usr_name, ''),
                    COALESCE(c.class_label_color, ''),
                    COALESCE(c.class_duration, 0),
                    c.class_is_free,
                    c.class_is_active,
                    COALESCE(c.class_type, '')
                FROM dbo.[class] c
                LEFT JOIN dbo.users u
                    ON u.usr_id = c.class_instructor_id
                WHERE 1=1
            """);

            var param = new DynamicParameters();

            if (isActive.HasValue)
            {
                sql.Append(" AND c.class_is_active = @isActiveFlag ");
                param.Add("isActiveFlag", isActive.Value);
            }

            sql.Append(" ORDER BY c.class_sn;");

            using var conn = _connectionFactory.CreateConnection();
            var cmd = new CommandDefinition(
                sql.ToString(),
                param,
                cancellationToken: ct
            );
            var rows = await conn.QueryAsync<ClassResult>(cmd);

            return rows.AsList();
        }
    }
}
