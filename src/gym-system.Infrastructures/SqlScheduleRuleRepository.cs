using Dapper;
using gym_system.Domain.Entities.ScheduleRules;
using gym_system.Domain.Repositories;
using gym_system.Infrastructures.Connections;
using System.Globalization;

namespace gym_system.Infrastructures
{
    internal class SqlScheduleRuleRepository : IScheduleRuleRepository
    {
        private readonly ISqlSession _session;

        public SqlScheduleRuleRepository(ISqlSession session)
        {
            _session = session;
        }

        public async Task<bool> AddAsync(ScheduleRule newScheduleRule, CancellationToken ct)
        {
            const string sql = """
                INSERT INTO dbo.cls_scdle_rules (
                    class_id,
                    cls_scdle_rules_day_wk,
                    cls_scdle_rules_st,
                    cls_scdle_duration,
                    cls_scdle_rules_et,
                    cls_scdle_rules_buffer_time,
                    cls_scdle_instructor_id,
                    cls_scdle_rules_is_active
                ) VALUES (
                    @ClassId,
                    @DayOfWeek,
                    @StartTime,
                    @Duration,
                    @EndTime,
                    @BufferTime,
                    @InstructorId,
                    @IsActive
                );
                """;

            var cmd = new CommandDefinition(
                sql,
                new
                {
                    newScheduleRule.ClassId,
                    newScheduleRule.DayOfWeek,
                    newScheduleRule.StartTime,
                    newScheduleRule.Duration,
                    newScheduleRule.EndTime,
                    newScheduleRule.BufferTime,
                    newScheduleRule.InstructorId,
                    newScheduleRule.IsActive
                },
                transaction: _session.Transaction,
                cancellationToken: ct);

            var affected = await _session.Connection.ExecuteAsync(cmd);
            return affected > 0;
        }

        public async Task<ScheduleRule?> GetByIdAsync(string ruleSn, CancellationToken ct)
        {
            const string sql = """
                SELECT TOP 1
                    cls_scdle_rules_sn,
                    class_id,
                    cls_scdle_rules_day_wk,
                    cls_scdle_rules_st,
                    cls_scdle_duration,
                    cls_scdle_rules_et,
                    cls_scdle_rules_buffer_time,
                    cls_scdle_instructor_id,
                    cls_scdle_rules_is_active
                FROM dbo.cls_scdle_rules
                WHERE cls_scdle_rules_sn = @RuleSn;
                """;

            var cmd = new CommandDefinition(
                sql,
                new { RuleSn = ruleSn },
                transaction: _session.Transaction,
                cancellationToken: ct);

            var row = await _session.Connection.QueryFirstOrDefaultAsync<ScheduleRuleRow>(cmd);
            if (row is null) return null;

            return ScheduleRule.Rehydrate(
                row.cls_scdle_rules_sn.ToString(CultureInfo.InvariantCulture),
                row.class_id,
                (System.DayOfWeek)row.cls_scdle_rules_day_wk,
                row.cls_scdle_rules_st,
                row.cls_scdle_duration,
                row.cls_scdle_rules_et,
                row.cls_scdle_rules_buffer_time,
                row.cls_scdle_instructor_id,
                row.cls_scdle_rules_is_active);
        }

        public async Task<bool> UpdateAsync(ScheduleRule scheduleRule, CancellationToken ct)
        {
            const string sql = """
                UPDATE dbo.cls_scdle_rules
                SET
                    class_id = @ClassId,
                    cls_scdle_rules_day_wk = @DayOfWeek,
                    cls_scdle_rules_st = @StartTime,
                    cls_scdle_duration = @Duration,
                    cls_scdle_rules_et = @EndTime,
                    cls_scdle_rules_buffer_time = @BufferTime,
                    cls_scdle_instructor_id = @InstructorId,
                    cls_scdle_rules_is_active = @IsActive
                WHERE cls_scdle_rules_sn = @RuleSn;
                """;

            var cmd = new CommandDefinition(
                sql,
                new
                {
                    RuleSn = scheduleRule.Sn,
                    scheduleRule.ClassId,
                    scheduleRule.DayOfWeek,
                    scheduleRule.StartTime,
                    scheduleRule.Duration,
                    scheduleRule.EndTime,
                    scheduleRule.BufferTime,
                    scheduleRule.InstructorId,
                    scheduleRule.IsActive
                },
                transaction: _session.Transaction,
                cancellationToken: ct);

            var affected = await _session.Connection.ExecuteAsync(cmd);
            return affected > 0;
        }

