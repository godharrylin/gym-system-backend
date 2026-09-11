using gym_system.Domain.Entities.Users;
using gym_system.Domain.Enums;
using gym_system.Domain.Repositories;
using gym_system.Infrastructures;
using gym_system.Infrastructures.Connections;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace gym_system.Api.Tests;

[Collection("Ticket SQL")]
public sealed class UserRoleRepositoryIntegrationTests : IAsyncLifetime
{
    private readonly TicketSqlFixture _fixture = new();
    private string? _testUserId;
    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync()
    {
        await _sp.DisposeAsync();
        await _fixture.DisposeAsync();
    }

    private async Task<string> TestUser()
    {
        if (_testUserId is not null) return _testUserId;
        _testUserId = await _fixture.AddStudent();
        return _testUserId;
    }
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

    [SqlServerFact]
    public async Task GetUserRoleAsync_ShouldGetTheRole()
    {
        //  給個測試的UserID
        var userId = await TestUser();
        CancellationToken ct = new CancellationToken();
        var getRole = await GetUserRoleAsync_Test(userId, UserRoleCode.Student, ct);
        Assert.NotNull(getRole);
        Assert.Equal(_fixture.Clock.Now().AddDays(-2), getRole.AssignedAt);
        _output.WriteLine($"getRole: ID={getRole?.UserId} ,Role={getRole?.RoleCode}, AssignedAt={getRole?.AssignedAt}");
    }
    [SqlServerFact]
    public async Task GetUserRoleAsync_ShouldNotGetTheRole()
    {
        //  給個測試的UserID
        var userId = await TestUser();
        CancellationToken ct = new CancellationToken();
        var getRole = await GetUserRoleAsync_Test(userId, UserRoleCode.Instructor, ct);
        Assert.Null(getRole);
        _output.WriteLine($"getRole: ID={getRole?.UserId} ,Role={getRole?.RoleCode}, AssignedAt={getRole?.AssignedAt}");
    }
    [SqlServerFact]
    public async Task AddRoleAsync_Test_CanAdd()
    {
        using var scope = _sp.CreateScope();
        var roleRepo = scope.ServiceProvider.GetRequiredService<IUserRoleRepository>();


        CancellationToken ct = new CancellationToken();
        var userId = await TestUser();
        var assignedAt = _fixture.Clock.Now();
        var role = UserRole.Assign(userId, UserRoleCode.Instructor, assignedAt, true);
        var isSuccess = await roleRepo.AddRoleAsync(role, ct);
        Assert.True(isSuccess);
        Assert.Equal(assignedAt, (await roleRepo.GetUserRoleAsync(userId, UserRoleCode.Instructor, ct))!.AssignedAt);
    }
    [SqlServerFact]
    public async Task ReactiveRole_ShouldSuccess()
    {
        using var scope = _sp.CreateScope();
        var roleRepo = scope.ServiceProvider.GetRequiredService<IUserRoleRepository>();
        CancellationToken ct = new CancellationToken();

        var userId = await TestUser();
        await roleRepo.SetRoleActiveAsync(userId, UserRoleCode.Student, false, ct);
        Assert.True(await roleRepo.ReactivateRoleAsync(userId, UserRoleCode.Student, ct));
        Assert.True((await roleRepo.GetUserRoleAsync(userId, UserRoleCode.Student, ct))!.IsActive);
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
