using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using gym_system.Api.Contracts;
using gym_system.Api.Contracts.Devices;
using gym_system.Api.Controllers;
using gym_system.Application.DevicesUseCase;
using gym_system.Infrastructures.Devices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace gym_system.Api.Tests;

// Every HTTP call is intercepted by a fake handler; no database or real controller is used.
public sealed class DeviceTestTests
{
    [Theory]
    [InlineData("Entry", 1)]
    [InlineData("Exit", 2)]
    public async Task SendsExactVendorCommandAndChecksResponse(string direction, int doorNo)
    {
        var transport = new FakeHttp(async (request, ct) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("http://test.invalid/", request.RequestUri!.ToString());
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            var root = json.RootElement;
            Assert.Equal("2.0", root.GetProperty("jsonrpc").GetString());
            Assert.Equal("RemoteOpenDoor", root.GetProperty("method").GetString());
            var param = root.GetProperty("params")[0];
            Assert.Equal(253260812, param.GetProperty("ControllerSN").GetInt32());
            Assert.Equal(doorNo, param.GetProperty("DoorNO").GetInt32());
            return Success(root.GetProperty("id").GetInt64());
        });
        await CreateHandler(transport).Handle(direction, default);
        Assert.Equal(1, transport.Calls);
    }

    [Theory]
    [InlineData("entry")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("Other")]
    public async Task InvalidDirectionNeverDispatches(string? direction)
    {
        var client = new StubClient();
        var handler = new SendDeviceTestCommandHandler(client, Enabled(), new());
        var error = await Assert.ThrowsAsync<DeviceCommandException>(() => handler.Handle(direction, default));
        Assert.Equal(400, error.StatusCode);
        Assert.Equal(0, client.Calls);
    }

    [Fact]
    public async Task DisabledNeverDispatches()
    {
        var client = new StubClient();
        var handler = new SendDeviceTestCommandHandler(client, new(), new());
        var error = await Assert.ThrowsAsync<DeviceCommandException>(() => handler.Handle("Entry", default));
        Assert.Equal("DEVICE_TEST_DISABLED", error.Code);
        Assert.Equal(0, client.Calls);
    }

    [Theory]
    [InlineData("not json", "CONTROL_INVALID_RESPONSE")]
    [InlineData("{}", "CONTROL_INVALID_RESPONSE")]
    [InlineData("[]", "CONTROL_INVALID_RESPONSE")]
    [InlineData("{\"error\":{\"code\":-32700}}", "CONTROLLER_REJECTED")]
    public async Task BadResponsesNeverRetry(string body, string expectedCode)
    {
        var transport = new FakeHttp((_, _) => Task.FromResult(Reply(body)));
        var error = await Assert.ThrowsAsync<DeviceCommandException>(() => CreateHandler(transport).Handle("Entry", default));
        Assert.Equal(expectedCode, error.Code);
        Assert.Equal(502, error.StatusCode);
        Assert.Equal(1, transport.Calls);
    }

    [Theory]
    [InlineData(false, 1, 253260812, false, "CONTROLLER_REJECTED")]
    [InlineData(true, 0, 253260812, false, "CONTROL_INVALID_RESPONSE")]
    [InlineData(true, 1, 123456789, false, "CONTROL_INVALID_RESPONSE")]
    [InlineData(true, 1, 253260812, true, "CONTROL_INVALID_RESPONSE")]
    public async Task ValidatesVendorSuccessFields(bool success, int code, int sn, bool wrongId, string expected)
    {
        var transport = new FakeHttp(async (request, ct) =>
        {
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            var id = json.RootElement.GetProperty("id").GetInt64();
            return Reply(JsonSerializer.Serialize(new { jsonrpc = "2.0", id = wrongId ? id + 1 : id,
                result = new { success, code, ControllerSN = sn } }));
        });
        var error = await Assert.ThrowsAsync<DeviceCommandException>(() => CreateHandler(transport).Handle("Exit", default));
        Assert.Equal(expected, error.Code);
        Assert.Equal(1, transport.Calls);
    }

    [Fact]
    public async Task HttpErrorNeverRetries()
    {
        var transport = new FakeHttp((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)));
        var error = await Assert.ThrowsAsync<DeviceCommandException>(() => CreateHandler(transport).Handle("Entry", default));
        Assert.Equal("CONTROL_SERVICE_HTTP_ERROR", error.Code);
        Assert.Equal(1, transport.Calls);
    }

    [Fact]
    public async Task ConnectionErrorNeverRetries()
    {
        var transport = new FakeHttp((_, _) => throw new HttpRequestException("offline"));
        var error = await Assert.ThrowsAsync<DeviceCommandException>(() => CreateHandler(transport).Handle("Entry", default));
        Assert.Equal("CONTROL_SERVICE_UNAVAILABLE", error.Code);
        Assert.Equal(1, transport.Calls);
    }

    [Fact]
    public async Task TimeoutIsUnknownAndNeverRetries()
    {
        var transport = new FakeHttp(async (_, ct) =>
        { await Task.Delay(Timeout.Infinite, ct); return Reply("{}"); });
        var error = await Assert.ThrowsAsync<DeviceCommandException>(() => CreateHandler(transport, 1).Handle("Entry", default));
        Assert.Equal(504, error.StatusCode);
        Assert.Equal("CONTROL_COMMAND_TIMEOUT", error.Code);
        Assert.Equal(1, transport.Calls);
    }

    [Fact]
    public async Task CancellationDoesNotRetryAndReleasesGate()
    {
        using var cancelled = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var transport = new FakeHttp(async (_, ct) =>
        { started.SetResult(); await Task.Delay(Timeout.Infinite, ct); return Reply("{}"); });
        var gate = new DeviceTestCommandGate();
        var handler = CreateHandler(transport, gate: gate);
        var pending = handler.Handle("Entry", cancelled.Token);
        await started.Task;
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(1, transport.Calls);
        Assert.True(gate.TryEnter());
        gate.Exit();
    }

    [Fact]
    public async Task BusyRejectsWithoutQueuingAndGateRecovers()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new StubClient { OnCall = () => release.Task };
        var gate = new DeviceTestCommandGate();
        var handler = new SendDeviceTestCommandHandler(client, Enabled(), gate);
        var pending = handler.Handle("Entry", default);
        var error = await Assert.ThrowsAsync<DeviceCommandException>(() => handler.Handle("Exit", default));
        Assert.Equal(409, error.StatusCode);
        Assert.Equal(1, client.Calls);
        release.SetResult();
        await pending;
        client.OnCall = () => Task.CompletedTask;
        await handler.Handle("Exit", default);
        Assert.Equal(2, client.Calls);
    }

    [Fact]
    public void EndpointRequiresAdminAndSettingsDefaultToDisabled()
    {
        var auth = typeof(DeviceTestsController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.Equal("Admin", auth!.Roles);
        Assert.False(new TurnstileSettings().DeviceTestEnabled);
        new TurnstileSettings().Validate();
        Assert.Throws<InvalidOperationException>(() => new TurnstileSettings { EntryDoorNo = 2 }.Validate());
    }

    [Fact]
    public async Task ControllerReturnsTraceIdAndMissingOperatorDoesNotDispatch()
    {
        var client = new StubClient();
        var controller = new DeviceTestsController(new(client, new(), new()), new(client, new(), new()), NullLogger<DeviceTestsController>.Instance)
        { ControllerContext = new() { HttpContext = new DefaultHttpContext { TraceIdentifier = "test-trace" } } };
        var result = Assert.IsType<ObjectResult>(await controller.OpenDoor(new() { Direction = "Entry" }, default));
        Assert.Equal(403, result.StatusCode);
        Assert.Equal(0, client.Calls);
        controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", "ADMIN1") }, "test"));
        result = Assert.IsType<ObjectResult>(await controller.OpenDoor(new() { Direction = "Entry" }, default));
        Assert.Equal("test-trace", Assert.IsType<ApiErrorResponse>(result.Value).TraceId);
        Assert.Equal(0, client.Calls);
    }

    private static TurnstileSettings Enabled(int seconds = 5) => new() { DeviceTestEnabled = true, TimeoutSeconds = seconds };
    [Fact]
    public async Task StatusQueryUsesReadOnlyCommandAndStripsHistory()
    {
        var logger = new CaptureLogger();
        var transport = new FakeHttp(async (request, ct) =>
        {
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            var root = json.RootElement;
            Assert.Equal("QueryControllerStatus", root.GetProperty("method").GetString());
            var param = root.GetProperty("params")[0];
            Assert.Equal(253260812, param.GetProperty("ControllerSN").GetInt32());
            Assert.False(param.TryGetProperty("DoorNO", out _));
            return StatusReply(root.GetProperty("id").GetInt64(),
                "{\"Time\":\"2026-10-07 12:00:00\",\"Door1#\":\"Closed\",\"Door2#\":\"Open\",\"LastSixRecords\":[{\"CardNO\":\"private-card\"}]}" );
        });
        using var http = new HttpClient(transport) { BaseAddress = new Uri("http://test.invalid/") };
        var client = new CloudServerTurnstileClient(http, Enabled(), logger);
        var snapshot = await client.QueryStatusAsync(253260812, default);
        Assert.Equal("Closed", snapshot.DoorStates["Door1#"]);
        Assert.Equal("Open", snapshot.DoorStates["Door2#"]);
        Assert.Equal("2026-10-07 12:00:00", snapshot.ControllerTime);
        Assert.DoesNotContain("LastSixRecords", JsonSerializer.Serialize(snapshot));
        Assert.DoesNotContain("private-card", JsonSerializer.Serialize(snapshot));
        Assert.DoesNotContain(logger.Records.SelectMany(r => r.Values), v => v?.ToString()?.Contains("private-card") == true);
        Assert.Equal(1, transport.Calls);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"Time\":\"invalid\",\"Door1#\":\"Closed\"}")]
    [InlineData("{\"Time\":\"2026-10-07 12:00:00\"}")]
    [InlineData("{\"Time\":\"2026-10-07 12:00:00\",\"Door1#\":\"Unknown\"}")]
    public async Task InvalidStatusDataIsNotSuccess(string info)
    {
        var transport = new FakeHttp(async (request, ct) =>
        {
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            return StatusReply(json.RootElement.GetProperty("id").GetInt64(), info);
        });
        using var http = new HttpClient(transport) { BaseAddress = new Uri("http://test.invalid/") };
        var client = new CloudServerTurnstileClient(http, Enabled(), NullLogger<CloudServerTurnstileClient>.Instance);
        var error = await Assert.ThrowsAsync<DeviceCommandException>(() => client.QueryStatusAsync(253260812, default));
        Assert.Equal("CONTROL_INVALID_RESPONSE", error.Code);
        Assert.Contains("狀態", error.Message);
        Assert.Equal(1, transport.Calls);
    }

    [Theory]
    [InlineData("reject", 502, "CONTROLLER_REJECTED")]
    [InlineData("json", 502, "CONTROL_INVALID_RESPONSE")]
    [InlineData("offline", 502, "CONTROL_SERVICE_UNAVAILABLE")]
    [InlineData("timeout", 504, "CONTROL_COMMAND_TIMEOUT")]
    public async Task StatusFailuresNeverRetry(string failure, int status, string code)
    {
        var transport = new FakeHttp(async (request, ct) =>
        {
            if (failure == "offline") throw new HttpRequestException("offline");
            if (failure == "timeout") await Task.Delay(Timeout.Infinite, ct);
            if (failure == "json") return Reply("invalid JSON");
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            return Reply(JsonSerializer.Serialize(new { jsonrpc = "2.0", id = json.RootElement.GetProperty("id").GetInt64(),
                result = new { success = false, code = 0, ControllerSN = 253260812 } }));
        });
        using var http = new HttpClient(transport) { BaseAddress = new Uri("http://test.invalid/"), Timeout = Timeout.InfiniteTimeSpan };
        var client = new CloudServerTurnstileClient(http, Enabled(1), NullLogger<CloudServerTurnstileClient>.Instance);
        var error = await Assert.ThrowsAsync<DeviceCommandException>(() => client.QueryStatusAsync(253260812, default));
        Assert.Equal(status, error.StatusCode);
        Assert.Equal(code, error.Code);
        Assert.Contains("狀態", error.Message);
        Assert.Equal(1, transport.Calls);
    }

    [Fact]
    public async Task StatusAndOpenShareGateAndDisabledQueryDoesNotDispatch()
    {
        var client = new StubClient();
        var gate = new DeviceTestCommandGate();
        var disabled = new GetDeviceTestStatusHandler(client, new(), gate);
        var error = await Assert.ThrowsAsync<DeviceCommandException>(() => disabled.Handle(default));
        Assert.Equal(403, error.StatusCode);
        Assert.Equal(0, client.Calls);
        var cancelled = new CancellationToken(true);
        var query = new GetDeviceTestStatusHandler(client, Enabled(), gate);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => query.Handle(cancelled));
        Assert.Equal(0, client.Calls);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.OnCall = () => release.Task;
        var pending = query.Handle(default);
        var open = new SendDeviceTestCommandHandler(client, Enabled(), gate);
        error = await Assert.ThrowsAsync<DeviceCommandException>(() => open.Handle("Entry", default));
        Assert.Equal(409, error.StatusCode);
        error = await Assert.ThrowsAsync<DeviceCommandException>(() => query.Handle(default));
        Assert.Equal(409, error.StatusCode);
        Assert.Equal(1, client.Calls);
        release.SetResult();
        await pending;
        client.OnCall = () => Task.CompletedTask;
        await open.Handle("Entry", default);
        Assert.Equal(2, client.Calls);
    }

    [Fact]
    public async Task StatusEndpointChecksOperatorAndReturnsTimestampWithoutCaching()
    {
        var client = new StubClient();
        var gate = new DeviceTestCommandGate();
        var controller = new DeviceTestsController(new(client, Enabled(), gate), new(client, Enabled(), gate), NullLogger<DeviceTestsController>.Instance)
        { ControllerContext = new() { HttpContext = new DefaultHttpContext { TraceIdentifier = "status-trace" } } };
        var rejected = Assert.IsType<ObjectResult>(await controller.GetStatus(default));
        Assert.Equal(403, rejected.StatusCode);
        Assert.Equal(0, client.Calls);
        controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", "ADMIN1") }, "test"));
        var result = Assert.IsType<OkObjectResult>(await controller.GetStatus(default));
        using var response = JsonDocument.Parse(JsonSerializer.Serialize(result.Value));
        Assert.Equal("QuerySucceeded", response.RootElement.GetProperty("Status").GetString());
        Assert.Equal("status-trace", response.RootElement.GetProperty("TraceId").GetString());
        Assert.True(response.RootElement.GetProperty("CheckedAt").TryGetDateTimeOffset(out _));
        var cache = typeof(DeviceTestsController).GetMethod(nameof(DeviceTestsController.GetStatus))!.GetCustomAttribute<ResponseCacheAttribute>();
        Assert.True(cache!.NoStore);
    }

    private static HttpResponseMessage StatusReply(long id, string info) => Reply(
        "{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"result\":{\"success\":true,\"code\":1,\"ControllerSN\":253260812,\"ControllerInformation\":" + info + "}}");

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LogsRequestAndAllowlistedVendorFieldsWithoutChangingOutcome(bool accepted)
    {
        using var activity = new System.Diagnostics.Activity("device-log-test").Start();
        var logger = new CaptureLogger();
        var transport = new FakeHttp(async (request, ct) =>
        {
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            var id = json.RootElement.GetProperty("id").GetInt64();
            return Reply(JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0", id,
                result = new { success = accepted, code = accepted ? 1 : 0, ControllerSN = 253260812,
                    msg = "Controller Communicate Failed", Reason = "sample\r\nreason", accessToken = "do-not-log-this" }
            }));
        });
        using var http = new HttpClient(transport) { BaseAddress = new Uri("http://test.invalid/") };
        var client = new CloudServerTurnstileClient(http, Enabled(), logger);
        if (accepted) await client.OpenDoorAsync(253260812, 2, default);
        else
        {
            var error = await Assert.ThrowsAsync<DeviceCommandException>(() => client.OpenDoorAsync(253260812, 2, default));
            Assert.Equal("CONTROLLER_REJECTED", error.Code);
        }
        var requestLog = Assert.Single(logger.Records, r => r.ContainsKey("DoorNO"));
        Assert.Equal(253260812, requestLog["ControllerSN"]);
        Assert.Equal(2, requestLog["DoorNO"]);
        var responseLog = Assert.Single(logger.Records, r => r.ContainsKey("Success"));
        Assert.Equal(requestLog["RpcId"], responseLog["RpcId"]);
        Assert.Equal(activity!.Id, responseLog["ActivityId"]);
        Assert.Equal(accepted ? "true" : "false", responseLog["Success"]);
        Assert.Equal(accepted ? "1" : "0", responseLog["Code"]);
        Assert.Equal("Controller Communicate Failed", responseLog["Msg"]);
        Assert.Equal("sample  reason", responseLog["Reason"]);
        Assert.DoesNotContain(logger.Records.SelectMany(r => r.Values), v => v?.ToString()?.Contains("do-not-log-this") == true);
        Assert.Equal(1, transport.Calls);
    }

    [Fact]
    public async Task LogsJsonRpcErrorDiagnostics()
    {
        var logger = new CaptureLogger();
        var transport = new FakeHttp((_, _) => Task.FromResult(Reply("{\"error\":{\"code\":-32700,\"message\":\"Parse error\"}}")));
        using var http = new HttpClient(transport) { BaseAddress = new Uri("http://test.invalid/") };
        var client = new CloudServerTurnstileClient(http, Enabled(), logger);
        var error = await Assert.ThrowsAsync<DeviceCommandException>(() => client.OpenDoorAsync(253260812, 1, default));
        Assert.Equal("CONTROLLER_REJECTED", error.Code);
        var responseLog = Assert.Single(logger.Records, r => r.ContainsKey("ErrorCode"));
        Assert.Equal("-32700", responseLog["ErrorCode"]);
        Assert.Equal("Parse error", responseLog["ErrorMessage"]);
        Assert.Equal(1, transport.Calls);
    }

    private sealed class CaptureLogger : ILogger<CloudServerTurnstileClient>
    {
        public List<Dictionary<string, object?>> Records { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (state is IEnumerable<KeyValuePair<string, object?>> fields)
                Records.Add(fields.ToDictionary(p => p.Key, p => p.Value));
        }
    }

    private static SendDeviceTestCommandHandler CreateHandler(FakeHttp transport, int seconds = 5, DeviceTestCommandGate? gate = null)
    {
        var settings = Enabled(seconds);
        var http = new HttpClient(transport) { BaseAddress = new Uri("http://test.invalid/"), Timeout = Timeout.InfiniteTimeSpan };
        return new(new CloudServerTurnstileClient(http, settings, NullLogger<CloudServerTurnstileClient>.Instance), settings, gate ?? new());
    }
    private static HttpResponseMessage Reply(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };
    private static HttpResponseMessage Success(long id) => Reply(JsonSerializer.Serialize(new
    { jsonrpc = "2.0", id, result = new { success = true, code = 1, ControllerSN = 253260812 } }));
    private sealed class FakeHttp(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { Calls++; return send(request, ct); }
    }
    private sealed class StubClient : ITurnstileCommandClient
    {
        public int Calls { get; private set; }
        public Func<Task> OnCall { get; set; } = () => Task.CompletedTask;
        public Task OpenDoorAsync(int controllerSN, int doorNo, CancellationToken ct) { Calls++; return OnCall(); }
        public async Task<ControllerStatusSnapshot> QueryStatusAsync(int controllerSN, CancellationToken ct)
        {
            Calls++; await OnCall();
            return new(controllerSN, "2026-10-07 12:00:00", new Dictionary<string, string> { ["Door1#"] = "Closed" });
        }
    }
}
