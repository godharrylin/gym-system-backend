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
    public sealed class UserRepositoryTest
    {
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

        [Fact]
        public async Task GetExistingPhoneAsync_Test()
        {
            using var scope = _sp.CreateAsyncScope();
            var userRepo = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            CancellationToken ct = new CancellationToken();

            var phones = new List<string>
            {
                "0911111111",
                "0922222222",
                "0933333333",
                "0944444444",
                "0912345678"
            };

            var result = await userRepo.GetExistingPhonesAsync(phones, ct);
            _output.WriteLine("If exist:");
            foreach (var phone in result)
            {
                _output.WriteLine(phone);
            }

            _output.WriteLine("If not exist:");
            phones = new List<string> 
            {
                "0955688779"
            };
            result = await userRepo.GetExistingPhonesAsync(phones, ct);
            foreach (var phone in result)
            {
                _output.WriteLine(phone);
            }
        }

        [Fact]
        public async Task FindUserByPhone_Test()
        {
            using var scope = _sp.CreateAsyncScope();
            var userRepo = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            CancellationToken ct = new CancellationToken();
            var user = await userRepo.FindUserByPhone("0900000000", ct);
            _output.WriteLine($"User Id:{user.Id}, Phone: {user.Phone}, isactive: {user.IsActived}");

        }
    }
}
