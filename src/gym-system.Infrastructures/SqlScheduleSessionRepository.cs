using Dapper;
using gym_system.Domain.Entities.ScheduleSessions;
using gym_system.Domain.Repositories;
using gym_system.Infrastructures.Connections;

namespace gym_system.Infrastructures
{
    internal sealed class SqlScheduleSessionRepository : IScheduleSessionRepository
    {
        private readonly ISqlSession _session;

        public SqlScheduleSessionRepository(ISqlSession session)
        {
            _session = session;
        }

        public async Task<IReadOnlyList<ScheduleSession>> GetByWeekAsync(DateOnly weekStart, DateOnly weekEnd, CancellationToken ct)
        {
            const string sql = """
                SELECT
                    a.cls_scdle_arnge_sn,
                    a.cls_scdle_arnge_id,
                    a.cls_scdle_date,
                    a.class_id,
                    a.class_name,
                    a.class_label_color,
                    a.cls_scdle_arnge_instructor_id,
                    a.instructor_name,
                    a.cls_scdle_arnge_st,
                    a.cls_scdle_arnge_et,
                    a.cls_scdle_status,
                    a.cls_scdle_source,
                    a.cls_scdle_rules_sn,
                    CAST(ISNULL(c.class_is_free, 0) AS bit) AS class_is_free
                FROM dbo.cls_scdle_arnge a
                LEFT JOIN dbo.class c ON c.class_id = a.class_id
                WHERE a.cls_scdle_date >= @WeekStart
                  AND a.cls_scdle_date <= @WeekEnd
                ORDER BY a.cls_scdle_arnge_st;
                """;

            var cmd = new CommandDefinition(
                sql,
                new
                {
                    WeekStart = weekStart.ToDateTime(TimeOnly.MinValue),
                    WeekEnd = weekEnd.ToDateTime(TimeOnly.MinValue)
                },
                transaction: _session.Transaction,
                cancellationToken: ct);

            var rows = await _session.Connection.QueryAsync<ScheduleSessionRow>(cmd);
            return rows.Select(ToSession).ToList();
        }

        public async Task<IReadOnlyList<ScheduleSessionTemplate>> GetActiveTemplatesAsync(CancellationToken ct)
        {
            const string sql = """
                SELECT
                    r.cls_scdle_rules_sn,
                    r.class_id,
                    c.class_name,
                    c.class_label_color,
                    c.class_is_free,
                    r.cls_scdle_rules_day_wk,
                    r.cls_scdle_rules_st,
                    r.cls_scdle_rules_et,
                    r.cls_scdle_instructor_id,
                    COALESCE(u.usr_name, c.class_default_instructor_name, N'') AS instructor_name
                FROM dbo.cls_scdle_rules r
                INNER JOIN dbo.class c ON c.class_id = r.class_id
                LEFT JOIN dbo.users u ON u.usr_id = r.cls_scdle_instructor_id
                WHERE r.cls_scdle_rules_is_active = 1
                  AND c.class_is_active = 1
                ORDER BY r.cls_scdle_rules_day_wk, r.cls_scdle_rules_st;
                """;

            var cmd = new CommandDefinition(
                sql,
                transaction: _session.Transaction,
                cancellationToken: ct);

            var rows = await _session.Connection.QueryAsync<ScheduleSessionTemplateRow>(cmd);
            return rows.Select(ToTemplate).ToList();
        }

