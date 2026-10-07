using System.Security.Claims;
using gym_system.Api.Contracts;
using gym_system.Api.Contracts.Devices;
using gym_system.Api.Controllers;
using gym_system.Api.Devices;
using gym_system.Application.DevicesUseCase;
using gym_system.Infrastructures.Devices;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace gym_system.Api.Tests;

public sealed class TurnstileModeTests
{
    [Theory]
    [InlineData("Real", typeof(CloudServerTurnstileClient))]
    [InlineData("Mock", typeof(MockTurnstileCommandClient))]
    public void RegistrationSelectsExactlyOneImplementation(string mode, Type expected)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTurnstileControl(new() { Mode = mode }, false);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.IsType(expected, scope.ServiceProvider.GetRequiredService<ITurnstileCommandClient>());
        Assert.Single(scope.ServiceProvider.GetServices<ITurnstileCommandClient>());
        if (mode == "Mock") Assert.Null(provider.GetService<IHttpClientFactory>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("mock")]
    [InlineData("Other")]
    public void InvalidModesRejectStartup(string mode)
    {
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddTurnstileControl(new() { Mode = mode }, false));
    }

    [Fact]
    public void ProductionCannotUseMockEvenWhenDeviceTestDisabled()
    {
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddTurnstileControl(new() { Mode = "Mock" }, true));
        Assert.Equal("Real", new TurnstileSettings().Mode);
        new TurnstileSettings().Validate(true);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task MockReturnsExplicitSuccessWithoutAnyHttpDependency(int doorNo)
    {
        var client = new MockTurnstileCommandClient(NullLogger<MockTurnstileCommandClient>.Instance);
        var result = await client.OpenDoorAsync(253260812, doorNo, default);
        Assert.True(result.Succeeded);
        Assert.Equal(TurnstileOutcome.Succeeded, result.Outcome);
        Assert.True(result.IsSimulated);
        Assert.Contains("未控制真實設備", result.Message);
        Assert.Null(result.Snapshot);
        var status = await client.QueryStatusAsync(253260812, default);
        Assert.True(status.Succeeded);
        Assert.True(status.IsSimulated);
        Assert.Equal(253260812, status.Snapshot!.ControllerSN);
        Assert.All(status.Snapshot.DoorStates, p => Assert.Equal("Closed", p.Value));
    }

    [Fact]
    public async Task MockNeverPretendsCancelledOrInvalidCallsSucceeded()
    {
        var client = new MockTurnstileCommandClient(NullLogger<MockTurnstileCommandClient>.Instance);
        var cancelled = new CancellationToken(true);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.OpenDoorAsync(253260812, 1, cancelled));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.QueryStatusAsync(253260812, cancelled));
        Assert.False((await client.OpenDoorAsync(253260812, 0, default)).Succeeded);
        Assert.False((await client.QueryStatusAsync(1, default)).Succeeded);
        var disabled = new SendDeviceTestCommandHandler(client, new() { Mode = "Mock" }, new());
        await Assert.ThrowsAsync<DeviceCommandException>(() => disabled.Handle("Entry", default));
    }

    [Theory]
    [InlineData(TurnstileOutcome.Failed, "CONTROLLER_REJECTED", 502)]
    [InlineData(TurnstileOutcome.Unknown, "CONTROL_COMMAND_TIMEOUT", 504)]
    [InlineData(TurnstileOutcome.Unknown, "CONTROL_INVALID_RESPONSE", 502)]
    public async Task ControllerExplicitlyMapsReturnedFailures(TurnstileOutcome outcome, string code, int httpStatus)
    {
        var client = new ResultClient(new(outcome, false, code, "test result"));
        var controller = Controller(client);
        var result = Assert.IsType<ObjectResult>(await controller.OpenDoor(new() { Direction = "Entry" }, default));
        Assert.Equal(httpStatus, result.StatusCode);
        var body = Assert.IsType<ApiErrorResponse>(result.Value);
        Assert.False(body.Succeeded);
        Assert.False(body.IsSimulated);
        Assert.Equal(outcome.ToString(), body.Outcome);
        result = Assert.IsType<ObjectResult>(await controller.GetStatus(default));
        Assert.Equal(httpStatus, result.StatusCode);
    }

    [Fact]
    public async Task MockApiSuccessIsMarkedAndUnexpectedErrorsStayErrors()
    {
        var controller = Controller(new MockTurnstileCommandClient(NullLogger<MockTurnstileCommandClient>.Instance));
        var result = Assert.IsType<OkObjectResult>(await controller.OpenDoor(new() { Direction = "Exit" }, default));
        using var json = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(result.Value));
        Assert.True(json.RootElement.GetProperty("Succeeded").GetBoolean());
        Assert.True(json.RootElement.GetProperty("IsSimulated").GetBoolean());
        var broken = Controller(new ResultClient(new(TurnstileOutcome.Succeeded, false, "broken", "broken")) { Throw = true });
        var failure = Assert.IsType<ObjectResult>(await broken.OpenDoor(new() { Direction = "Entry" }, default));
        Assert.Equal(500, failure.StatusCode);
    }

    private static DeviceTestsController Controller(ITurnstileCommandClient client)
    {
        var settings = new TurnstileSettings { DeviceTestEnabled = true };
        var gate = new DeviceTestCommandGate();
        return new(new(client, settings, gate), new(client, settings, gate), NullLogger<DeviceTestsController>.Instance)
        {
            ControllerContext = new() { HttpContext = new DefaultHttpContext
            {
                TraceIdentifier = "mode-test", User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", "ADMIN1") }, "test"))
            } }
        };
    }

    private sealed class ResultClient(TurnstileCommandResult result) : ITurnstileCommandClient
    {
        public bool Throw { get; init; }
        public Task<TurnstileCommandResult> OpenDoorAsync(int sn, int doorNo, CancellationToken ct) =>
            Throw ? throw new InvalidOperationException("unexpected") : Task.FromResult(result);
        public Task<TurnstileCommandResult> QueryStatusAsync(int sn, CancellationToken ct) => Task.FromResult(result);
    }
}
