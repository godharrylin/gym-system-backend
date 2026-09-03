namespace gym_system.Application.TicketsUseCase.Queries
{
    public interface IStudentTicketPassQueryService
    {
        Task<IReadOnlyList<StudentTicketPassResult>> GetByStudentIdAsync(
            string studentId,
            CancellationToken ct);
    }

    public sealed class StudentTicketPassResult
    {
        public required string PassId { get; init; }
        public required string PlanCode { get; init; }
        public required string PlanName { get; init; }
        public required string PlanType { get; init; }
        public required string ValidStatus { get; init; }
        public required string PaymentStatus { get; init; }
        public required decimal UnitPrice { get; init; }
        public required DateTime PaidAt { get; init; }
        public DateTime? ValidStartDate { get; init; }
        public DateTime? ValidEndDate { get; init; }
        public int? CreditsTotal { get; init; }
        public int? CreditsRemaining { get; init; }
        public bool CanCancel { get; init; }
    }
}
