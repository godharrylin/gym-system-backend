using gym_system.Domain.Enums;
using gym_system.Domain.Repositories;

namespace gym_system.Application.InstructorsUseCase.Command.UpdateInstructor
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

            if (string.IsNullOrWhiteSpace(userId)) throw new InvalidOperationException("老師 ID 必填");
            if (command.Name is not null && string.IsNullOrWhiteSpace(command.Name)) throw new InvalidOperationException("老師姓名不可為空白");
            if (command.Phone is not null && string.IsNullOrWhiteSpace(command.Phone)) throw new InvalidOperationException("老師電話不可為空白");
            if (command.Name is null && command.Phone is null && command.IsEmployed is null)
            {
                throw new InvalidOperationException("至少提供一個可更新欄位");
            }

            await _unitOfWork.BeginAsync(ct);
            try
            {
                var user = await _userRepository.FindUserByIdAsync(userId, ct);
                if (user is null)
                {
                    throw new InvalidOperationException("老師不存在");
                }

                var nextName = command.Name is null ? user.Name : command.Name.Trim();
                var nextPhone = command.Phone is null ? user.Phone : command.Phone.Trim();

                if (command.Phone is not null)
                {
                    var existsPhoneInOtherUser = await _userRepository.ExistsPhoneForOtherUserAsync(userId, nextPhone, ct);
                    if (existsPhoneInOtherUser)
                    {
                        throw new InvalidOperationException("手機號碼已被其他使用者註冊");
                    }
                }

                var updatedProfile = await _userRepository.UpdateBasicProfileAsync(userId, nextName, nextPhone, ct);
                if (!updatedProfile)
                {
                    throw new InvalidOperationException("老師資料更新失敗");
                }

                var role = await _roleRepository.GetUserRoleAsync(userId, UserRoleCode.Instructor, ct);
                if (role is null)
                {
                    throw new InvalidOperationException("找不到老師角色");
                }

                var updatedRole = true;
                if (command.IsEmployed.HasValue)
                {
                    updatedRole = await _roleRepository.SetRoleActiveAsync(userId, UserRoleCode.Instructor, command.IsEmployed.Value, ct);
                }

                if (updatedProfile && updatedRole)
                {
                    await _unitOfWork.CommitAsync(ct);
                }
                else
                {
                    await _unitOfWork.RollbackAsync(ct);
                }

                return updatedProfile && updatedRole;
            }
            catch
            {
                await _unitOfWork.RollbackAsync(ct);
                throw;
            }
        }
    }
}
