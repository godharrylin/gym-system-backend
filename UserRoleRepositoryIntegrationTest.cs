using gym_system.Domain.Entities.Users;
using gym_system.Domain.Enums;
using gym_system.Domain.Repositories;
using gym_system.Infrastructures;
using gym_system.Infrastructures.Connections;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;
using Xunit;

namespace gym_system.Api.Tests;

public sealed class UserRoleRepositoryIntegrationTests
{
    private readonly IServiceProvider _sp;
    
    public UserRoleRepositoryIntegrationTests()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] =
                    "Server=LAPTOP-GBB0UO0C;Database=GymDB;User Id=sa;Password=gym55688;TrustServerCertificate=True;"
            })
            .Build();
    }
}
