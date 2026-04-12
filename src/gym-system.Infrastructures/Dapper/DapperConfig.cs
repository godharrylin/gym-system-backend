using Dapper;
using gym_system.Infrastructures.Dapper.TypeHandler;

namespace gym_system.Infrastructures.Dapper
{
    internal class DapperConfig
    {
        public static void Register()
        {
            SqlMapper.AddTypeHandler(new StringArrayHandler());
        }
    }
}
