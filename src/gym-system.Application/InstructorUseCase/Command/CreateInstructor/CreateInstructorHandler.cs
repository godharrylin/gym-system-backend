using gym_system.Application.Service;
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
            var user = await _userRepository.FindUserByPhone(phone, ct);
            var createDay = _clock.Now();

            await _unitOfWork.BeginAsync(ct);
            try
            {
                if (user == null)
                {
                    var id = await _userRepository.GenerateIdsAsync(1, ct);
                    user = User.Create(id[0], command.Name, phone, phone);
                    //  建立 User
                    var status = await _userRepository.AddRangeAsync(new[] { user }, ct);

                    if (status == false)
                    {
                        throw new InvalidOperationException("User Create Fail");
                    }
                }

                var userId = user.Id;
                //  判斷是否有Role，如果沒有就建立
                var hasRole = await _roleRepository.HasRoleAsync(userId, UserRoleCode.Instructor);
                if (!hasRole)
                {
                    var role = UserRole.Assign(userId, UserRoleCode.Instructor, createDay, true);
                    var result = await _roleRepository.AddRoleAsync(role);
                    await _unitOfWork.CommitAsync(ct);
                    return result;
                }
                else
                {
                    return true;
                }
            }
            catch
            {
                await _unitOfWork.RollbackAsync(ct);
                throw;
            }
        }
    }
}
