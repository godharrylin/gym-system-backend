using Dapper;
using gym_system.Application.CoursesUseCase.Queries;
using gym_system.Infrastructures.Connections;
using System.Text;

namespace gym_system.Infrastructures.Queries.Courses
{
    internal sealed class DapperCourseCatalogQueryService : ICourseCatalogQueryService
    {
        private readonly ISqlConnectionFactory _connectionFactory;

        public DapperCourseCatalogQueryService(ISqlConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task<IReadOnlyList<CourseResult>> GetClassesAsync(bool? isActive, CancellationToken ct)
        {
            var sql = new StringBuilder();
            sql.Append("""
                SELECT
                    c.class_sn,
                    c.class_name,
                    u.usr_name,
                    c.class_label_color,
                    c.class_duration,
                    c.class_is_free,
                    c.class_is_active,
                    c.class_type
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
            var rows = await conn.QueryAsync<CourseResult>(cmd);

            return rows.AsList();
        }
    }
}
