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
                //  如果輸入的電話號碼還沒被註冊，先註冊user
                if (user == null)
                {
                    var id = await _userRepository.GenerateIdsAsync(1, ct);
                    user = User.Create(id[0], command.Name, phone, phone);
                    //  建立 User
                    var status = await _userRepository.AddRangeAsync(new[] { user }, ct);

                    if (status == false)
                    {
                        throw new InvalidOperationException("[CreateInstructor Handle]: User Create Fail");
                    }
                }

                var userId = user.Id;

                var role = await _roleRepository.CheckRoleAsync(userId, UserRoleCode.Instructor);
                var result = false;
                if (role == null)
                {
                    // 情境 1：完全沒有角色紀錄 -> 新增
                    role = UserRole.Assign(userId, UserRoleCode.Instructor, createDay, true);
                    result = await _roleRepository.AddRoleAsync(role, UserRoleCode.Instructor, ct);
                }
                else if(role.IsActived == false)
                {
                    // 情境 2：有紀錄但被停用了 -> 重新啟用
                    result = await _roleRepository.ReactiveRole(userId, UserRoleCode.Instructor, ct);
                }
                else
                {
                    // 情境 3：已經是活躍的教練了 -> 直接視為成功 (冪等性設計)
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
