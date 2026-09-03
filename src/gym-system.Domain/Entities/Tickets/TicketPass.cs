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
            DateOnly? validStartDate,
            DateOnly? validEndDate,
            TicketValidStatus validStatus,
            PaymentState paymentState,
            int creditsTotal,
            int creditsRemaining,
            int? renewedFromPassSn,
            DateTime? endedAt,
            TicketEndReason? endReason,
            DateTime? paidAt)
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
            RenewedFromPassSn = renewedFromPassSn;
            EndedAt = endedAt;
            EndReason = endReason;
            PaidAt = paidAt;
        }

        public string Id { get; }
        public string OwnerId { get; }
        public string OrderId { get; }
        public string OrderItemId { get; }
        public TicketPlanKind Plan { get; }
        public DateOnly? ValidStartDate { get; private set; }
        public DateOnly? ValidEndDate { get; private set; }
        public TicketValidStatus ValidStatus { get; private set; }
        public PaymentState PaymentState { get; }
        public int CreditsTotal { get; }
        public int CreditsRemaining { get; private set; }
        public int? RenewedFromPassSn { get; }
        public DateTime? EndedAt { get; private set; }
        public TicketEndReason? EndReason { get; private set; }
        public DateTime? PaidAt { get; }

        public static TicketPass IssueQueued(
            string id,
            string ownerId,
            string orderId,
            string orderItemId,
            TicketPlanKind plan,
            int? renewedFromPassSn = null,
            DateTime? paidAt = null)
        {
            var credits = plan.Type == TicketPlanType.Pack
                ? plan.DefaultCredit ?? throw new InvalidOperationException("堂票方案缺少預設堂數")
                : 0;

            return new TicketPass(
                id: id,
                ownerId: ownerId,
                orderId: orderId,
                orderItemId: orderItemId,
                plan: plan,
                validStartDate: null,
                validEndDate: null,
                validStatus: TicketValidStatus.UnActive,
                paymentState: PaymentState.Paid,
                creditsTotal: credits,
                creditsRemaining: credits,
                renewedFromPassSn: renewedFromPassSn,
                endedAt: null,
                endReason: null,
                paidAt: paidAt);
        }

        public void Activate(DateOnly activationDate)
        {
            if (ValidStatus != TicketValidStatus.UnActive)
            {
                throw new InvalidOperationException("只有未啟用票券可以啟用");
            }

            ValidStatus = TicketValidStatus.Active;
            if (Plan.Id.Equals("SINGLE", StringComparison.OrdinalIgnoreCase))
            {
                ValidStartDate = null;
                ValidEndDate = null;
                return;
            }

            var expireDays = Plan.DefaultExpireDays
                ?? throw new InvalidOperationException("非單次票方案缺少有效天數");
            ValidStartDate = activationDate;
            ValidEndDate = TicketActivationSchedule.GetEndDate(activationDate, expireDays);
        }

        public void RefreshStatus(DateOnly today)
        {
            if (ValidStatus != TicketValidStatus.Active)
            {
                return;
            }

            if (Plan.Type == TicketPlanType.Pack && CreditsRemaining <= 0)
            {
                if (EndedAt is null)
                {
                    throw new InvalidOperationException("堂票已用完但缺少實際用完時間");
                }

                End(TicketEndReason.Depleted, EndedAt.Value);
                return;
            }

            if (ValidEndDate is not null && ValidEndDate.Value < today)
            {
                End(
                    TicketEndReason.Expire,
                    ValidEndDate.Value.ToDateTime(TimeOnly.MinValue));
            }
        }

        public void MarkDepleted(DateTime endedAt)
        {
            if (Plan.Type != TicketPlanType.Pack || CreditsRemaining > 0)
            {
                throw new InvalidOperationException("只有堂數已歸零的堂票可以標記為用完");
            }

            End(TicketEndReason.Depleted, endedAt);
        }

        public void UseCredit(DateTime usedAt)
        {
            if (ValidStatus != TicketValidStatus.Active || Plan.Type != TicketPlanType.Pack)
            {
                throw new InvalidOperationException("只有使用中的堂票可以核銷");
            }

            if (CreditsRemaining <= 0)
            {
                throw new InvalidOperationException("票券堂數已用完");
            }

            CreditsRemaining--;
            if (CreditsRemaining == 0)
            {
                End(TicketEndReason.Depleted, usedAt);
            }
        }

        public void Cancel(DateTime cancelledAt)
        {
            if (RenewedFromPassSn is null)
            {
                throw new InvalidOperationException("只有續約票可以取消");
            }

            if (ValidStatus != TicketValidStatus.UnActive)
            {
                throw new InvalidOperationException("只有尚未啟用的續約票可以取消");
            }

            if (Plan.Type == TicketPlanType.Pack && CreditsRemaining != CreditsTotal)
            {
                throw new InvalidOperationException("已使用的續約票不能取消");
            }

            End(TicketEndReason.Cancelled, cancelledAt);
        }

        private void End(TicketEndReason reason, DateTime endedAt)
        {
            if (ValidStatus is TicketValidStatus.Expire
                or TicketValidStatus.Depleted
                or TicketValidStatus.Cancelled)
            {
                throw new InvalidOperationException("票券已經結束");
            }

            ValidStatus = reason switch
            {
                TicketEndReason.Expire => TicketValidStatus.Expire,
                TicketEndReason.Depleted => TicketValidStatus.Depleted,
                TicketEndReason.Cancelled => TicketValidStatus.Cancelled,
                _ => throw new InvalidOperationException("不支援的票券結束原因")
            };
            EndedAt = endedAt;
            EndReason = reason;
        }

        public static TicketPass Rehydrate(
            string id,
            string ownerId,
            string orderId,
            string orderItemId,
            TicketPlanKind plan,
            DateOnly? validStartDate,
            DateOnly? validEndDate,
            TicketValidStatus validStatus,
            PaymentState paymentState,
            int creditsTotal,
            int creditsRemaining,
            int? renewedFromPassSn = null,
            DateTime? endedAt = null,
            TicketEndReason? endReason = null,
            DateTime? paidAt = null)
        {
            return new TicketPass(
                id,
                ownerId,
                orderId,
                orderItemId,
                plan,
                validStartDate,
                validEndDate,
                validStatus,
                paymentState,
                creditsTotal,
                creditsRemaining,
                renewedFromPassSn,
                endedAt,
                endReason,
                paidAt);
        }

        public CurrentTicketSnapshot ToSnapshot(string? ticketId = null)
        {
            return new CurrentTicketSnapshot
            {
                TicketId = string.IsNullOrWhiteSpace(ticketId) ? Id : ticketId,
                TicketType = Plan.Type == TicketPlanType.Pack ? "PACK" : "M_PASS",
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
        Depleted = 4,
        Cancelled = 5
    }

    public enum TicketEndReason
    {
        Expire = 1,
        Depleted = 2,
        Cancelled = 3
    }
}
