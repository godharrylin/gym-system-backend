namespace gym_system.Application.MembersUseCase.Commands.RegisterMember
{
    public sealed class RegisterMembersResult
    {
        public IReadOnlyList<string> MemberIds { get; init; } = Array.Empty<string>();
        public string? OrderId { get; init; }
        public decimal? TotalAmount { get; init; }
        public decimal? ActualAmount { get; init; }
    }
}
