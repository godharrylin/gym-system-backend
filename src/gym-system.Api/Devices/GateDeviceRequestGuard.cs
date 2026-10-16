using gym_system.Application.GateDevicesUseCase;

namespace gym_system.Api.Devices;

// Kept separate from the Admin JWT/CORS pipeline. Kiosks use a same-origin /api proxy.
public sealed class GateDeviceRequestGuard(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, GateDeviceSettings settings)
    {
        if (!context.Request.Path.StartsWithSegments("/api/v1/gate-devices"))
        { await next(context); return; }

        context.Response.Headers.CacheControl = "no-store";
        if (!settings.Enabled)
        { await Reject(context, 503, "GATE_DEVICES_DISABLED", "裝置配對功能尚未啟用。"); return; }
        if (!settings.AllowHttp && !context.Request.IsHttps)
        { await Reject(context, 400, "HTTPS_REQUIRED", "裝置授權需要 HTTPS。"); return; }

        // Version 1 only permits same-origin browser requests. The frontend must expose
        // /api through its reverse proxy; the device cookie is never a CORS credential.
        var origin = context.Request.Headers.Origin.ToString();
        var ownOrigin = $"{context.Request.Scheme}://{context.Request.Host}";
        var allowed = origin.Equals(ownOrigin, StringComparison.OrdinalIgnoreCase);
        if ((origin.Length != 0 && !allowed) ||
            (HttpMethods.IsPost(context.Request.Method) && origin.Length == 0) ||
            context.Request.Headers["Sec-Fetch-Site"] == "cross-site")
        { await Reject(context, 403, "GATE_ORIGIN_REJECTED", "不允許從此來源操作裝置。"); return; }

        if (context.Request.Headers["X-Gate-Request"] != "1")
        { await Reject(context, 403, "GATE_REQUEST_REJECTED", "缺少裝置請求識別。"); return; }
        if (HttpMethods.IsPost(context.Request.Method) && !context.Request.HasJsonContentType())
        { await Reject(context, 415, "JSON_REQUIRED", "請使用 JSON 請求。"); return; }
        await next(context);
    }

    private static Task Reject(HttpContext context, int status, string code, string message)
    {
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(new { code, message, traceId = context.TraceIdentifier });
    }
}
