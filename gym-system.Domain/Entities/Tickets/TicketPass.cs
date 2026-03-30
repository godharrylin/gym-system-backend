using gym_system.Domain.Entities.Members;
using gym_system.Domain.Enums;

namespace gym_system.Domain.Entities.Tickets
{
    public sealed class TicketPass
    {
        private TicketPass(
            string id,
            string ownerId,
            string orderId,
            string orderItemId,
            TicketPlanKind plan,
            DateOnly validStartDate,
            DateOnly? validEndDate,
            TicketValidStatus validStatus,
            PaymentState paymentState,
            int creditsTotal,
            int creditsRemaining)
        {
            Id = id;
            OwnerId = ownerId;
            OrderId = orderId;
            OrderItemId = orderItemId;
            Plan = plan;
            ValidStartDate = validStartDate;
            ValidEndDate = validEndDate;
            ValidStatus = validStatus;
            PaymentState = paymentState;
            CreditsTotal = creditsTotal;
            CreditsRemaining = creditsRemaining;
        }

        public string Id { get; }
        public string OwnerId { get; }
        public string OrderId { get; }
        public string OrderItemId { get; }
        public TicketPlanKind Plan { get; }
        public DateOnly ValidStartDate { get; }
        public DateOnly? ValidEndDate { get; }
        public TicketValidStatus ValidStatus { get; }
        public PaymentState PaymentState { get; }
        public int CreditsTotal { get; }
        public int CreditsRemaining { get; }

        public static TicketPass Issue(
            string id,
            string ownerId,
            string orderId,
            string orderItemId,
            TicketPlanKind plan,
            DateOnly activationDate,
            PaymentState paymentState,
            DateOnly today)
        {
            DateOnly? expireDate = plan.DefaultExpireDays == -1
                ? null
                : activationDate.AddDays(plan.DefaultExpireDays);

            var validStatus = activationDate <= today ? TicketValidStatus.Active : TicketValidStatus.UnActive;

            return new TicketPass(
                id: id,
                ownerId: ownerId,
                orderId: orderId,
                orderItemId: orderItemId,
                plan: plan,
                validStartDate: activationDate,
                validEndDate: expireDate,
                validStatus: validStatus,
                paymentState: paymentState,
                creditsTotal: plan.DefaultCredit,
                creditsRemaining: plan.DefaultCredit);
        }

        public CurrentTicketSnapshot ToSnapshot()
        {
            return new CurrentTicketSnapshot
            {
                TicketId = Id,
                TicketType = Plan.Type.ToString(),
                TicketValidState = ValidStatus.ToString(),
                TicketPaymentState = PaymentState.ToString(),
                TicketRemainCount = Plan.Type == TicketPlanType.Pack ? CreditsRemaining : null,
                TicketExpireDate = ValidEndDate,
                UpdatedAt = DateTime.UtcNow
            };
        }
    }

    public enum TicketValidStatus
    {
        UnActive = 1,
        Active = 2,
        Expire = 3,
        Depleted = 4
    }
}
