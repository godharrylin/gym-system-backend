using System.Data;

namespace gym_system.Infrastructures.Connections
{
    internal interface ISqlSession
    {
        IDbConnection Connection { get; }
        IDbTransaction? Transaction { get; set; }
    }
}
