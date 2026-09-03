namespace gym_system.Api.Contracts.TicketPasses
{
    public sealed class GetStudentTicketPassesResponse
    {
        public IReadOnlyList<StudentTicketPassDto> TicketPasses { get; init; } = [];
    }

    public sealed class StudentTicketPassDto
    {
        public required string PassId { get; init; }
        public required string PlanCode { get; init; }
        public required string PlanName { get; init; }
        public required string PlanType { get; init; }
        public required string ValidStatus { get; init; }
        public required string PaymentStatus { get; init; }
        public required decimal UnitPrice { get; init; }
        public required string PaidAt { get; init; }
        public string? ValidStartDate { get; init; }
        public string? ValidEndDate { get; init; }
        public int? CreditsTotal { get; init; }
        public int? CreditsRemaining { get; init; }
        public bool CanCancel { get; init; }
    }
}
