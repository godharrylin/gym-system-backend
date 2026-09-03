using gym_system.Application.TicketPlansUseCase.Queries;
using gym_system.Domain.Entities.Orders;
using gym_system.Domain.Entities.Tickets;
using gym_system.Domain.Enums;
using gym_system.Domain.Exceptions;
using gym_system.Domain.Repositories;

namespace gym_system.Application.OrdersUseCase.Services
{
    public sealed class TicketPurchaseService
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

        public TicketPurchaseService(
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

        public async Task<TicketPurchaseResult> PurchaseAsync(
            TicketPurchaseRequest request,
            CancellationToken ct = default)
        {
            var beneficiaryIds = ValidateAndNormalize(request);
            var buyer = await _userRepository.FindUserByIdAsync(request.BuyerId.Trim(), ct)
                ?? throw new KeyNotFoundException("學生不存在");
            if (!buyer.IsActive)
            {
                throw new InvalidOperationException("學生不是有效學生");
            }

            var plan = await _ticketPlanRepository.GetActiveByIdAsync(request.TicketPlanKindCode.Trim(), ct)
                ?? throw new KeyNotFoundException("票券方案不存在或未上架");
            ValidateQuantity(plan.Id, request.Quantity);

            var activePlanResult = await GetActivePlanResultAsync(plan.Id, ct)
                ?? throw new KeyNotFoundException("票券方案不存在或未上架");

            if (beneficiaryIds.Count != 1)
            {
                throw new TicketPurchaseRejectedException(
                    "FAMILY_PURCHASE_NOT_AVAILABLE",
                    "家庭購票功能尚未開放");
            }

            int? renewalSourcePassSn = null;
            foreach (var studentId in beneficiaryIds)
            {
                await EnsureActiveStudentAsync(studentId, ct);
                if (await _studentProfileRepository.FindByUserIdAsync(studentId, ct) is null)
                {
                    throw new InvalidOperationException("學生沒有 Profile");
                }

                if (request.PaymentStatus == PaymentState.Paid)
                {
                    await _ticketPassRepository.LockOwnerAsync(studentId, ct);
                }

                var context = await _ticketPlanEligibilityService.GetEligibilityContextAsync(studentId, ct);
                if (context is null)
                {
                    throw new TicketPurchaseRejectedException(
                        "TICKET_PLAN_NOT_AVAILABLE",
                        "學生不符合此票券購買資格");
                }

                var isRenewal = activePlanResult.EligibilityRuleCodes.Contains(
                    "RENEWAL",
                    StringComparer.OrdinalIgnoreCase);
                if (!await _ticketPlanEligibilityService.CanPurchaseAsync(context, activePlanResult, ct))
                {
                    if (isRenewal)
                    {
                        var evaluation = await _renewalEligibilityService.EvaluateAsync(
                            studentId,
                            activePlanResult.FamilyCode,
                            _clock.Now(),
                            acquireLock: false,
                            ct);
                        throw new TicketPurchaseRejectedException(
                            evaluation.FailureCode ?? "TICKET_PLAN_NOT_AVAILABLE",
                            evaluation.FailureMessage ?? "學生不符合此票券購買資格");
                    }

                    throw new TicketPurchaseRejectedException(
                        "TICKET_PLAN_NOT_AVAILABLE",
                        "學生不符合此票券購買資格");
                }

                if (isRenewal && request.PaymentStatus == PaymentState.Paid)
                {
                    var evaluation = await _renewalEligibilityService.EvaluateAsync(
                        studentId,
                        activePlanResult.FamilyCode,
                        _clock.Now(),
                        acquireLock: true,
                        ct);
                    if (evaluation.Eligibility is null)
                    {
                        throw new TicketPurchaseRejectedException(
                            evaluation.FailureCode ?? "TICKET_PLAN_NOT_AVAILABLE",
                            evaluation.FailureMessage ?? "續約資格已失效，請重新整理方案");
                    }

                    renewalSourcePassSn = evaluation.Eligibility.SourcePassSn;
                }
            }

            var buyAt = _clock.Now();
            var orderPaymentState = request.PaymentStatus == PaymentState.Paid
                ? OrderOverallPaymentState.Paid
                : OrderOverallPaymentState.UnPaid;
            var itemPaymentState = request.PaymentStatus == PaymentState.Paid
                ? OrderItemPaymentState.Paid
                : OrderItemPaymentState.UnPaid;
            var actualUnitPrice = plan.Price;
            var totalAmountPerPerson = plan.Price * request.Quantity;
            var actualAmountPerPerson = actualUnitPrice * request.Quantity;
            var orderTotalAmount = totalAmountPerPerson * beneficiaryIds.Count;
            var orderActualAmount = actualAmountPerPerson * beneficiaryIds.Count;
            var operatorId = string.IsNullOrWhiteSpace(request.OperatorId)
                ? "ADMIN_PLACEHOLDER"
                : request.OperatorId.Trim();
            var orderClientId = $"ORD-{Guid.NewGuid():N}";
            var order = Order.Create(
                id: orderClientId,
                buyerId: buyer.Id,
                buyerName: buyer.Name,
                totalAmount: orderTotalAmount,
                actualAmount: orderActualAmount,
                paymentState: orderPaymentState,
                operatorId: operatorId,
                buyAt: buyAt);

            var passes = new List<TicketPass>(beneficiaryIds.Count * request.Quantity);
            foreach (var studentId in beneficiaryIds)
            {
                var item = OrderItem.CreateTicketItem(
                    id: $"ITM-{Guid.NewGuid():N}",
                    orderId: orderClientId,
                    ticketPlanKindId: plan.Id,
                    ticketPlanKindName: plan.Name,
                    unitPrice: plan.Price,
                    totalAmount: totalAmountPerPerson,
                    actualAmount: actualAmountPerPerson,
                    quantityUnit: UnitType.Pieces,
                    quantity: request.Quantity,
                    bonusQuantity: 0,
                    bonusUnit: null,
                    discountType: null,
                    discountRate: null,
                    paymentMethod: OrderItemPaymentMethod.Cash,
                    paymentState: itemPaymentState,
                    buyAt: buyAt);
                order.AddItem(item);

                if (request.PaymentStatus != PaymentState.Paid)
                {
                    continue;
                }

                for (var index = 0; index < request.Quantity; index++)
                {
                    passes.Add(TicketPass.IssueQueued(
                        id: $"PASS-{Guid.NewGuid():N}",
                        ownerId: studentId,
                        orderId: orderClientId,
                        orderItemId: item.Id,
                        plan: plan,
                        renewedFromPassSn: renewalSourcePassSn,
                        paidAt: buyAt));
                }
            }

            var orderPersistence = await _orderRepository.AddAsync(order, ct);
            if (passes.Count > 0)
            {
                await _ticketPassRepository.AddRangeAsync(passes, orderPersistence, ct);

                foreach (var studentId in beneficiaryIds.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                {
                    var snapshot = await _ticketPassRepository.ReconcileCurrentAsync(
                        studentId,
                        _clock.Today(),
                        buyAt,
                        operatorId,
                        ct);
                    var updated = snapshot is null
                        ? await _studentProfileRepository.ClearCurrentTicketAsync(studentId, buyAt, ct)
                        : await _studentProfileRepository.UpdateCurrentTicketAsync(studentId, snapshot, ct);
                    if (!updated)
                    {
                        throw new InvalidOperationException("更新會員票券快照失敗");
                    }
                }
            }

            return new TicketPurchaseResult
            {
                OrderId = orderPersistence.OrderId,
                TotalAmount = order.TotalAmount,
                ActualAmount = order.ActualAmount
            };
        }

        private async Task<TicketPlanResult?> GetActivePlanResultAsync(string planCode, CancellationToken ct)
        {
            var activePlans = await _ticketPlanCatalogQueryService.GetActiveTicketPlansAsync(ct);
            return activePlans.FirstOrDefault(x => x.Id.Equals(planCode, StringComparison.OrdinalIgnoreCase));
        }

        private async Task EnsureActiveStudentAsync(string studentId, CancellationToken ct)
        {
            var user = await _userRepository.FindUserByIdAsync(studentId, ct)
                ?? throw new KeyNotFoundException("學生不存在");
            if (!user.IsActive)
            {
                throw new InvalidOperationException("學生不是有效學生");
            }

            var role = await _userRoleRepository.GetUserRoleAsync(studentId, UserRoleCode.Student, ct);
            if (role is null || !role.IsActive)
            {
                throw new InvalidOperationException("學生不是有效學生");
            }
        }

        private static List<string> ValidateAndNormalize(TicketPurchaseRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.BuyerId))
            {
                throw new InvalidOperationException("購買者必填");
            }

            if (string.IsNullOrWhiteSpace(request.TicketPlanKindCode))
            {
                throw new InvalidOperationException("票券方案必填");
            }

            if (request.BeneficiaryStudentIds.Count == 0)
            {
                throw new InvalidOperationException("至少需要一位受益學生");
            }

            var beneficiaryIds = request.BeneficiaryStudentIds
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .ToList();
            if (beneficiaryIds.Count != request.BeneficiaryStudentIds.Count)
            {
                throw new InvalidOperationException("受益學生不可為空");
            }

            if (beneficiaryIds.Count != beneficiaryIds.Distinct(StringComparer.OrdinalIgnoreCase).Count())
            {
                throw new InvalidOperationException("受益學生清單不可重複");
            }

            if (!beneficiaryIds.Contains(request.BuyerId.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("購買者必須包含在受益學生清單內");
            }

            if (!Enum.IsDefined(request.PaymentStatus))
            {
                throw new InvalidOperationException("付款狀態不支援");
            }

            if (!string.IsNullOrWhiteSpace(request.PaymentMethod)
                && !request.PaymentMethod.Equals("Cash", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("付款方式目前只支援 Cash");
            }

            return beneficiaryIds;
        }

        private static void ValidateQuantity(string planCode, int quantity)
        {
            if (quantity < 1)
            {
                throw new InvalidOperationException("購買數量至少為 1");
            }

            if (!planCode.Equals("SINGLE", StringComparison.OrdinalIgnoreCase) && quantity != 1)
            {
                throw new InvalidOperationException("目前只有單次票支援購買多張");
            }

            if (planCode.Equals("SINGLE", StringComparison.OrdinalIgnoreCase) && quantity > SingleMaxQuantity)
            {
                throw new InvalidOperationException($"單次票一次最多購買 {SingleMaxQuantity} 張");
            }
        }
    }

    public sealed class TicketPurchaseRequest
    {
        public string BuyerId { get; init; } = string.Empty;
        public IReadOnlyList<string> BeneficiaryStudentIds { get; init; } = Array.Empty<string>();
        public string TicketPlanKindCode { get; init; } = string.Empty;
        public int Quantity { get; init; } = 1;
        public PaymentState PaymentStatus { get; init; }
        public string? PaymentMethod { get; init; }
        public string OperatorId { get; init; } = "ADMIN_PLACEHOLDER";
    }

    public sealed class TicketPurchaseResult
    {
        public string OrderId { get; init; } = string.Empty;
        public decimal TotalAmount { get; init; }
        public decimal ActualAmount { get; init; }
    }
}