        public async Task<bool> DeleteAsync(string ruleSn, CancellationToken ct)
        {
            const string sql = """
                DELETE FROM dbo.cls_scdle_rules
                WHERE cls_scdle_rules_sn = @RuleSn;
                """;

            var cmd = new CommandDefinition(
                sql,
                new { RuleSn = ruleSn },
                transaction: _session.Transaction,
                cancellationToken: ct);

            var affected = await _session.Connection.ExecuteAsync(cmd);
            return affected > 0;
        }

        /// <summary>
        /// 找上課時段部分重疊的排課
        /// </summary>
        /// <param name="newScheduleRule"></param>
        /// <param name="ct"></param>
        /// <remarks>因為教室目前只有一個，所以這個方法目前主要用來看是否上課時段有部分重疊</remarks>
        /// <returns></returns>
        public async Task<ScheduleRule?> GetOverlappingSchedulesRuleAsync(ScheduleRule newScheduleRule, CancellationToken ct, string? excludeRuleSn = null)
        {
            var sql = """
                SELECT TOP 1
                    cls_scdle_rules_sn,
                    class_id,
                    cls_scdle_rules_day_wk,
                    cls_scdle_rules_st,
                    cls_scdle_duration,
                    cls_scdle_rules_et,
                    cls_scdle_rules_buffer_time,
                    cls_scdle_instructor_id,
                    cls_scdle_rules_is_active
                FROM dbo.cls_scdle_rules
                WHERE 1=1
                AND cls_scdle_rules_day_wk = @DayOfWeek
                AND cls_scdle_rules_is_active = 1
                AND @StartTime < DATEADD(MINUTE, cls_scdle_rules_buffer_time, cls_scdle_rules_et) -- 【關鍵 2】A的開始 < B的結束(含buffer)
                AND cls_scdle_rules_st < DATEADD(MINUTE, @BufferTime, @EndTime)                        -- 【關鍵 3】A的結束(含buffer) > B的開始
                     
                """;

            if (!string.IsNullOrWhiteSpace(excludeRuleSn))
            {
                sql += """
                    AND cls_scdle_rules_sn <> @ExcludeRuleSn    
                """;
            }

            var cmd = new CommandDefinition(
                sql,
                new
                {
                    newScheduleRule.DayOfWeek,
                    newScheduleRule.StartTime,
                    newScheduleRule.EndTime,
                    newScheduleRule.BufferTime,
                    ExcludeRuleSn = excludeRuleSn
                },
                transaction: _session.Transaction,
                cancellationToken: ct);

            var row = await _session.Connection.QueryFirstOrDefaultAsync<ScheduleRuleRow>(cmd);
            
            if (row is null) return null;

            return ScheduleRule.Rehydrate(
                row.cls_scdle_rules_sn.ToString(CultureInfo.InvariantCulture),
                row.class_id,
                (System.DayOfWeek)row.cls_scdle_rules_day_wk,
                row.cls_scdle_rules_st,
                row.cls_scdle_duration,
                row.cls_scdle_rules_et,
                row.cls_scdle_rules_buffer_time,
                row.cls_scdle_instructor_id,
                row.cls_scdle_rules_is_active);
        }

        private sealed record ScheduleRuleRow
        {
            public required string cls_scdle_rules_sn { get; set; }
            public string class_id { get; set; } = string.Empty;
            public int cls_scdle_rules_day_wk { get; set; }
            public TimeSpan cls_scdle_rules_st { get; set; }
            public int cls_scdle_duration { get; set; }
            public TimeSpan cls_scdle_rules_et { get; set; }
            public int cls_scdle_rules_buffer_time { get; set; }
            public string cls_scdle_instructor_id { get; set; } = string.Empty;
            public bool cls_scdle_rules_is_active { get; set; }
        }
    }
}
