using Dapper;
using gym_system.Domain.Entities.Users;
using gym_system.Domain.Enums;
using gym_system.Domain.Repositories;
using gym_system.Infrastructures.Connections;
using Microsoft.Data.SqlClient;

namespace gym_system.Infrastructures
{
    internal sealed class SqlUserRoleRepository : IUserRoleRepository
    {
        private readonly ISqlSession _session;

        public SqlUserRoleRepository(ISqlSession session) 
        {
            _session = session;
        }

        public async Task<UserRole?> GetUserRoleAsync(string userId, UserRoleCode roleType, CancellationToken ct)
        {
            var sql = """
                SELECT 
                    ur.usr_id
                    ,br.bmc_role_code
                    ,ur.user_role_cdt
                    ,ur.user_role_is_active
                FROM user_role AS ur
                INNER JOIN bmc_role AS br 
                ON ur.bmc_role_id = br.bmc_role_id
                WHERE 1=1
                    AND ur.usr_id = @usr_id
                    AND br.bmc_role_code = @bmc_role_code
            """;

            var cmd = new CommandDefinition(
                sql,
                new { usr_id = userId, bmc_role_code = roleType.ToString() },
                transaction: _session.Transaction,
                cancellationToken: ct);

            var row = await _session.Connection.QueryFirstOrDefaultAsync<RoleRow>(cmd);
            if (row is null) return null;

            var code = Enum.Parse<UserRoleCode>(row.bmc_role_code, ignoreCase: true);
            return UserRole.Assign(row.usr_id, code, row.user_role_cdt, row.user_role_is_active);
        }
        public async Task<bool> AddRoleAsync(UserRole userRole, CancellationToken ct)
        {
            var sql = """
                    INSERT INTO user_role 
                        (usr_id, bmc_role_id, user_role_is_active, user_role_cdt)
                    VALUES
                        (@usr_id, @bmc_role_id, @user_role_is_active, @user_role_cdt)
                """;
            try
            {
                var cmd = new CommandDefinition(
                    sql,
                    new
                    {
                        usr_id = userRole.UserId,
                        bmc_role_id = userRole.RoleCode,
                        user_role_is_active = userRole.IsActive,
                        user_role_cdt = userRole.AssignedAt
                    },
                    transaction: _session.Transaction,
                    cancellationToken: ct
                );

                var affected = await _session.Connection.ExecuteAsync(cmd);
                return affected >= 1;
            }
            
            catch(SqlException ex) when (ex.Number is 2601 or 2627)
            {
                // 冪等性處理：重複視為成功
                return true;
            }
            catch (SqlException ex)
            {
                // 3. 建議至少要在這裡紀錄 Log，否則除了重複外的錯誤都會讓你不知所措
                // _logger.LogError(ex, "Insert user role failed.");
                throw;
            }
        }
        public async Task<bool> ReactiveRole(string userId, UserRoleCode roleType, CancellationToken ct)
        {
            var sql = """
                    UPDATE dbo.user_role
                    SET user_role_is_active = 1
                    WHERE 1=1
                    AND usr_id = @usr_id
                    AND bmc_role_id = @bmc_role_id
                """;

            var cmd = new CommandDefinition(
                sql,
                new { usr_id = userId, bmc_role_id = roleType },
                transaction: _session.Transaction,
                cancellationToken: ct
            );

            var affected = await _session.Connection.ExecuteAsync(cmd);
            return affected > 0;
        }

        public async Task<bool> SetRoleActiveAsync(string userId, UserRoleCode roleType, bool isActive, CancellationToken ct)
        {
            var sql = """
                    UPDATE dbo.user_role
                    SET user_role_is_active = @isActive
                    WHERE 1=1
                    AND usr_id = @usr_id
                    AND bmc_role_id = @bmc_role_id
                """;

            var cmd = new CommandDefinition(
                sql,
                new { usr_id = userId, bmc_role_id = roleType, isActive },
                transaction: _session.Transaction,
                cancellationToken: ct
            );

            var affected = await _session.Connection.ExecuteAsync(cmd);
            return affected > 0;
        }
        private sealed class RoleRow
        {
            public string usr_id { get; init; } = string.Empty;
            public string bmc_role_code { get; init; } = string.Empty;
            public bool user_role_is_active { get; init; }
            public DateTime user_role_cdt { get; init; }
        }
    }
}
