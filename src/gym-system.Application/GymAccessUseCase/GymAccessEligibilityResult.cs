using gym_system.Domain.Entities.Members;
using gym_system.Domain.Enums;

namespace gym_system.Application.GymAccessUseCase;

public enum GymAccessIdentityKind { Unknown, IdentifiedUser, Staff, Student }

public sealed record GymAccessEligibilityResult
{
    // This is deliberately NOT named CanOpenDoor/CanEnter: stage 01 is not full authorization.
    public required bool BasicEligibilityPassed { get; init; }
    public required string Code { get; init; }
    public required string Message { get; init; }
    public required DateTime EvaluatedAt { get; init; }
    public string? UserId { get; init; }
    public GymAccessIdentityKind IdentityKind { get; init; }
    // Preserve all active roles, without inventing precedence between Admin/Staff/Instructor.
    public IReadOnlyList<UserRoleCode> ActiveRoles { get; init; } = [];
    public CurrentTicketSnapshot? CurrentTicket { get; init; }
    public IReadOnlyList<string> WarningCodes { get; init; } = [];
    public bool RequiresClassAndReservationCheck =>
        BasicEligibilityPassed && IdentityKind == GymAccessIdentityKind.Student;
}
