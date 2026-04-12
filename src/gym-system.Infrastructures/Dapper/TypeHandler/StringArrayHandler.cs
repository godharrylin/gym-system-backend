using Dapper;
using System.Data;
using System.Text.Json;

namespace gym_system.Infrastructures.Dapper.TypeHandler
{
    //  用來將SQL 拿到的字串 傳換成 C# string[]
    public class StringArrayHandler : SqlMapper.TypeHandler<string[]>
    {
        public override string[] Parse(object value)
        {
            if (value == null || value is DBNull)
                return Array.Empty<string>();

            var str = value.ToString();
            if (string.IsNullOrWhiteSpace(str))
                return Array.Empty<string>();

            return JsonSerializer.Deserialize<string[]>(str)
                   ?? Array.Empty<string>();
        }

        public override void SetValue(IDbDataParameter parameter, string[]? value)
        {
            if (value == null)
            {
                parameter.Value = DBNull.Value;
                return;
            }

            parameter.Value = JsonSerializer.Serialize(value);
        }
    }
}