        public async Task AddRangeAsync(IReadOnlyList<ScheduleSession> sessions, CancellationToken ct)
        {
            const string sql = """
                INSERT INTO dbo.cls_scdle_arnge (
                    cls_scdle_date,
                    class_id,
                    class_name,
                    class_label_color,
                    cls_scdle_arnge_instructor_id,
                    instructor_name,
                    cls_scdle_arnge_st,
                    cls_scdle_arnge_et,
                    cls_scdle_status,
                    cls_scdle_source,
                    cls_scdle_rules_sn
                )
                SELECT
                    @Date,
                    @ClassId,
                    @ClassName,
                    @ClassLabelColor,
                    @InstructorId,
                    @InstructorName,
                    @StartAt,
                    @EndAt,
                    @Status,
                    @Source,
                    @RuleSn
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM dbo.cls_scdle_arnge WITH (UPDLOCK, HOLDLOCK)
                    WHERE cls_scdle_rules_sn = @RuleSn
                      AND cls_scdle_date = @Date
                );
                """;

            foreach (var session in sessions)
            {
                var cmd = new CommandDefinition(
                    sql,
                    new
                    {
                        Date = session.Date.ToDateTime(TimeOnly.MinValue),
                        session.ClassId,
                        session.ClassName,
                        ClassLabelColor = session.ClassLabelColor,
                        InstructorId = session.InstructorId,
                        session.InstructorName,
                        StartAt = session.StartAt,
                        EndAt = session.EndAt,
                        session.Status,
                        session.Source,
                        session.RuleSn
                    },
                    transaction: _session.Transaction,
                    cancellationToken: ct);

                await _session.Connection.ExecuteAsync(cmd);
            }
        }

        private static ScheduleSession ToSession(ScheduleSessionRow row)
        {
            return ScheduleSession.Rehydrate(
                arrangeSn: row.cls_scdle_arnge_sn,
                arrangeId: row.cls_scdle_arnge_id,
                date: DateOnly.FromDateTime(row.cls_scdle_date),
                classId: row.class_id,
                className: row.class_name,
                classLabelColor: row.class_label_color,
                instructorId: row.cls_scdle_arnge_instructor_id,
                instructorName: row.instructor_name,
                startAt: row.cls_scdle_arnge_st,
                endAt: row.cls_scdle_arnge_et,
                status: row.cls_scdle_status,
                source: row.cls_scdle_source,
                ruleSn: row.cls_scdle_rules_sn,
                isFree: row.class_is_free);
        }

        private static ScheduleSessionTemplate ToTemplate(ScheduleSessionTemplateRow row)
        {
            return ScheduleSessionTemplate.Rehydrate(
                ruleSn: row.cls_scdle_rules_sn,
                classId: row.class_id,
                className: row.class_name,
                classLabelColor: row.class_label_color,
                isFree: row.class_is_free,
                dayOfWeek: (DayOfWeek)row.cls_scdle_rules_day_wk,
                startTime: row.cls_scdle_rules_st,
                endTime: row.cls_scdle_rules_et,
                instructorId: row.cls_scdle_instructor_id,
                instructorName: row.instructor_name);
        }

        private sealed record ScheduleSessionRow
        {
            public int cls_scdle_arnge_sn { get; set; }
            public string cls_scdle_arnge_id { get; set; } = string.Empty;
            public DateTime cls_scdle_date { get; set; }
            public string class_id { get; set; } = string.Empty;
            public string class_name { get; set; } = string.Empty;
            public string class_label_color { get; set; } = string.Empty;
            public string cls_scdle_arnge_instructor_id { get; set; } = string.Empty;
            public string instructor_name { get; set; } = string.Empty;
            public DateTime cls_scdle_arnge_st { get; set; }
            public DateTime cls_scdle_arnge_et { get; set; }
            public string cls_scdle_status { get; set; } = string.Empty;
            public string cls_scdle_source { get; set; } = string.Empty;
            public string cls_scdle_rules_sn { get; set; } = string.Empty;
            public bool class_is_free { get; set; }
        }

        private sealed record ScheduleSessionTemplateRow
        {
            public string cls_scdle_rules_sn { get; set; } = string.Empty;
            public string class_id { get; set; } = string.Empty;
            public string class_name { get; set; } = string.Empty;
            public string class_label_color { get; set; } = string.Empty;
            public bool class_is_free { get; set; }
            public int cls_scdle_rules_day_wk { get; set; }
            public TimeSpan cls_scdle_rules_st { get; set; }
            public TimeSpan cls_scdle_rules_et { get; set; }
            public string cls_scdle_instructor_id { get; set; } = string.Empty;
            public string instructor_name { get; set; } = string.Empty;
        }
    }
}
