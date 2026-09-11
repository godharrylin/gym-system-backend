using gym_system.Domain.Entities;
using gym_system.Domain.Entities.Users;
using gym_system.Domain.Repositories;
using gym_system.Infrastructures;
using gym_system.Infrastructures.Connections;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Xunit;
using Xunit.Abstractions;


namespace gym_system.Api.Tests
{
    [Collection("Ticket SQL")]
    public sealed class UserRepositoryTest : IAsyncLifetime
    {
        private readonly TicketSqlFixture _fixture = new();
        public Task InitializeAsync() => Task.CompletedTask;
        public async Task DisposeAsync()
        {
            await _sp.DisposeAsync();
            await _fixture.DisposeAsync();
        }
        private readonly ServiceProvider _sp;
        private readonly ITestOutputHelper _output;
        public UserRepositoryTest(ITestOutputHelper output) 
        {
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
        public async Task GetExistingPhoneAsync_Test()
        {
            using var scope = _sp.CreateAsyncScope();
            var userRepo = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            CancellationToken ct = new CancellationToken();

            var userId = await _fixture.AddStudent();
            var testUser = (await userRepo.FindUserByIdAsync(userId, ct))!;
            var phones = new List<string> { testUser.Phone };

            var result = await userRepo.GetExistingPhonesAsync(phones, ct);
            Assert.Equal(phones, result);
            _output.WriteLine("If exist:");
            foreach (var phone in result)
            {
                _output.WriteLine(phone);
            }

            _output.WriteLine("If not exist:");
            phones = new List<string> 
            {
                _fixture.NewPhone()
            };
            result = await userRepo.GetExistingPhonesAsync(phones, ct);
            Assert.Empty(result);
            foreach (var phone in result)
            {
                _output.WriteLine(phone);
            }
        }

        [SqlServerFact]
        public async Task FindUserByPhone_Test()
        {
            using var scope = _sp.CreateAsyncScope();
            var userRepo = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            CancellationToken ct = new CancellationToken();
            var userId = await _fixture.AddStudent();
            var testUser = (await userRepo.FindUserByIdAsync(userId, ct))!;
            var user = await userRepo.FindUserByPhoneAsync(testUser.Phone, ct);
            Assert.NotNull(user);
            Assert.Equal(userId, user.Id);
            _output.WriteLine($"User Id:{user.Id}, Phone: {user.Phone}, isactive: {user.IsActive}");

        }
    }
}
