using gym_system.Domain.Repositories;
using gym_system.Domain.Entities.Courses;

namespace gym_system.Application.CoursesUseCase.Commands
{
    public class CreateCourseHandler
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IUserRepository _userRepository;
        private readonly IUserRoleRepository _roleRepository;
        private readonly ICourseRepository _courseRepository;

        public CreateCourseHandler(
            IUnitOfWork unitOfWork,
            IUserRepository userRepository,
            IUserRoleRepository userRoleRepository,
            ICourseRepository courseRepository)
        {
            _unitOfWork = unitOfWork;
            _userRepository = userRepository;
            _roleRepository = userRoleRepository;
            _courseRepository = courseRepository;
        }

        public async Task<bool> Handle(CreateCourseCommand command, CancellationToken ct)
        {
            // 1. 檢查老師身分
            var user = await _userRepository.FindUserByIdAsync(command.InstructorId, ct);
            if (user is null || !user.IsActive)
                throw new InvalidOperationException("找不到使用者或已停用");

            var role = await _roleRepository.GetUserRoleAsync(command.InstructorId, Domain.Enums.UserRoleCode.Instructor, ct);
            if (role is null || !role.IsActive)
                throw new InvalidOperationException("該使用者不是老師或已停止授課");

            // 2. 透過 Domain 建立實體
            var course = Course.Create(
                name: command.Name,
                labelColor: command.LabelColor,
                duration: command.Duration,
                isFree: command.IsFree,
                instructorId: command.InstructorId,
                instructorName: user.Name,
                type: command.Type
            );

            // 3. 持久化
            bool result = false;
            await _unitOfWork.BeginAsync(ct);
            try
            {
                result = await _courseRepository.AddAsync(course, ct);
                await _unitOfWork.CommitAsync(ct);
            }
            catch
            {
                await _unitOfWork.RollbackAsync(ct);
                throw;
            }

            return result;
        }
    }
}
