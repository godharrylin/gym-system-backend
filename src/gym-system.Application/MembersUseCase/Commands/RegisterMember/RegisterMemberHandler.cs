using gym_system.Domain.Entities.Members;
using gym_system.Domain.Entities.Orders;
using gym_system.Domain.Entities.Tickets;
using gym_system.Domain.Entities.Users;
using gym_system.Domain.Enums;
using gym_system.Domain.Repositories;

namespace gym_system.Application.MembersUseCase.Commands.RegisterMember
{
    public sealed class RegisterMemberHandler
    {
        private readonly IUserRepository _userRepository;
        private readonly IUserRoleRepository _userRoleRepository;
        private readonly IStudentProfileRepository _studentProfileRepository;
        private readonly ITicketPlanRepository _ticketPlanRepository;
        private readonly IOrderRepository _orderRepository;
        private readonly ITicketPassRepository _ticketPassRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IClock _clock;

        public RegisterMemberHandler(
            IUserRepository userRepository,
            IUserRoleRepository userRoleRepository,
            IStudentProfileRepository studentProfileRepository,
            ITicketPlanRepository ticketPlanRepository,
            IOrderRepository orderRepository,
            ITicketPassRepository ticketPassRepository,
            IUnitOfWork unitOfWork,
            IClock clock)
        {
            _userRepository = userRepository;
            _userRoleRepository = userRoleRepository;
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
                var existingPhones = await _userRepository.GetExistingPhonesAsync(phones, ct);
                if (existingPhones.Count > 0)
                {
                    throw new InvalidOperationException("手機號碼已被註冊");
                }

                var registeredUserIds = new List<string>(command.Members.Count);
                foreach (var input in command.Members)
                {
                    var user = User.Register(
                        input.Name.Trim(),
                        input.Phone.Trim(),
                        input.Phone.Trim());
                    var userId = await _userRepository.AddAsync(user, ct);

                    var studentRole = UserRole.Assign(
                        userId,
                        UserRoleCode.Student,
                        _clock.Now(),
                        true);
                    if (!await _userRoleRepository.AddRoleAsync(studentRole, ct))
                    {
                        throw new InvalidOperationException("建立會員角色失敗");
                    }

                    await _studentProfileRepository.AddAsync(StudentProfile.Create(userId), ct);
                    registeredUserIds.Add(userId);
                }

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
                    var order_total_amount = plan.Price * registeredUserIds.Count;

                    //  1. 兩人以上一起註冊直接給95折，先算個人
                    var order_actual_amount_person = registeredUserIds.Count >= 2
                        ? decimal.Round(plan.Price * 0.95m, 0, MidpointRounding.AwayFromZero)
                        : plan.Price;

                    //  2. 加總人數後便訂單實際價格
                    var order_actual_amount = order_actual_amount_person * registeredUserIds.Count;

                    #endregion
                    var createDay = _clock.Now();
                    //  建立訂單資訊
                    order = Order.Create(
                        id: $"ORD-{Guid.NewGuid():N}",
                        buyerId: registeredUserIds[0],
                        totalAmount: order_total_amount,
                        actualAmount: order_actual_amount,
                        paymentState: ticketPurchase.PaymentStatus == PaymentState.Paid
                            ? OrderOverallPaymentState.Paid
                            : OrderOverallPaymentState.UnPaid,
                        operatorId: command.OperatorId,
                        buyAt: createDay);

                    //  建立訂單票券明細，及學生票券資訊
                    var passes = new List<TicketPass>(registeredUserIds.Count);
                    foreach (var userId in registeredUserIds)
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
                            ownerId: userId,
                            orderId: order.Id,
                            orderItemId: item.Id,
                            plan: plan,
                            activationDate: ticketPurchase.ActivationDate,
                            paymentState: ticketPurchase.PaymentStatus,
                            today: DateOnly.FromDateTime(createDay));

                        passes.Add(pass);
                        if (!await _studentProfileRepository.UpdateCurrentTicketAsync(userId, pass.ToSnapshot(), ct))
                        {
                            throw new InvalidOperationException("更新會員票券快照失敗");
                        }
                    }

                    await _orderRepository.AddAsync(order, ct);
                    await _ticketPassRepository.AddRangeAsync(passes, ct);
                }

                await _unitOfWork.CommitAsync(ct);

                return new RegisterMembersResult
                {
                    MemberIds = registeredUserIds,
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
