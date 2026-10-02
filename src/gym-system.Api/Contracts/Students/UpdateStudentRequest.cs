using System.ComponentModel.DataAnnotations;

namespace gym_system.Api.Contracts.Students;

public sealed class UpdateStudentRequest
{
    [Required, StringLength(50)] public string Name { get; init; } = "";
    [Required, StringLength(20)] public string Phone { get; init; } = "";
}
