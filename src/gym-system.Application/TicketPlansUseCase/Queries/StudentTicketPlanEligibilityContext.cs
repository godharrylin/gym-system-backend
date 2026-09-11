namespace gym_system.Application.TicketPlansUseCase.Queries
{
    public sealed class StudentTicketPlanEligibilityContext
    {
        public string? StudentId { get; init; }
        public TicketPlanEligibilityContextKind Kind { get; init; } =
            TicketPlanEligibilityContextKind.ExistingMember;
        public required bool IsActiveStudent { get; init; }
        public DateTime? StudentAssignedAt { get; init; }
        public required DateTime Now { get; init; }

        public bool IsRegistration =>
            Kind == TicketPlanEligibilityContextKind.Registration;
    }
}
