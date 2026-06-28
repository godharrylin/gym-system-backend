using Xunit;

namespace gym_system.Api.Tests;

public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TEST_DB_CONNECTION")))
        {
            Skip = "需要設定 TEST_DB_CONNECTION 才能執行 SQL 整合測試";
        }
    }
}
