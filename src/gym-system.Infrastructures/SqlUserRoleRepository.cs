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

        public async Task<IReadOnlyList<UserRole>> GetActiveRolesAsync(string userId, CancellationToken ct)
        {
            const string sql = """
                SELECT
                    ur.usr_id,
                    br.bmc_role_code,
                    ur.user_role_cdt,
                    ur.user_role_is_active
                FROM dbo.user_role AS ur
                INNER JOIN dbo.bmc_role AS br
                    ON ur.bmc_role_id = br.bmc_role_id
                WHERE ur.usr_id = @usr_id
                    AND ur.user_role_is_active = 1
                ORDER BY br.bmc_role_code
                """;

            var rows = await _session.Connection.QueryAsync<RoleRow>(
                new CommandDefinition(
                    sql,
                    new { usr_id = userId },
                    transaction: _session.Transaction,
                    cancellationToken: ct));

            return rows
                .Select(row => UserRole.Assign(
                    row.usr_id,
                    Enum.Parse<UserRoleCode>(row.bmc_role_code, ignoreCase: true),
                    row.user_role_cdt,
                    row.user_role_is_active))
                .ToList()
                .AsReadOnly();
        }

        public async Task<bool> AddRoleAsync(UserRole userRole, CancellationToken ct)
        {
            const string sql = """
                    INSERT INTO dbo.user_role
                        (usr_id, bmc_role_id, user_role_is_active, user_role_cdt)
                    SELECT
                        @usr_id,
                        br.bmc_role_id,
                        @user_role_is_active,
                        @user_role_cdt
                    FROM dbo.bmc_role AS br
                    WHERE br.bmc_role_code = @bmc_role_code
                """;
            try
            {
                var cmd = new CommandDefinition(
                    sql,
                    new
                    {
                        usr_id = userRole.UserId,
                        bmc_role_code = userRole.RoleCode.ToString(),
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
            catch (SqlException)
            {
                // 3. 建議至少要在這裡紀錄 Log，否則除了重複外的錯誤都會讓你不知所措
                // _logger.LogError(ex, "Insert user role failed.");
                throw;
            }
        }
        public async Task<bool> ReactivateRoleAsync(string userId, UserRoleCode roleType, CancellationToken ct)
        {
            const string sql = """
                    UPDATE ur
                    SET ur.user_role_is_active = 1,
                        ur.user_role_upd_dt = SYSDATETIME()
                    FROM dbo.user_role AS ur
                    INNER JOIN dbo.bmc_role AS br
                        ON ur.bmc_role_id = br.bmc_role_id
                    WHERE ur.usr_id = @usr_id
                        AND br.bmc_role_code = @bmc_role_code
                """;

            var cmd = new CommandDefinition(
                sql,
                new { usr_id = userId, bmc_role_code = roleType.ToString() },
                transaction: _session.Transaction,
                cancellationToken: ct
            );

            var affected = await _session.Connection.ExecuteAsync(cmd);
            return affected > 0;
        }

        public async Task<bool> SetRoleActiveAsync(string userId, UserRoleCode roleType, bool isActive, CancellationToken ct)
        {
            const string sql = """
                    UPDATE ur
                    SET ur.user_role_is_active = @isActive,
                        ur.user_role_upd_dt = SYSDATETIME()
                    FROM dbo.user_role AS ur
                    INNER JOIN dbo.bmc_role AS br
                        ON ur.bmc_role_id = br.bmc_role_id
                    WHERE ur.usr_id = @usr_id
                        AND br.bmc_role_code = @bmc_role_code
                """;

            var cmd = new CommandDefinition(
                sql,
                new { usr_id = userId, bmc_role_code = roleType.ToString(), isActive },
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
