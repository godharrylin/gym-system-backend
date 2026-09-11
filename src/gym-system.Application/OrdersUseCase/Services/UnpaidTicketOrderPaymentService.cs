using gym_system.Application.TicketPlansUseCase.Queries;
using gym_system.Domain.Entities.Tickets;
using gym_system.Domain.Enums;
using gym_system.Domain.Exceptions;
using gym_system.Domain.Repositories;

namespace gym_system.Application.OrdersUseCase.Services
{
    public sealed class UnpaidTicketOrderPaymentService
    {
        private const int SingleMaxQuantity = 5;

        private readonly IUserRepository _userRepository;
        private readonly IUserRoleRepository _userRoleRepository;
        private readonly IStudentProfileRepository _studentProfileRepository;
        private readonly ITicketPlanRepository _ticketPlanRepository;
        private readonly ITicketPlanCatalogQueryService _ticketPlanCatalogQueryService;
        private readonly ITicketPlanEligibilityService _ticketPlanEligibilityService;
        private readonly RenewalTicketPassEligibilityService _renewalEligibilityService;
        private readonly IOrderRepository _orderRepository;
        private readonly ITicketPassRepository _ticketPassRepository;
        private readonly IClock _clock;

        public UnpaidTicketOrderPaymentService(
            IUserRepository userRepository,
            IUserRoleRepository userRoleRepository,
            IStudentProfileRepository studentProfileRepository,
            ITicketPlanRepository ticketPlanRepository,
            ITicketPlanCatalogQueryService ticketPlanCatalogQueryService,
            ITicketPlanEligibilityService ticketPlanEligibilityService,
            RenewalTicketPassEligibilityService renewalEligibilityService,
            IOrderRepository orderRepository,
            ITicketPassRepository ticketPassRepository,
            IClock clock)
        {
            _userRepository = userRepository;
            _userRoleRepository = userRoleRepository;
            _studentProfileRepository = studentProfileRepository;
            _ticketPlanRepository = ticketPlanRepository;
            _ticketPlanCatalogQueryService = ticketPlanCatalogQueryService;
            _ticketPlanEligibilityService = ticketPlanEligibilityService;
            _renewalEligibilityService = renewalEligibilityService;
            _orderRepository = orderRepository;
            _ticketPassRepository = ticketPassRepository;
            _clock = clock;
        }

        public async Task PayAsync(
            string orderId,
            string paymentMethod,
            string operatorId,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(orderId))
            {
                throw new InvalidOperationException("訂單 ID 必填");
            }

