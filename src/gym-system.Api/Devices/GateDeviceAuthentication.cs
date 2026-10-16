using System.Security.Claims;
using System.Threading.RateLimiting;
using gym_system.Application.GateDevicesUseCase;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;

namespace gym_system.Api.Devices;

public static class GateDeviceAuthentication
{
    public const string Scheme = "GateDevice";
    public const string SessionClaim = "gate_session_id";

    public static IServiceCollection AddGateDevices(this IServiceCollection services, GateDeviceSettings settings)
    {
        settings.Validate();
        services.AddSingleton(settings);
        services.AddScoped<GateDeviceService>();
        services.AddScoped<GateDeviceCookieEvents>();
        services.AddSingleton<GatePairingPreviewProtector>();
        services.AddSingleton(TimeProvider.System);
        var protection = services.AddDataProtection().SetApplicationName("GymSystem.GateDevices.v1");
        if (settings.Enabled)
            protection.PersistKeysToFileSystem(new DirectoryInfo(settings.KeyRingPath));
        services.AddAuthentication().AddCookie(Scheme, options =>
        {
            options.Cookie.Name = "Gym.GateDevice";
            options.Cookie.Path = "/api/v1";
            options.Cookie.HttpOnly = true;
            options.Cookie.IsEssential = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = settings.AllowHttp ? CookieSecurePolicy.None : CookieSecurePolicy.Always;
            // Transport lifetime, NOT a DB authorization deadline. Daily use renews it.
            // Browsers can clear/cap persistent cookies; no server can promise infinite browser storage.
            options.ExpireTimeSpan = TimeSpan.FromDays(365);
            options.SlidingExpiration = true;
            options.EventsType = typeof(GateDeviceCookieEvents);
        });
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy("GatePairing", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });
        return services;
    }

    public static Guid? GetSessionId(ClaimsPrincipal? principal) =>
        principal?.Identity?.AuthenticationType == Scheme &&
        Guid.TryParse(principal.FindFirstValue(SessionClaim), out var id) ? id : null;

    public static Task SignInAsync(HttpContext context, Guid sessionId) =>
        context.SignInAsync(Scheme,
            new ClaimsPrincipal(new ClaimsIdentity([new Claim(SessionClaim, sessionId.ToString("D"))], Scheme)),
            new AuthenticationProperties { IsPersistent = true, AllowRefresh = true });
}

public sealed class GateDeviceCookieEvents(GateDeviceService devices, TimeProvider time) : CookieAuthenticationEvents
{
    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        var id = GateDeviceAuthentication.GetSessionId(context.Principal);
        if (id is null || await devices.FindValidAsync(id.Value, context.HttpContext.RequestAborted) is null)
        {
            context.RejectPrincipal();
            return;
        }
        // Renew only after DB validation. A revoked cookie must never revive itself.
        if (context.Properties.IssuedUtc is { } issued && time.GetUtcNow() - issued >= TimeSpan.FromDays(1))
            context.ShouldRenew = true;
    }

    public override Task RedirectToLogin(RedirectContext<CookieAuthenticationOptions> context)
    { context.Response.StatusCode = 401; return Task.CompletedTask; }

    public override Task RedirectToAccessDenied(RedirectContext<CookieAuthenticationOptions> context)
    { context.Response.StatusCode = 403; return Task.CompletedTask; }
}
