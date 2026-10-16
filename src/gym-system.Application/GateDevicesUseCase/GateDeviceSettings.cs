using System.Security.Cryptography;
using System.Text;

namespace gym_system.Application.GateDevicesUseCase;

public sealed class GateDeviceSettings
{
    public bool Enabled { get; set; }
    public bool AllowHttp { get; set; }
    public string EntryPairingCode { get; set; } = "";
    public string ExitPairingCode { get; set; } = "";
    public string KeyRingPath { get; set; } = "";

    public void Validate()
    {
        if (!Enabled) return;
        if (string.IsNullOrWhiteSpace(EntryPairingCode) || string.IsNullOrWhiteSpace(ExitPairingCode) ||
            EntryPairingCode.Length > 128 || ExitPairingCode.Length > 128 || EntryPairingCode == ExitPairingCode)
            throw new InvalidOperationException("GateDevices needs two distinct pairing codes (maximum 128 characters).");
        if (string.IsNullOrWhiteSpace(KeyRingPath) || !Path.IsPathFullyQualified(KeyRingPath))
            throw new InvalidOperationException("GateDevices:KeyRingPath must be an absolute persistent directory.");
    }

    public string? ResolveStation(string? code)
    {
        if (!Enabled || string.IsNullOrEmpty(code) || code.Length > 128) return null;
        var supplied = SHA256.HashData(Encoding.UTF8.GetBytes(code));
        // Evaluate both comparisons, and never log the submitted or configured code.
        var entry = CryptographicOperations.FixedTimeEquals(supplied, SHA256.HashData(Encoding.UTF8.GetBytes(EntryPairingCode)));
        var exit = CryptographicOperations.FixedTimeEquals(supplied, SHA256.HashData(Encoding.UTF8.GetBytes(ExitPairingCode)));
        return entry ? "ENTRY_MAIN" : exit ? "EXIT_MAIN" : null;
    }
}
