using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using gym_system.Api.Controllers;
using gym_system.Api.Devices;
using gym_system.Application.GateDevicesUseCase;
using gym_system.Domain.Entities.Devices;
using gym_system.Domain.Repositories;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace gym_system.Api.Tests;

public sealed class GateDeviceHttpTests
{
    [Fact]
    public async Task RealHttpCookieSurvivesApiRestart_AndReplacementAndRevocationRejectOldBrowser()
    {
        var repository = new TestRepository();
        var keyPath = Path.Combine(Path.GetTempPath(), "gym-gate-http-" + Guid.NewGuid().ToString("N"));
        var cookies = new CookieContainer();
        await using (var app = await Start(repository, keyPath))
        {
            using var browser = Browser(app, cookies);
            using var unauthorized = await browser.GetAsync("/api/v1/gate-devices/session");
            Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
            Assert.Null(unauthorized.Headers.Location);
            using var invalid = await browser.PostAsJsonAsync("/api/v1/gate-devices/pairing-preview", new { code = "wrong" });
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            await Pair(browser);
            Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/api/v1/gate-devices/session")).StatusCode);
            var cookie = Assert.Single(cookies.GetCookies(new Uri(browser.BaseAddress!, "/api/v1/gate-devices/session")).Cast<Cookie>());
            Assert.True(cookie.HttpOnly);
            Assert.False(cookie.Secure); // Explicitly approved internal HTTP mode.
            Assert.True(cookie.Expires > DateTime.Now.AddDays(300));
        }

        // New host/port, same persisted keys and database state: protected cookie still validates.
        await using (var app = await Start(repository, keyPath))
        {
            using var original = Browser(app, cookies);
            Assert.Equal(HttpStatusCode.OK, (await original.GetAsync("/api/v1/gate-devices/session")).StatusCode);
            using var replacement = Browser(app, new CookieContainer());
            using var previewResponse = await replacement.PostAsJsonAsync("/api/v1/gate-devices/pairing-preview", new { code = "test-entry" });
            previewResponse.EnsureSuccessStatusCode();
            using var preview = JsonDocument.Parse(await previewResponse.Content.ReadAsStringAsync());
            Assert.True(preview.RootElement.GetProperty("requiresReplacement").GetBoolean());
            // Merely previewing a replacement must not revoke the current browser.
            Assert.Equal(HttpStatusCode.OK, (await original.GetAsync("/api/v1/gate-devices/session")).StatusCode);
            using var paired = await replacement.PostAsJsonAsync("/api/v1/gate-devices/pair", new
            { code = "test-entry", previewToken = preview.RootElement.GetProperty("previewToken").GetString() });
            paired.EnsureSuccessStatusCode();
            Assert.Equal(HttpStatusCode.Unauthorized, (await original.GetAsync("/api/v1/gate-devices/session")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await replacement.GetAsync("/api/v1/gate-devices/session")).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await replacement.PostAsJsonAsync("/api/v1/gate-devices/revoke", new { })).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await replacement.GetAsync("/api/v1/gate-devices/session")).StatusCode);
        }
    }

    private static async Task Pair(HttpClient browser)
    {
        using var response = await browser.PostAsJsonAsync("/api/v1/gate-devices/pairing-preview", new { code = "test-entry" });
        response.EnsureSuccessStatusCode();
        using var preview = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        using var paired = await browser.PostAsJsonAsync("/api/v1/gate-devices/pair", new
        { code = "test-entry", previewToken = preview.RootElement.GetProperty("previewToken").GetString() });
        paired.EnsureSuccessStatusCode();
    }

    private static HttpClient Browser(WebApplication app, CookieContainer cookies)
    {
        var client = new HttpClient(new HttpClientHandler { CookieContainer = cookies, AllowAutoRedirect = false })
        { BaseAddress = new Uri(app.Urls.Single()) };
        client.DefaultRequestHeaders.Add("Origin", client.BaseAddress.GetLeftPart(UriPartial.Authority));
        client.DefaultRequestHeaders.Add("X-Gate-Request", "1");
        return client;
    }

    private static async Task<WebApplication> Start(TestRepository repository, string keyPath)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.Logging.ClearProviders(); // No raw request headers/cookies/codes in test logs.
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        builder.Services.AddControllers().AddApplicationPart(typeof(GateDevicesController).Assembly);
        builder.Services.AddCors();
        builder.Services.AddAuthentication("Bearer").AddJwtBearer();
        builder.Services.AddAuthorization();
        builder.Services.AddGateDevices(new()
        { Enabled = true, AllowHttp = true, EntryPairingCode = "test-entry", ExitPairingCode = "test-exit", KeyRingPath = keyPath });
        builder.Services.AddSingleton<IGateDeviceRepository>(repository);
        builder.Services.AddSingleton<IGatePassageRepository>(new StubPassages());
        builder.Services.AddSingleton<IUnitOfWork>(new NoopWork());
        builder.Services.AddSingleton<IClock>(new TestClock());
        var app = builder.Build();
        app.UseRouting();
        app.UseMiddleware<GateDeviceRequestGuard>();
        app.UseRateLimiter();
        app.UseCors();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        await app.StartAsync();
        return app;
    }

    private sealed class TestRepository : IGateDeviceRepository
    {
        private GateStation station = new(1, "ENTRY_MAIN", "test entry", GateDirection.Entry, true, null);
        private readonly Dictionary<Guid, GateDeviceSession> sessions = [];
        public Task<GateStation?> FindStationAsync(string code, bool forUpdate, CancellationToken ct) =>
            Task.FromResult<GateStation?>(code == station.Code ? station : null);
        public Task<GateDeviceAuthorization?> FindAuthorizationAsync(Guid id, CancellationToken ct) =>
            Task.FromResult(sessions.TryGetValue(id, out var value) ? new GateDeviceAuthorization(station, value) : null);
        public Task ReplaceAsync(GateStation value, Guid id, DateTime now, CancellationToken ct)
        {
            if (station.CurrentSessionId is { } old) sessions[old] = sessions[old] with { Status = "Revoked" };
            sessions[id] = new(id, station.Id, "Active", now, null);
            station = station with { CurrentSessionId = id };
            return Task.CompletedTask;
        }
        public Task RevokeAsync(GateStation value, string operatorId, DateTime now, CancellationToken ct)
        {
            sessions[station.CurrentSessionId!.Value] = sessions[station.CurrentSessionId.Value] with { Status = "Revoked" };
            station = station with { CurrentSessionId = null };
            return Task.CompletedTask;
        }
    }

    private sealed class TestClock : IClock
    {
        public DateTime Now() => new(2026, 10, 16, 19, 10, 0);
        public DateOnly Today() => DateOnly.FromDateTime(Now());
    }

    private sealed class NoopWork : IUnitOfWork
    {
        public Task BeginAsync(CancellationToken ct) => Task.CompletedTask;
        public Task CommitAsync(CancellationToken ct) => Task.CompletedTask;
        public Task RollbackAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class StubPassages : IGatePassageRepository
    {
        public Task<GatePassageRecord?> FindByRequestIdAsync(Guid requestId, CancellationToken ct) => Task.FromResult<GatePassageRecord?>(null);
        public Task<bool> TryCreateAsync(Guid requestId, GateDeviceAuthorization device, string? userId,
            string identityKind, string roleSnapshot, DateTime requestedAt, CancellationToken ct) => Task.FromResult(true);
        public Task<bool> CompleteAsync(Guid requestId, string outcome, string resultCode, bool isSuccess,
            bool isSimulated, bool affectsPresence, DateTime respondedAt, string? remark, CancellationToken ct) => Task.FromResult(true);
    }
}
