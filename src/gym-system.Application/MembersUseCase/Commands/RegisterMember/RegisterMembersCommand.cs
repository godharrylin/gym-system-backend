using gym_system.Domain.Enums;

namespace gym_system.Application.MembersUseCase.Commands.RegisterMember
{
    public sealed class RegisterMembersCommand
    {
        public IReadOnlyList<MemberRegisterInput> Members { get; init; } = Array.Empty<MemberRegisterInput>();
        public TicketPurchaseInput? TicketPurchase { get; init; }
        public string OperatorId { get; init; } = "SYSTEM";
    }

    public sealed class MemberRegisterInput
    {
        public string Name { get; init; } = string.Empty;
        public string Phone { get; init; } = string.Empty;
    }

    public sealed class TicketPurchaseInput
    {
        public string TicketPlanKindId { get; init; } = string.Empty;
        public DateOnly ActivationDate { get; init; }
        public RegisterPaymentStatus PaymentStatus { get; init; }
    }

    public enum RegisterPaymentStatus
    {
        Paid = 1,
        UnPaid = 2
    }

}
