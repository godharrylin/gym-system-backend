using gym_system.Domain.Entities.Members;
using gym_system.Domain.Entities.Orders;
using gym_system.Domain.Entities.Tickets;
using gym_system.Domain.Enums;
using gym_system.Domain.Repositories;

namespace gym_system.Application.MembersUseCase.Commands.RegisterMember
{
    public sealed class RegisterMemberHandler
    {
        private readonly IMemberRepository _memberRepository;
        private readonly IStudentProfileRepository _studentProfileRepository;
        private readonly ITicketPlanRepository _ticketPlanRepository;
        private readonly IOrderRepository _orderRepository;
        private readonly ITicketPassRepository _ticketPassRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IClock _clock;

        public RegisterMemberHandler(
            IMemberRepository memberRepository,
            IStudentProfileRepository studentProfileRepository,
            ITicketPlanRepository ticketPlanRepository,
            IOrderRepository orderRepository,
            ITicketPassRepository ticketPassRepository,
            IUnitOfWork unitOfWork,
            IClock clock)
        {
            _memberRepository = memberRepository;
            _studentProfileRepository = studentProfileRepository;
            _ticketPlanRepository = ticketPlanRepository;
            _orderRepository = orderRepository;
            _ticketPassRepository = ticketPassRepository;
            _unitOfWork = unitOfWork;
            _clock = clock;
        }

        public async Task<RegisterMembersResult> Handle(RegisterMembersCommand command, CancellationToken ct = default)
        {
            Validate(command);

            var phones = command.Members.Select(x => x.Phone.Trim()).ToList();
            EnsureNoDuplicateInRequest(phones);

            await _unitOfWork.BeginAsync(ct);
            try
            {
                if (await _memberRepository.AnyPhoneExistsAsync(phones, ct))
                {
                    throw new InvalidOperationException("手機號碼已被註冊");
                }

                //  一次性產生需要的會員ID
                var ids = await _memberRepository.GenerateIdsAsync(command.Members.Count, ct);
                var members = command.Members
                    .Select((item, index) => Member.Register(ids[index], item.Name.Trim(), item.Phone.Trim()))
                    .ToList();

                await _memberRepository.AddRangeAsync(members, ct);

                var profiles = members.Select(StudentProfile.CreateEmpty).ToList();
                await _studentProfileRepository.AddRangeAsync(profiles, ct);

                Order? order = null;

                //  如果有購買票券走這裡
                if (command.TicketPurchase is not null)
                {
                    var ticketPurchase = command.TicketPurchase;

                    //  從資料庫撈取票券資訊
                    var plan = await _ticketPlanRepository.GetActiveByIdAsync(ticketPurchase.TicketPlanKindId, ct)
                                ?? throw new InvalidOperationException("票券方案不存在或未上架");
                    //  決定購買量的單位
                    var qty_unit = plan.Type switch
                    {
                        TicketPlanType.Pack => UnitType.Credits,
                        TicketPlanType.MPass => UnitType.Days,
                        _ => throw new NotSupportedException($"Unknown type: {plan.Type}.")
                    };
                    //  購買預設天數
                    var qty = plan.Type switch
                    {
                        TicketPlanType.Pack => plan.DefaultCredit,
                        TicketPlanType.MPass => plan.DefaultExpireDays,
                        _ => throw new NotSupportedException($"Unknown type: {plan.Type}.")
                    };

                    #region 計算訂單和訂單明細價格 (先算個人再加總) 
                    var order_total_amount = plan.Price * members.Count;

                    //  1. 兩人以上一起註冊直接給95折，先算個人
                    var order_actual_amount_person = members.Count >= 2
                        ? decimal.Round(plan.Price * 0.95m, 0, MidpointRounding.AwayFromZero)
                        : plan.Price;

                    //  2. 加總人數後便訂單實際價格
                    var order_actual_amount = order_actual_amount_person * members.Count;

                    #endregion
                    var createDay = _clock.Now();
                    //  建立訂單資訊
                    order = Order.Create(
                        id: $"ORD-{Guid.NewGuid():N}",
                        buyerId: members[0].Id,
                        totalAmount: order_total_amount,
                        actualAmount: order_actual_amount,
                        paymentState: ticketPurchase.PaymentStatus == PaymentState.Paid
                            ? OrderOverallPaymentState.Paid
                            : OrderOverallPaymentState.UnPaid,
                        operatorId: command.OperatorId,
                        buyAt: createDay);

                    //  建立訂單票券明細，及學生票券資訊
                    var passes = new List<TicketPass>(members.Count);
                    foreach (var member in members)
                    {
                        //  票券訂單明細
                        var item = OrderItem.CreateTicketItem(
                            id: $"ITM-{Guid.NewGuid():N}",
                            orderId: order.Id,
                            ticketPlanKindId: plan.Id,
                            unitPrice: plan.Price,
                            totalAmount: plan.Price,
                            actualAmount: order_actual_amount_person,
                            quantityUnit: qty_unit,
                            quantity: qty,
                            bonusQuantity: 0, // 目前預設是0
                            paymentMethod: OrderItemPaymentMethod.Cash,
                            paymentState: ticketPurchase.PaymentStatus == PaymentState.Paid
                                ? OrderItemPaymentState.Paid
                                : OrderItemPaymentState.UnPaid,
                            buyAt: createDay);
                        order.AddItem(item);

                        //  建立票券資訊
                        var pass = TicketPass.Issue(
                            id: $"PASS-{Guid.NewGuid():N}",
                            ownerId: member.Id,
                            orderId: order.Id,
                            orderItemId: item.Id,
                            plan: plan,
                            activationDate: ticketPurchase.ActivationDate,
                            paymentState: ticketPurchase.PaymentStatus,
                            today: DateOnly.FromDateTime(createDay));

                        passes.Add(pass);
                        await _studentProfileRepository.UpdateCurrentTicketAsync(member.Id, pass.ToSnapshot(), ct);
                    }

                    await _orderRepository.AddAsync(order, ct);
                    await _ticketPassRepository.AddRangeAsync(passes, ct);
                }

                await _unitOfWork.CommitAsync(ct);

                return new RegisterMembersResult
                {
                    MemberIds = members.Select(x => x.Id).ToList(),
                    OrderId = order?.Id,
                    TotalAmount = order?.TotalAmount,
                    ActualAmount = order?.ActualAmount
                };
            }
            catch
            {
                await _unitOfWork.RollbackAsync(ct);
                throw;
            }
        }

        private static void Validate(RegisterMembersCommand command)
        {
            if (command.Members.Count == 0)
            {
                throw new InvalidOperationException("至少需要一位會員");
            }

            foreach (var member in command.Members)
            {
                if (string.IsNullOrWhiteSpace(member.Name))
                {
                    throw new InvalidOperationException("姓名必填");
                }

                if (string.IsNullOrWhiteSpace(member.Phone))
                {
                    throw new InvalidOperationException("手機必填");
                }
            }

            if (command.TicketPurchase is not null && string.IsNullOrWhiteSpace(command.TicketPurchase.TicketPlanKindId))
            {
                throw new InvalidOperationException("票券方案必填");
            }
        }

        private static void EnsureNoDuplicateInRequest(IReadOnlyList<string> phones)
        {
            if (phones.Count != phones.Distinct(StringComparer.Ordinal).Count())
            {
                throw new InvalidOperationException("請求內有重複手機號碼");
            }
        }
    }
}
