using System.Security.Claims;
using gym_system.Api.Devices;
using gym_system.Application.GateDevicesUseCase;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace gym_system.Api.Tests;

public sealed class GateDeviceSecurityTests
{
    [Fact]
    public async Task SchemeKeepsJwtDefault_AndHttpCookieIsHttpOnlyStrictPersistentCapable()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthentication("Bearer").AddJwtBearer();
        services.AddGateDevices(new() { AllowHttp = true });
        using var provider = services.BuildServiceProvider();
        Assert.Equal("Bearer", (await provider.GetRequiredService<IAuthenticationSchemeProvider>().GetDefaultAuthenticateSchemeAsync())!.Name);
        var cookie = provider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(GateDeviceAuthentication.Scheme);
        Assert.True(cookie.Cookie.HttpOnly);
        Assert.Equal(SameSiteMode.Strict, cookie.Cookie.SameSite);
        Assert.Equal(CookieSecurePolicy.None, cookie.Cookie.SecurePolicy);
        Assert.True(cookie.SlidingExpiration);
        Assert.Equal(TimeSpan.FromDays(365), cookie.ExpireTimeSpan);
        Assert.Null(GateDeviceAuthentication.GetSessionId(new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(GateDeviceAuthentication.SessionClaim, Guid.NewGuid().ToString())], "Bearer"))));
    }

    [Theory]
    [InlineData("http://gym.local", "1", "application/json", 200)]
    [InlineData("http://evil.local", "1", "application/json", 403)]
    [InlineData("", "1", "application/json", 403)]
    [InlineData("http://gym.local", "", "application/json", 403)]
    [InlineData("http://gym.local", "1", "text/plain", 415)]
    public async Task HttpGuardRejectsCrossOriginAndSimpleFormRequests(string origin, string header, string contentType, int status)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.Request.Path = "/api/v1/gate-devices/pair";
        context.Request.Scheme = "http";
        context.Request.Host = new("gym.local");
        context.Request.Method = "POST";
        context.Request.ContentType = contentType;
        context.Request.Headers.Origin = origin;
        context.Request.Headers["X-Gate-Request"] = header;
        var nextCalls = 0;
        await new GateDeviceRequestGuard(_ => { nextCalls++; return Task.CompletedTask; })
            .InvokeAsync(context, new() { Enabled = true, AllowHttp = true });
        Assert.Equal(status, context.Response.StatusCode);
        Assert.Equal(status == 200 ? 1 : 0, nextCalls);
        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public void PreviewTokenIsProtected_ExpiresAtFiveMinutes_AndDoesNotContainPairingCode()
    {
        var time = new TestTime();
        var protector = new GatePairingPreviewProtector(new EphemeralDataProtectionProvider(), time);
        var id = Guid.NewGuid();
        var token = protector.Create("ENTRY_MAIN", id);
        Assert.Equal(id, protector.Read(token)!.ExpectedSessionId);
        Assert.Null(protector.Read("forged"));
        Assert.DoesNotContain("ENTRY_MAIN", token);
        time.At = time.At.AddMinutes(5);
        Assert.Null(protector.Read(token));
    }

    private sealed class TestTime : TimeProvider
    {
        public DateTimeOffset At = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => At;
    }
}
