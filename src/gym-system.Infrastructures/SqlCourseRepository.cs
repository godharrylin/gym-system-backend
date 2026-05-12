using Dapper;
using gym_system.Domain.Entities.Courses;
using gym_system.Domain.Repositories;
using gym_system.Infrastructures.Connections;

namespace gym_system.Infrastructures
{
    internal class SqlCourseRepository : ICourseRepository
    {
        private readonly ISqlSession _session;
        public SqlCourseRepository(ISqlSession session) 
        {
            _session = session;
        }

        public async Task<Course?> GetByIdAsync(string courseId, CancellationToken ct)
        {
            const string sql = """
                    SELECT
                        class_id
                        ,class_name
                        ,class_label_color
                        ,class_duration
                        ,class_is_free
                        ,class_default_instructor_id
                        ,class_default_instructor_name
                        ,class_is_active
                        ,class_type
                    FROM dbo.class
                    WHERE
                        class_id = @courseId
                """;

            var cmd = new CommandDefinition(
                sql,
                new { courseId },
                transaction: _session.Transaction,
                cancellationToken: ct);

            var row = await _session.Connection.QueryFirstOrDefaultAsync<CourseRow>(cmd);
            if (row is null) return null;

            return Course.Rehydrate(
                id: row.class_id, 
                name: row.class_name, 
                labelColor: row.class_label_color,
                duration: row.class_duration,
                isFree: row.class_is_free, 
                instructorId: row.class_default_instructor_id, 
                instructorName: row.class_default_instructor_name,
                isActive: row.class_is_active, 
                type: row.class_type);
        }
        public async Task <bool> UpdateAsync(Course course, CancellationToken ct)
        {
            const string sql = """
                UPDATE dbo.class
                SET class_name = @Name,
                    class_label_color = @LabelColor,
                    class_duration = @Duration,
                    class_is_free = @IsFree,
                    class_default_instructor_id = @DefaultInstructorId,
                    class_default_instructor_name = @DefaultInstructorName,
                    class_is_active = @IsActive,
                    class_type = @Type
                WHERE class_id = @Id;
                """;

            var affected = await _session.Connection.ExecuteAsync(new CommandDefinition(
                                    sql,
                                    new{
                                        course.Id,
                                        course.Name,
                                        course.LabelColor,
                                        course.Duration,
                                        course.IsFree,
                                        course.DefaultInstructorId,
                                        course.DefaultInstructorName,
                                        course.IsActive,
                                        course.Type
                                    },
                                    transaction: _session.Transaction,
                                    cancellationToken: ct));
            return affected > 0;
        }

        private sealed record CourseRow
        {
            public required string class_id { get; set; }
            public string class_name { get; set; } = string.Empty;
            public string class_label_color { get; set; } = string.Empty;
            public int class_duration { get; set; }
            public bool class_is_free { get; set; }
            public string class_default_instructor_id { get; set; } = string.Empty;
            public string class_default_instructor_name { get; set; } = string.Empty;
            public bool class_is_active { get; set; }
            public string class_type { get; set; } = string.Empty;
        }
    }
}
