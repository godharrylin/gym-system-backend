using Dapper;
using gym_system.Application.ScheduleRulesUseCase.Queries;
using gym_system.Infrastructures.Connections;
using System;
using System.Collections.Generic;
using System.Text;

namespace gym_system.Infrastructures.Queries.ScheduleRules
{
    internal class DapperGetScheduleRulesQueryService : IScheduleRulesQueryService
    {
        private readonly ISqlConnectionFactory _connectionFactory;
        public DapperGetScheduleRulesQueryService(ISqlConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task<IReadOnlyList<ScheduleRulesResult>> GetScheduleRulesAsync(CancellationToken ct)
        {
            const string sql = """
                        SELECT 
                            cls_scdle_rules_sn
                            ,class_id
                            ,cls_scdle_rules_day_wk
                            ,cls_scdle_rules_st
                            ,cls_scdle_duration
                            ,cls_scdle_rules_et
                            ,cls_scdle_rules_buffer_time
                            ,cls_scdle_instructor_id
                            ,cls_scdle_rules_is_active
                        FROM cls_scdle_rules
                """;
            using var conn = _connectionFactory.CreateConnection();
            var cmd = new CommandDefinition(
                        sql,
                        cancellationToken: ct);
            var rows = await conn.QueryAsync<ScheduleRulesResult>(cmd);
            return rows.AsList();
        }
    }
}