            if (!paymentMethod.Equals("Cash", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("付款方式目前只支援 Cash");
            }

            var unpaidOrder = await _orderRepository.FindUnpaidTicketOrderAsync(
                orderId.Trim(),
                acquireLock: true,
                ct) ?? throw new InvalidOperationException("訂單不存在、已付款或不可付款");

            var student = await _userRepository.FindUserByIdAsync(unpaidOrder.BuyerId, ct)
                ?? throw new KeyNotFoundException("學生不存在");
            var studentRole = await _userRoleRepository.GetUserRoleAsync(
                unpaidOrder.BuyerId,
                UserRoleCode.Student,
                ct);
            if (!student.IsActive || studentRole is null || !studentRole.IsActive)
            {
                throw new InvalidOperationException("學生不是有效學生");
            }

            if (await _studentProfileRepository.FindByUserIdAsync(unpaidOrder.BuyerId, ct) is null)
            {
                throw new InvalidOperationException("學生沒有 Profile");
            }

            var plan = await _ticketPlanRepository.GetActiveByIdAsync(
                unpaidOrder.TicketPlanKindCode,
                ct) ?? throw new KeyNotFoundException("票券方案不存在或未上架");
            ValidateQuantity(plan.Id, unpaidOrder.Quantity);
            if (unpaidOrder.UnitPrice != plan.Price
                || unpaidOrder.TotalAmount != plan.Price * unpaidOrder.Quantity
                || unpaidOrder.ActualAmount != unpaidOrder.TotalAmount)
            {
                throw new TicketPurchaseRejectedException(
                    "TICKET_PLAN_PRICE_CHANGED",
                    "方案價格已變更，請取消原訂單後重新建立");
            }

            var planResult = (await _ticketPlanCatalogQueryService.GetActiveTicketPlansAsync(ct))
                .FirstOrDefault(x => x.Id.Equals(plan.Id, StringComparison.OrdinalIgnoreCase))
                ?? throw new KeyNotFoundException("票券方案不存在或未上架");

            await _ticketPassRepository.LockOwnerAsync(unpaidOrder.BuyerId, ct);
            // Delayed payment, including registration orders, revalidates the
            // existing member against today's catalog. Unpaid orders issue no pass.
            var context = await _ticketPlanEligibilityService.GetEligibilityContextAsync(
                unpaidOrder.BuyerId,
                ct);
            if (context is null
                || !await _ticketPlanEligibilityService.CanPurchaseAsync(context, planResult, ct))
            {
                var isRenewalPlan = planResult.EligibilityRuleCodes.Contains(
                    "RENEWAL",
                    StringComparer.OrdinalIgnoreCase);
                if (context is not null && isRenewalPlan)
                {
                    var evaluation = await _renewalEligibilityService.EvaluateAsync(
                        unpaidOrder.BuyerId,
                        planResult.FamilyCode,
                        _clock.Now(),
                        acquireLock: false,
                        ct);
                    throw new TicketPurchaseRejectedException(
                        evaluation.FailureCode ?? "TICKET_PLAN_NOT_AVAILABLE",
                        evaluation.FailureMessage ?? "付款時續約資格已失效");
                }

                throw new TicketPurchaseRejectedException(
                    "TICKET_PLAN_NOT_AVAILABLE",
                    "付款時票券購買資格已失效");
            }

            int? renewalSourcePassSn = null;
            var isRenewal = planResult.EligibilityRuleCodes.Contains(
                "RENEWAL",
                StringComparer.OrdinalIgnoreCase);
            var paidAt = _clock.Now();
            if (isRenewal)
            {
                var evaluation = await _renewalEligibilityService.EvaluateAsync(
                    unpaidOrder.BuyerId,
                    planResult.FamilyCode,
                    paidAt,
                    acquireLock: true,
                    ct);
                renewalSourcePassSn = evaluation.Eligibility?.SourcePassSn
                    ?? throw new TicketPurchaseRejectedException(
                        evaluation.FailureCode ?? "TICKET_PLAN_NOT_AVAILABLE",
                        evaluation.FailureMessage ?? "付款時續約資格已失效");
            }

            await _orderRepository.MarkPaidAsync(
                unpaidOrder.OrderSn,
                unpaidOrder.OrderItemSn,
                paidAt,
                "Cash",
                operatorId,
                ct);

            var passes = Enumerable.Range(0, unpaidOrder.Quantity)
                .Select(_ => TicketPass.IssueQueued(
                    id: $"PASS-{Guid.NewGuid():N}",
                    ownerId: unpaidOrder.BuyerId,
                    orderId: unpaidOrder.OrderId,
                    orderItemId: unpaidOrder.OrderItemId,
                    plan: plan,
                    renewedFromPassSn: renewalSourcePassSn,
                    paidAt: paidAt))
                .ToList();
            await _ticketPassRepository.AddRangeAsync(
                passes,
                new OrderPersistenceResult
                {
                    OrderSn = unpaidOrder.OrderSn,
                    OrderId = unpaidOrder.OrderId,
                    Items =
                    [
                        new OrderItemPersistenceResult
                        {
                            ClientItemId = unpaidOrder.OrderItemId,
                            OrderItemSn = unpaidOrder.OrderItemSn,
                            OrderItemId = unpaidOrder.OrderItemId
                        }
                    ]
                },
                ct);

            var snapshot = await _ticketPassRepository.ReconcileCurrentAsync(
                unpaidOrder.BuyerId,
                DateOnly.FromDateTime(paidAt),
                paidAt,
                operatorId,
                ct);
            var updated = snapshot is null
                ? await _studentProfileRepository.ClearCurrentTicketAsync(
                    unpaidOrder.BuyerId,
                    paidAt,
                    ct)
                : await _studentProfileRepository.UpdateCurrentTicketAsync(
                    unpaidOrder.BuyerId,
                    snapshot,
                    ct);
            if (!updated)
            {
                throw new InvalidOperationException("更新會員票券快照失敗");
            }
        }

        private static void ValidateQuantity(string planId, int quantity)
        {
            if (quantity < 1)
            {
                throw new InvalidOperationException("購買數量至少為 1");
            }

            if (!planId.Equals("SINGLE", StringComparison.OrdinalIgnoreCase) && quantity != 1)
            {
                throw new InvalidOperationException("目前只有單次票支援購買多張");
            }

            if (planId.Equals("SINGLE", StringComparison.OrdinalIgnoreCase)
                && quantity > SingleMaxQuantity)
            {
                throw new InvalidOperationException($"單次票一次最多購買 {SingleMaxQuantity} 張");
            }
        }
    }
}
