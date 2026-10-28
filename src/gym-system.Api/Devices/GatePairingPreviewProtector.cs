using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace gym_system.Api.Devices;

public sealed record GatePairingPreview(string StationCode, Guid? ExpectedSessionId, DateTimeOffset ExpiresAt);

public sealed class GatePairingPreviewProtector(IDataProtectionProvider provider, TimeProvider time)
{
    private readonly IDataProtector protector = provider.CreateProtector("GymSystem.GateDevices.PairingPreview.v1");

    public string Create(string stationCode, Guid? expectedSessionId) =>
        protector.Protect(JsonSerializer.Serialize(new GatePairingPreview(stationCode, expectedSessionId, time.GetUtcNow().AddMinutes(5))));

    public GatePairingPreview? Read(string? token)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 4096) return null;
        try
        {
            var preview = JsonSerializer.Deserialize<GatePairingPreview>(protector.Unprotect(token));
            return preview is not null && time.GetUtcNow() < preview.ExpiresAt ? preview : null;
        }
        catch (Exception error) when (error is CryptographicException or JsonException or FormatException)
        { return null; }
    }
}
