namespace gym_system.Application.TicketPlansUseCase.Queries
{
    public sealed class StudentTicketPlanEligibilityContext
    {
        public required string StudentId { get; init; }
        public required bool IsActiveStudent { get; init; }
        public DateTime? StudentAssignedAt { get; init; }
        public required DateTime Now { get; init; }
    }
}
