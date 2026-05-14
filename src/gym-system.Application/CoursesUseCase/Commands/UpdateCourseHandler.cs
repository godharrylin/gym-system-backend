using gym_system.Domain.Repositories;
using gym_system.Domain.Entities.Courses;

namespace gym_system.Application.CoursesUseCase.Commands
{
    //  這是更新預設課程
    public class UpdateCourseHandler
    {
        private IUnitOfWork _unitOfWork;
        private IUserRepository _userRepository;
        private IUserRoleRepository _roleRepository;
        private ICourseRepository _courseRepository;
        public UpdateCourseHandler(IUnitOfWork unitOfWork, IUserRepository userRepository,
            IUserRoleRepository userRoleRepository, ICourseRepository courserRepository) 
        {
            _unitOfWork = unitOfWork;
            _userRepository = userRepository;
            _roleRepository = userRoleRepository;
            _courseRepository = courserRepository;
        }

        public async Task<bool> Handle(string id, UpdateCourseCommand command, CancellationToken ct)
        {
            // 1. 基本輸入檢查 (Optional, if using FluentValidation)
            if (string.IsNullOrEmpty(id)) throw new InvalidOperationException("課程ID 必填");
       
            // 2. 讀取課程實體
            var course = await _courseRepository.GetByIdAsync(id, ct);
            if (course is null) throw new InvalidOperationException("找不到課程");
       
            // 3. 檢查老師 (相依性檢查)
            // 這裡維持原樣，因為需要透過 Repository 查另一個聚合(資料表)
            var user = await _userRepository.FindUserByIdAsync(command.InstructorId, ct);
            if (user is null || !user.IsActived) throw new InvalidOperationException("找不到使用者或已停用");
       
            var role = await _roleRepository.GetUserRoleAsync(command.InstructorId, Domain.Enums.UserRoleCode.Instructor, ct);
            if (role is null || !role.IsActive) throw new InvalidOperationException("該使用者不是老師或已停止授課");

            //  更新切成兩段，因為可能更新老師會需要觸發領域事件，例如更新老師後發信通知，或是改完後檢查模板排課是否有衝堂
            course.UpdateBasicInfo(command.Name, command.LabelColor, command.IsFree);
            course.Reschedule(command.Duration);
            course.UpdateInstructor(role.UserId, user.Name);

            bool result = false;
            await _unitOfWork.BeginAsync(ct);
            try
            {
                result = await _courseRepository.UpdateAsync(course, ct);
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
