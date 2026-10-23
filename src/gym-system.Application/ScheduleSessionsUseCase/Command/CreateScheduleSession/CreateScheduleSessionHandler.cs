using gym_system.Domain.Entities.ScheduleSessions;
using gym_system.Domain.Repositories;
using System.Globalization;
using gym_system.Domain.Enums;

namespace gym_system.Application.ScheduleSessionsUseCase.Command.CreateScheduleSession
{
    public class CreateScheduleSessionHandler
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IScheduleSessionRepository _scheduleSessionRepository;
        private readonly ICourseRepository _courseRepository;
        private readonly IUserRepository _userRepository;
        private readonly IUserRoleRepository _roleRepository;

        public CreateScheduleSessionHandler(IUnitOfWork unitOfWork, 
                                            IScheduleSessionRepository scheduleSessionRepository,
                                            ICourseRepository courseRepository,
                                            IUserRepository userRepository,
                                            IUserRoleRepository roleRepository)
        {
            _unitOfWork = unitOfWork;
            _scheduleSessionRepository = scheduleSessionRepository;
            _courseRepository = courseRepository;
            _userRepository = userRepository;
            _roleRepository = roleRepository;
        }

        public async Task<bool> Handle( CreateScheduleSessionCommand cmd, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(cmd.ClassId))
                throw new ArgumentException("課程 ID 不可為空");

            if (string.IsNullOrWhiteSpace(cmd.Date))
                throw new InvalidOperationException("日期不能為空");

            var classId = cmd.ClassId.Trim();

            try
            {
                await _unitOfWork.BeginAsync(ct);
                var course = await _courseRepository.GetByIdAsync(classId, ct);
                if (course == null || course.IsActive == false)
                    throw new InvalidOperationException($"輸入的Class Id: {cmd.ClassId} 不存在或已停用");

                var instructor = await _userRepository.FindUserByIdAsync(course.DefaultInstructorId, ct);
                if (instructor is null || !instructor.IsActive)
                    throw new InvalidOperationException("課程預設老師不存在或已停用");

                var instructorRole = await _roleRepository.GetUserRoleAsync(
                    course.DefaultInstructorId, UserRoleCode.Instructor, ct);
                if (instructorRole is null || !instructorRole.IsActive)
                    throw new InvalidOperationException("課程預設老師目前不是有效老師");

                var date = ParseToDateOnly(cmd.Date);
                await _scheduleSessionRepository.AcquireWeekGenerationLockAsync(MondayOf(date), ct);
                var st = ParseToTimeOnly(cmd.StartTime);
                var startAt = date.ToDateTime(st);
                var newSession = ScheduleSession.Create(
                    date: date,
                    classId: course.Id,
                    className: course.Name,
                    classLabelColor: course.LabelColor,
                    instructorId: course.DefaultInstructorId,
                    instructorName: instructor.Name,
                    startAt: startAt,
                    endAt: startAt.AddMinutes(course.Duration),
                    isFree: cmd.IsFree ?? course.IsFree);

                var conflictSession = await _scheduleSessionRepository.GetOverlappingSchedulesSessionAsync(newSession, ct);
                if (conflictSession != null)
                    throw new InvalidOperationException($"新增排課程和 {conflictSession.ArrangeId} 衝堂");
                
                
                var added = await _scheduleSessionRepository.AddAsync(newSession, ct);
                if (!added)
                {
                    await _unitOfWork.RollbackAsync(ct);
                    return false;
                }

                await _unitOfWork.CommitAsync(ct);
                return true;
            }
            catch
            {
                await _unitOfWork.RollbackAsync(ct);
                throw;
            }
        }

        private DateOnly ParseToDateOnly(string date)
        {
            if (!DateOnly.TryParseExact(
                    date,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var result))
            {
                throw new ArgumentException("日期格式錯誤，請使用 yyyy-MM-dd");
            }

            return result;
        }


        private TimeOnly ParseToTimeOnly(string value)
        {
            if (!TimeOnly.TryParseExact(
                    value,
                    "HH:mm",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var result))
            {
                throw new ArgumentException("時間格式錯誤，請使用 HH:mm");
            }

            return result;
        }

        private static DateOnly MondayOf(DateOnly date)
        {
            var daysSinceMonday = ((int)date.DayOfWeek + 6) % 7;
            return date.AddDays(-daysSinceMonday);
        }

    }
}
