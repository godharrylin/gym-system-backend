using gym_system.Application.OrdersUseCase.Services;
using gym_system.Application.TicketPlansUseCase.Queries;
using gym_system.Domain.Entities.Members;
using gym_system.Domain.Entities.Users;
using gym_system.Domain.Enums;
using gym_system.Domain.Exceptions;
using gym_system.Domain.Repositories;

namespace gym_system.Application.MembersUseCase.Commands.RegisterMember
{
    public sealed class RegisterMemberHandler
    {
        private readonly IUserRepository _userRepository;
        private readonly IUserRoleRepository _userRoleRepository;
        private readonly IStudentProfileRepository _studentProfileRepository;
        private readonly TicketPurchaseService _ticketPurchaseService;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IClock _clock;

        public RegisterMemberHandler(
            IUserRepository userRepository,
            IUserRoleRepository userRoleRepository,
            IStudentProfileRepository studentProfileRepository,
            TicketPurchaseService ticketPurchaseService,
            IUnitOfWork unitOfWork,
            IClock clock)
        {
            _userRepository = userRepository;
            _userRoleRepository = userRoleRepository;
            _studentProfileRepository = studentProfileRepository;
            _ticketPurchaseService = ticketPurchaseService;
            _unitOfWork = unitOfWork;
            _clock = clock;
        }

        public async Task<RegisterMembersResult> Handle(
            RegisterMembersCommand command,
            CancellationToken ct = default)
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
                    throw new MemberRegistrationRejectedException(
                        "MEMBER_PHONE_ALREADY_REGISTERED",
                        "手機號碼已被註冊");
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

                TicketPurchaseResult? purchase = null;
                if (command.TicketPurchase is not null)
                {
                    purchase = await _ticketPurchaseService.PurchaseAsync(
                        new TicketPurchaseRequest
                        {
                            BuyerId = registeredUserIds[0],
                            BeneficiaryStudentIds = registeredUserIds,
                            TicketPlanKindCode = command.TicketPurchase.TicketPlanKindId,
                            Quantity = command.TicketPurchase.Quantity,
                            PaymentStatus = command.TicketPurchase.PaymentStatus,
                            PaymentMethod = "Cash",
                            OperatorId = command.OperatorId,
                            EligibilityContextKind = TicketPlanEligibilityContextKind.Registration
                        },
                        ct);
                }

                await _unitOfWork.CommitAsync(ct);
                return new RegisterMembersResult
                {
                    MemberIds = registeredUserIds,
                    OrderId = purchase?.OrderId,
                    TotalAmount = purchase?.TotalAmount,
                    ActualAmount = purchase?.ActualAmount
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
                throw new MemberRegistrationRejectedException(
                    "MEMBERS_REQUIRED",
                    "至少需要一位會員");
            }

            foreach (var member in command.Members)
            {
                if (string.IsNullOrWhiteSpace(member.Name))
                {
                    throw new MemberRegistrationRejectedException(
                        "MEMBER_NAME_REQUIRED",
                        "姓名必填");
                }

                if (string.IsNullOrWhiteSpace(member.Phone))
                {
                    throw new MemberRegistrationRejectedException(
                        "MEMBER_PHONE_REQUIRED",
                        "手機必填");
                }
            }

            if (command.TicketPurchase is not null
                && string.IsNullOrWhiteSpace(command.TicketPurchase.TicketPlanKindId))
            {
                throw new MemberRegistrationRejectedException(
                    "TICKET_PLAN_REQUIRED",
                    "票券方案必填");
            }
        }

        private static void EnsureNoDuplicateInRequest(IReadOnlyList<string> phones)
        {
            if (phones.Count != phones.Distinct(StringComparer.Ordinal).Count())
            {
                throw new MemberRegistrationRejectedException(
                    "DUPLICATE_PHONE_IN_REQUEST",
                    "請求內有重複手機號碼");
            }
        }
    }
}
