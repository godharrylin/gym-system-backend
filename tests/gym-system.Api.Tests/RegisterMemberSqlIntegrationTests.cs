using System.Data;
using gym_system.Application.MembersUseCase.Commands.RegisterMember;
using gym_system.Domain.Entities.Orders;
using gym_system.Domain.Entities.Tickets;
using gym_system.Domain.Entities.Users;
using gym_system.Domain.Enums;
using gym_system.Domain.Repositories;
using gym_system.Infrastructures;
using gym_system.Infrastructures.Connections;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace gym_system.Api.Tests;

public sealed class RegisterMemberSqlIntegrationTests
{
    private static long _phoneSequence = DateTime.UtcNow.Ticks;

    [SqlServerFact]
    public async Task RegisterWithoutTicket_ShouldCreateUserStudentRoleAndProfile()
    {
        var phone = CreateUniquePhone();
        using var provider = BuildProvider();

        try
        {
            await using var scope = provider.CreateAsyncScope();
            var handler = CreateHandler(scope.ServiceProvider);

            var result = await handler.Handle(new RegisterMembersCommand
            {
                Members = [new MemberRegisterInput { Name = "Integration Student", Phone = phone }]
            });

            var userId = Assert.Single(result.MemberIds);
            var user = await scope.ServiceProvider.GetRequiredService<IUserRepository>()
                .FindUserByPhoneAsync(phone, CancellationToken.None);
            var role = await scope.ServiceProvider.GetRequiredService<IUserRoleRepository>()
                .GetUserRoleAsync(userId, UserRoleCode.Student, CancellationToken.None);
            var profile = await scope.ServiceProvider.GetRequiredService<IStudentProfileRepository>()
                .FindByUserIdAsync(userId, CancellationToken.None);

            Assert.NotNull(user);
            Assert.NotNull(role);
            Assert.True(role.IsActive);
            Assert.NotNull(profile);
        }
        finally
        {
            CleanupByPhone(provider, phone);
        }
    }

    [SqlServerFact]
    public async Task DuplicatePhone_ShouldBeRejectedByDatabaseConstraint()
    {
        var phone = CreateUniquePhone();
        using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        try
        {
            await unitOfWork.BeginAsync(CancellationToken.None);
            await userRepository.AddAsync(User.Register("A", phone, phone), CancellationToken.None);

            await Assert.ThrowsAnyAsync<Exception>(() =>
                userRepository.AddAsync(User.Register("B", phone, phone), CancellationToken.None));
        }
        finally
        {
            await unitOfWork.RollbackAsync(CancellationToken.None);
            CleanupByPhone(provider, phone);
        }
    }

    [SqlServerFact]
    public async Task AdminWithoutStudentRole_ShouldNotHaveProfile_AndMultiRoleUserShouldHaveOneProfile()
    {
        var phone = CreateUniquePhone();
        using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var roleRepository = scope.ServiceProvider.GetRequiredService<IUserRoleRepository>();
        var profileRepository = scope.ServiceProvider.GetRequiredService<IStudentProfileRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        try
        {
            await unitOfWork.BeginAsync(CancellationToken.None);
            var userId = await userRepository.AddAsync(
                User.Register("Multi Role", phone, phone),
                CancellationToken.None);
            await roleRepository.AddRoleAsync(
                UserRole.Assign(userId, UserRoleCode.Admin, clock.Now(), true),
                CancellationToken.None);

            Assert.Null(await profileRepository.FindByUserIdAsync(userId, CancellationToken.None));

            await roleRepository.AddRoleAsync(
                UserRole.Assign(userId, UserRoleCode.Student, clock.Now(), true),
                CancellationToken.None);
            await profileRepository.AddAsync(
                gym_system.Domain.Entities.Members.StudentProfile.Create(userId),
                CancellationToken.None);
            await unitOfWork.CommitAsync(CancellationToken.None);

            var roles = await roleRepository.GetActiveRolesAsync(userId, CancellationToken.None);
            Assert.Equal(2, roles.Count);
            Assert.Equal(1, CountProfiles(provider, phone));
        }
        finally
        {
            await unitOfWork.RollbackAsync(CancellationToken.None);
            CleanupByPhone(provider, phone);
        }
    }

