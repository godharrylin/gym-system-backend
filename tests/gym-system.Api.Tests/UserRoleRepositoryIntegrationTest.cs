using gym_system.Domain.Entities.Users;
using gym_system.Domain.Enums;
using gym_system.Domain.Repositories;
using gym_system.Infrastructures;
using gym_system.Infrastructures.Connections;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;
using Xunit;
using Xunit.Abstractions;

namespace gym_system.Api.Tests;

public sealed class UserRoleRepositoryIntegrationTests
{
    private readonly ServiceProvider _sp;
    private readonly ITestOutputHelper _output;
    public UserRoleRepositoryIntegrationTests(ITestOutputHelper output)
    {
        //  用環境變數，避免把敏感資料庫連線資訊放上來
        //  $env:TEST_DB_CONNECTION="Server=...;Database=...;User Id=...;Password=...;TrustServerCertificate=True;"
        //  dotnet test tests\gym - system.Api.Tests\gym - system.Api.Tests.csproj
        var conn = Environment.GetEnvironmentVariable("TEST_DB_CONNECTION")
            ?? throw new InvalidOperationException("請先設定 TEST_DB_CONNECTION");

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = conn
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddInfrastructureSql();

        _sp = services.BuildServiceProvider();
        _output = output;
    }

    [Fact]
    public async Task GetUserRoleAsync_ShouldGetTheRole()
    {
        //  給個測試的UserID
        var userId = "U00006";
        CancellationToken ct = new CancellationToken();
        var getRole = await GetUserRoleAsync_Test(userId, UserRoleCode.Instructor, ct);
        _output.WriteLine($"getRole: ID={getRole?.UserId} ,Role={getRole?.RoleCode}, AssignedAt={getRole?.AssignedAt}");
    }
    [Fact]
    public async Task GetUserRoleAsync_ShouldNotGetTheRole()
    {
        //  給個測試的UserID
        var userId = "U00007";
        CancellationToken ct = new CancellationToken();
        var getRole = await GetUserRoleAsync_Test(userId, UserRoleCode.Instructor, ct);
        _output.WriteLine($"getRole: ID={getRole?.UserId} ,Role={getRole?.RoleCode}, AssignedAt={getRole?.AssignedAt}");
    }

    private async Task<UserRole?> GetUserRoleAsync_Test(string userId, UserRoleCode roleType, CancellationToken ct)
    {
        //  開啟一個新的DI Scope，模擬一次Http Request的生命週期
        using var scope = _sp.CreateScope();

        //  從DI容器取出要測試的主角: IUserRepository
        var roleRepo = scope.ServiceProvider.GetRequiredService<IUserRoleRepository>();

        //  取出資料庫連線工廠，主要用來在測試中執行一些「前置/後置」的直接SQL 操作
        var connFactory = scope.ServiceProvider.GetRequiredService<ISqlConnectionFactory>();

        //  防呆測試:確保DI容器給我們的是真的SQL實作版 (SqlUserRepository)，而不是Mock版
        Assert.Equal("SqlUserRoleRepository", roleRepo.GetType().Name);

        var result = await roleRepo.GetUserRoleAsync(userId, roleType, ct);
        return result;
    }
}
