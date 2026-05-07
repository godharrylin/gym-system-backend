using System.Data;
using gym_system.Domain.Repositories;
using gym_system.Infrastructures.Connections;
using Microsoft.Data.SqlClient;

namespace gym_system.Infrastructures
{
    internal sealed class SqlUnitOfWork : IUnitOfWork
    {
        private readonly ISqlSession _session;

        public SqlUnitOfWork(ISqlSession session)
        {
            _session = session;
        }

        public async Task BeginAsync(CancellationToken ct)
        {
            if (_session.Transaction is not null) return;

            //  Method 1: 先用非同步方式開啟連線和Transaction
            if (_session.Connection is SqlConnection sqlConn)
            {
                if (sqlConn.State != ConnectionState.Open)
                    await sqlConn.OpenAsync(ct);

                _session.Transaction = await sqlConn.BeginTransactionAsync(ct);
                return;
            }

            //  Method 2: 如果非同步建立失敗，改用同步方式建立連線和Transaction
            if (_session.Connection.State != ConnectionState.Open)
                _session.Connection.Open();

            _session.Transaction = _session.Connection.BeginTransaction();
        }

        public Task CommitAsync(CancellationToken ct)
        {
            if (_session.Transaction is null) return Task.CompletedTask;
            _session.Transaction.Commit();
            _session.Transaction.Dispose();
            _session.Transaction = null;
            return Task.CompletedTask;
        }

        public Task RollbackAsync(CancellationToken ct)
        {
            if (_session.Transaction is null) return Task.CompletedTask;
            _session.Transaction.Rollback();
            _session.Transaction.Dispose();
            _session.Transaction = null;
            return Task.CompletedTask;
        }
    }
}