    [SqlServerFact]
    public async Task Register_ShouldRollbackUser_WhenStudentRoleCreationFails()
    {
        var phone = CreateUniquePhone();
        using var provider = BuildProvider();

        try
        {
            await using var scope = provider.CreateAsyncScope();
            var handler = new RegisterMemberHandler(
                scope.ServiceProvider.GetRequiredService<IUserRepository>(),
                new FailingUserRoleRepository(),
                scope.ServiceProvider.GetRequiredService<IStudentProfileRepository>(),
                new UnusedTicketPlanRepository(),
                new UnusedOrderRepository(),
                new UnusedTicketPassRepository(),
                scope.ServiceProvider.GetRequiredService<IUnitOfWork>(),
                scope.ServiceProvider.GetRequiredService<IClock>());

            await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(
                new RegisterMembersCommand
                {
                    Members = [new MemberRegisterInput { Name = "Rollback", Phone = phone }]
                }));

            var user = await scope.ServiceProvider.GetRequiredService<IUserRepository>()
                .FindUserByPhoneAsync(phone, CancellationToken.None);
            Assert.Null(user);
        }
        finally
        {
            CleanupByPhone(provider, phone);
        }
    }

    private static RegisterMemberHandler CreateHandler(IServiceProvider services)
    {
        return new RegisterMemberHandler(
            services.GetRequiredService<IUserRepository>(),
            services.GetRequiredService<IUserRoleRepository>(),
            services.GetRequiredService<IStudentProfileRepository>(),
            new UnusedTicketPlanRepository(),
            new UnusedOrderRepository(),
            new UnusedTicketPassRepository(),
            services.GetRequiredService<IUnitOfWork>(),
            services.GetRequiredService<IClock>());
    }

    private static ServiceProvider BuildProvider()
    {
        var connectionString = Environment.GetEnvironmentVariable("TEST_DB_CONNECTION")!;
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = connectionString
            })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddInfrastructureSql();
        return services.BuildServiceProvider();
    }

    private static string CreateUniquePhone()
    {
        var value = Math.Abs(Interlocked.Increment(ref _phoneSequence) % 100_000_000);
        return $"09{value:00000000}";
    }

    private static int CountProfiles(ServiceProvider provider, string phone)
    {
        using var scope = provider.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<ISqlConnectionFactory>();
        using var connection = factory.CreateConnection();
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(1)
            FROM dbo.sdt_profile AS p
            INNER JOIN dbo.users AS u ON p.usr_id = u.usr_id
            WHERE u.usr_phone = @phone
            """;
        AddParameter(command, "@phone", phone);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static void CleanupByPhone(ServiceProvider provider, string phone)
    {
        using var scope = provider.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<ISqlConnectionFactory>();
        using var connection = factory.CreateConnection();
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE p
            FROM dbo.sdt_profile AS p
            INNER JOIN dbo.users AS u ON p.usr_id = u.usr_id
            WHERE u.usr_phone = @phone;

            DELETE ur
            FROM dbo.user_role AS ur
            INNER JOIN dbo.users AS u ON ur.usr_id = u.usr_id
            WHERE u.usr_phone = @phone;

            DELETE FROM dbo.users WHERE usr_phone = @phone;
            """;
        AddParameter(command, "@phone", phone);
        command.ExecuteNonQuery();
    }

    private static void AddParameter(IDbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private sealed class FailingUserRoleRepository : IUserRoleRepository
    {
        public Task<UserRole?> GetUserRoleAsync(string userId, UserRoleCode roleType, CancellationToken ct) =>
            Task.FromResult<UserRole?>(null);
        public Task<IReadOnlyList<UserRole>> GetActiveRolesAsync(string userId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<UserRole>>([]);
        public Task<bool> AddRoleAsync(UserRole userRole, CancellationToken ct) => Task.FromResult(false);
        public Task<bool> ReactivateRoleAsync(string userId, UserRoleCode roleType, CancellationToken ct) =>
            Task.FromResult(false);
        public Task<bool> SetRoleActiveAsync(string userId, UserRoleCode roleType, bool isActive, CancellationToken ct) =>
            Task.FromResult(false);
    }

    private sealed class UnusedTicketPlanRepository : ITicketPlanRepository
    {
        public Task<TicketPlanKind?> GetActiveByIdAsync(string ticketPlanKindId, CancellationToken ct) =>
            throw new InvalidOperationException("純註冊不應查詢票券方案");
    }

    private sealed class UnusedOrderRepository : IOrderRepository
    {
        public Task AddAsync(Order order, CancellationToken ct) =>
            throw new InvalidOperationException("純註冊不應建立訂單");
    }

    private sealed class UnusedTicketPassRepository : ITicketPassRepository
    {
        public Task AddRangeAsync(IReadOnlyList<TicketPass> passes, CancellationToken ct) =>
            throw new InvalidOperationException("純註冊不應建立票券");
    }
}
