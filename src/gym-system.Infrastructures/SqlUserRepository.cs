using Dapper;
using gym_system.Domain.Entities.Users;
using gym_system.Domain.Repositories;
using gym_system.Infrastructures.Connections;


namespace gym_system.Infrastructures
{
    internal sealed class SqlUserRepository : IUserRepository
    {
        private ISqlSession _session;
        public SqlUserRepository(ISqlSession session)
        {
            _session = session;
        }

        public async Task<IReadOnlyList<string>> GetExistingPhonesAsync(IReadOnlyList<string> phones, CancellationToken ct)
        {
            var distinctPhones = phones.Distinct().ToList();
            const string sql = """
                SELECT u.usr_phone
                FROM dbo.users AS u
                WHERE u.usr_phone IN @distinctPhones
                """;
            var result = await _session.Connection.QueryAsync<string>(
                new CommandDefinition(
                    sql,
                    new { distinctPhones },
                    transaction: _session.Transaction,
                    cancellationToken: ct)
            );

            return result.ToList().AsReadOnly();
        }
        /// <summary>
        /// 新增成員資料
        /// </summary>
        /// <param name="members"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        public async Task<string> AddAsync(User user, CancellationToken ct)
        {
            const string sql = """
                        INSERT INTO dbo.users (usr_name, usr_phone, usr_active)
                        OUTPUT INSERTED.usr_id
                        VALUES (@Name, @Phone, @IsActived)
                    """;

            var createdUserId = await _session.Connection.QuerySingleAsync<string>(
                new CommandDefinition(
                    sql,
                    user,
                    transaction: _session.Transaction,
                    cancellationToken: ct)
            );

            return createdUserId;
        }

        public async Task<User?> FindUserByIdAsync(string userId, CancellationToken ct)
        {
            const string sql = """
                        SELECT
                            u.usr_id
                            ,u.usr_name
                            ,u.usr_phone
                            ,u.usr_active
                        FROM dbo.users AS u
                        WHERE u.usr_id = @userId
                """;
            var row = await _session.Connection.QueryFirstOrDefaultAsync<UserRow>(
                new CommandDefinition(
                    sql,
                    new { userId },
                    transaction: _session.Transaction,
                    cancellationToken: ct)
            );

            return (row != null) ? User.Rehydrate(row.usr_id, row.usr_name, row.usr_phone, row.usr_phone)
                : null;
        }

        public async Task<User?> FindUserByPhone(string phone, CancellationToken ct)
        {
            var searchPhone = phone;
            const string sql = """
                        SELECT 
                            u.usr_id
                            ,usr_name
                            ,u.usr_phone
                            ,u.usr_active
                        FROM dbo.users AS u
                        WHERE u.usr_phone = @searchPhone
                """;
            var row = await _session.Connection.QueryFirstOrDefaultAsync<UserRow>(
                new CommandDefinition(
                    sql,
                    new { searchPhone },
                    transaction: _session.Transaction,
                    cancellationToken: ct)
            );

            return (row != null) ? User.Rehydrate(row.usr_id, row.usr_name, row.usr_phone, row.usr_phone)
                : null;
        }

        public async Task<bool> ExistsPhoneForOtherUserAsync(string userId, string phone, CancellationToken ct)
        {
            const string sql = """
                        SELECT COUNT(1)
                        FROM dbo.users AS u
                        WHERE u.usr_phone = @phone
                          AND u.usr_id <> @userId
                """;

            var count = await _session.Connection.QuerySingleAsync<int>(
                new CommandDefinition(
                    sql,
                    new { userId, phone },
                    transaction: _session.Transaction,
                    cancellationToken: ct)
            );

            return count > 0;
        }

        public async Task<bool> UpdateBasicProfileAsync(string userId, string name, string phone, CancellationToken ct)
        {
            const string sql = """
                        UPDATE dbo.users
                        SET usr_name = @name,
                            usr_phone = @phone
                        WHERE usr_id = @userId
                """;

            var affected = await _session.Connection.ExecuteAsync(
                new CommandDefinition(
                    sql,
                    new { userId, name, phone },
                    transaction: _session.Transaction,
                    cancellationToken: ct)
            );

            return affected > 0;
        }

        private sealed class UserRow
        {
            public string usr_id { get; init; } = string.Empty;
            public string usr_name { get; init; } = string.Empty;
            public string usr_phone { get; init; } = string.Empty;
            public bool usr_active { get; init; }

        }
    }
}
