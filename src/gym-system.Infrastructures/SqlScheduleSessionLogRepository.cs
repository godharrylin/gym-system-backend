using Dapper;
using gym_system.Domain.Entities.ScheduleSessions;
using gym_system.Domain.Repositories;
using gym_system.Infrastructures.Connections;

namespace gym_system.Infrastructures
{
    internal sealed class SqlScheduleSessionLogRepository : IScheduleSessionLogRepository
    {
        private readonly ISqlSession _session;

        public SqlScheduleSessionLogRepository(ISqlSession session)
        {
            _session = session;
        }

        public async Task AddAsync(ScheduleSessionLog log, CancellationToken ct)
        {
            const string sql = """
                INSERT INTO dbo.cls_scdle_arnge_log (
                    cls_scdle_arnge_sn,
                    cls_scdle_changed_data,
                    operator_id,
                    cls_scdle_arnge_log_remark
                )
                VALUES (
                    @ArrangeSn,
                    @ChangedData,
                    @OperatorId,
                    @Remark
                );
                """;

            var cmd = new CommandDefinition(
                sql,
                new
                {
                    log.ArrangeSn,
                    log.ChangedData,
                    log.OperatorId,
                    log.Remark
                },
                transaction: _session.Transaction,
                cancellationToken: ct);

            await _session.Connection.ExecuteAsync(cmd);
        }
    }
}
