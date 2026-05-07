using gym_system.Domain.Entities.Users;
using gym_system.Domain.Repositories;
using gym_system.Domain.Enums;

namespace gym_system.Application.InstructorUseCase.Command.CreateInstructor
{
    public sealed class CreateInstructorHandler
    {
        private readonly IUserRepository _userRepository;
        private readonly IUserRoleRepository _roleRepository;
        private readonly IClock _clock;
        private readonly IUnitOfWork _unitOfWork;
        public CreateInstructorHandler(
                    IUserRepository userRepository,
                    IUserRoleRepository roleRepository,
                    IClock clock,
                    IUnitOfWork unitOfWork)
        {
            _userRepository = userRepository;
            _roleRepository = roleRepository;
            _clock = clock;
            _unitOfWork = unitOfWork;
        }

        public async Task<bool> Handle(CreateInstructorCommand command, CancellationToken ct)
        {
            var phone = command.Phone.Trim();
            
            var createDay = _clock.Now();

            await _unitOfWork.BeginAsync(ct);
            try
            {
                var user = await _userRepository.FindUserByPhone(phone, ct);
                string userId;
                //  如果輸入的電話號碼還沒被註冊，先註冊user
                if (user == null)
                {
                    user = User.Register(command.Name.Trim(), phone, phone);
                    //  建立 User
                    userId = await _userRepository.AddAsync(user, ct);

                    if (string.IsNullOrWhiteSpace(userId))
                    {
                        throw new InvalidOperationException("[CreateInstructor Handle]: User Create Fail");
                    }
                }
                else
                {
                    userId = user.Id;
                }

                var role = await _roleRepository.GetUserRoleAsync(userId, UserRoleCode.Instructor, ct);
                var result = false;
                if (role == null)
                {
                    // 情境 1：完全沒有角色紀錄 -> 新增
                    role = UserRole.Assign(userId, UserRoleCode.Instructor, createDay, true);
                    result = await _roleRepository.AddRoleAsync(role, ct);
                }
                else if(role.IsActive == false)
                {
                    // 情境 2：有紀錄但被停用了 -> 重新啟用
                    result = await _roleRepository.ReactiveRole(userId, role.RoleCode, ct);
                }
                else
                {
                    // 情境 3：已經是啟用中指導老師了 -> 直接視為成功 (冪等性設計)
                    result = true;
                }

                if (result)
                {
                    await _unitOfWork.CommitAsync(ct);
                }
                else
                {
                    await _unitOfWork.RollbackAsync(ct);
                }

                return result;

            }
            catch
            {
                await _unitOfWork.RollbackAsync(ct);
                throw;
            }
        }
    }
}
