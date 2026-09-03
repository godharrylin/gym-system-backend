namespace gym_system.Api.Contracts
{
    public sealed class RegisterMembersRequest
    {
        public List<MemberItem> Members { get; init; } = [];
        public TicketPurchaseItem? TicketPurchase { get; init; }
    }

    public sealed class MemberItem
    {
        public string Name { get; init; } = string.Empty;
        public string Phone { get; init; } = string.Empty;
    }

    public sealed class TicketPurchaseItem
    {
        public string TicketPlanKindId { get; init; } = string.Empty;
        public int Quantity { get; init; } = 1;
        public string PaymentStatus { get; init; } = "UnPaid";
    }
}
