using gym_system.Domain.Enums;
using gym_system.Domain.Repositories;

namespace gym_system.Application.InstructorUseCase.Command.UpdateInstructor
{
    public sealed class UpdateInstructorHandler
    {
        private readonly IUserRepository _userRepository;
        private readonly IUserRoleRepository _roleRepository;
        private readonly IUnitOfWork _unitOfWork;

        public UpdateInstructorHandler(
            IUserRepository userRepository,
            IUserRoleRepository roleRepository,
            IUnitOfWork unitOfWork)
        {
            _userRepository = userRepository;
            _roleRepository = roleRepository;
            _unitOfWork = unitOfWork;
        }

        public async Task<bool> Handle(UpdateInstructorCommand command, CancellationToken ct)
        {
            var userId = command.UserId.Trim();
            var name = command.Name.Trim();
            var phone = command.Phone.Trim();

            if (string.IsNullOrWhiteSpace(userId)) throw new InvalidOperationException("老師 ID 必填");
            if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("老師姓名必填");
            if (string.IsNullOrWhiteSpace(phone)) throw new InvalidOperationException("老師電話必填");

            await _unitOfWork.BeginAsync(ct);
            try
            {
                var user = await _userRepository.FindUserByIdAsync(userId, ct);
                if (user is null)
                {
                    throw new InvalidOperationException("老師不存在");
                }

                var existsPhoneInOtherUser = await _userRepository.ExistsPhoneForOtherUserAsync(userId, phone, ct);
                if (existsPhoneInOtherUser)
                {
                    throw new InvalidOperationException("手機號碼已被其他使用者註冊");
                }

                var updatedProfile = await _userRepository.UpdateBasicProfileAsync(userId, name, phone, ct);
                if (!updatedProfile)
                {
                    throw new InvalidOperationException("老師資料更新失敗");
                }

                var role = await _roleRepository.GetUserRoleAsync(userId, UserRoleCode.Instructor, ct);
                if (role is null)
                {
                    throw new InvalidOperationException("找不到老師角色");
                }

                var updatedRole = await _roleRepository.SetRoleActiveAsync(userId, UserRoleCode.Instructor, command.IsEmployed, ct);

                if (updatedRole)
                {
                    await _unitOfWork.CommitAsync(ct);
                }
                else
                {
                    await _unitOfWork.RollbackAsync(ct);
                }

                return updatedRole;
            }
            catch
            {
                await _unitOfWork.RollbackAsync(ct);
                throw;
            }
        }
    }
}
