using Dapper;
using gym_system.Domain.Repositories;
using gym_system.Infrastructures;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace gym_system.Api.Tests;

public sealed class GateDeviceSqlTests
{
    [SqlServerFact]
    public async Task RepositoryMapsDirectionAndSession_AndRollbackLeavesNoAuthorization()
    {
        var connectionString = Environment.GetEnvironmentVariable("TEST_DB_CONNECTION")!;
        var code = "GT_" + Guid.NewGuid().ToString("N")[..20];
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = connectionString }).Build());
        services.AddInfrastructureSql();
        await using var provider = services.BuildServiceProvider();
        using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        var now = new DateTime(2026, 10, 16, 19, 10, 0);
        var stationId = await connection.QuerySingleAsync<int>("""
            INSERT dbo.gate_station(gate_station_code,gate_station_name,gate_station_direction,
                gate_station_is_active,gate_station_create_dt)
            OUTPUT inserted.gate_station_sn VALUES(@code,N'owned integration test',1,1,@now);
            """, new { code, now });
        try
        {
            await using var scope = provider.CreateAsyncScope();
            var repository = scope.ServiceProvider.GetRequiredService<IGateDeviceRepository>();
            var passages = scope.ServiceProvider.GetRequiredService<IGatePassageRepository>();
            var work = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await work.BeginAsync(default);
            try
            {
                var station = (await repository.FindStationAsync(code, true, default))!;
                Assert.Equal(stationId, station.Id);
                var oldId = Guid.NewGuid();
                await repository.ReplaceAsync(station, oldId, now, default);
                var old = (await repository.FindAuthorizationAsync(oldId, default))!;
                Assert.True(old.IsValidAt(now.AddYears(10)));
                Assert.Null(old.Session.ExpiresAt);
                Assert.Equal(gym_system.Domain.Entities.Devices.GateDirection.Entry, old.Station.Direction);
                var requestId = Guid.NewGuid();
                Assert.True(await passages.TryCreateAsync(requestId, old, "SQL_TEST_USER", "Staff",
                    "[\"Staff\"]", now, default));
                Assert.False(await passages.TryCreateAsync(requestId, old, "SQL_TEST_USER", "Staff",
                    "[\"Staff\"]", now, default));
                var requested = (await passages.FindByRequestIdAsync(requestId, default))!;
                Assert.Equal("Requested", requested.Outcome);
                Assert.False(requested.AffectsPresence);
                Assert.True(await passages.CompleteAsync(requestId, "Succeeded", "COMMAND_ACCEPTED",
                    true, false, true, now.AddMilliseconds(125), null, default));
                var completed = (await passages.FindByRequestIdAsync(requestId, default))!;
                Assert.Equal("Succeeded", completed.Outcome);
                Assert.True(completed.AffectsPresence);
                Assert.False(completed.IsSimulated);
                var newId = Guid.NewGuid();
                await repository.ReplaceAsync(old.Station, newId, now.AddSeconds(1), default);
                Assert.False((await repository.FindAuthorizationAsync(oldId, default))!.IsValidAt(now));
                var current = (await repository.FindAuthorizationAsync(newId, default))!;
                Assert.True(current.IsValidAt(now));
                await repository.RevokeAsync(current.Station, "TEST_OPERATOR", now.AddSeconds(2), default);
                Assert.False((await repository.FindAuthorizationAsync(newId, default))!.IsValidAt(now));
            }
            finally { await work.RollbackAsync(default); }
            Assert.Equal(0, await connection.QuerySingleAsync<int>(
                "SELECT COUNT(*) FROM dbo.gate_device_session WHERE gate_station_sn=@stationId", new { stationId }));
            Assert.Null(await connection.QuerySingleAsync<Guid?>(
                "SELECT gate_station_current_session_id FROM dbo.gate_station WHERE gate_station_sn=@stationId", new { stationId }));
        }
        finally
        {
            // Remove only this test's explicitly generated station; never delete the real entry/exit stations.
            await connection.ExecuteAsync("DELETE dbo.gate_station WHERE gate_station_sn=@stationId AND gate_station_code=@code",
                new { stationId, code });
        }
    }
}
