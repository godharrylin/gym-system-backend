using System.ComponentModel.DataAnnotations;

namespace gym_system.Api.Contracts.Devices;

public sealed class DeviceTestRequest
{
    [Required]
    public string Direction { get; init; } = "";
}
