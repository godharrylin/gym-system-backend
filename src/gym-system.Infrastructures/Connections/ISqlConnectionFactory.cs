using System.Data;

namespace gym_system.Infrastructures.Connections
{
    public interface ISqlConnectionFactory
    {
        IDbConnection CreateConnection();
    }
}
