using System;
using System.Collections.Generic;
using System.Data;
using System.Text;

namespace gym_system.Infrastructures.Connections
{
    internal class SqlSession: ISqlSession, IDisposable
    {
        public IDbConnection Connection { get; }
        public IDbTransaction? Transaction { get; set; }

        public SqlSession(ISqlConnectionFactory factory)
        {
            Connection = factory.CreateConnection();
        }

        public void Dispose()
        {
            Transaction?.Dispose();
            Connection.Dispose();
        }
    }
}
