using gym_system.Domain.Entities.ScheduleSessions;
using gym_system.Domain.Repositories;
using System.Globalization;

namespace gym_system.Application.ScheduleSessionsUseCase.Command.CreateScheduleSession
{
    public class CreateScheduleSessionHandler
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IScheduleSessionRepository _scheduleSessionRepository;
        private readonly ICourseRepository _courseRepository;

        public CreateScheduleSessionHandler(IUnitOfWork unitOfWork, 
                                            IScheduleSessionRepository scheduleSessionRepository,
                                            ICourseRepository courseRepository) 
        {
            _unitOfWork = unitOfWork;
            _scheduleSessionRepository = scheduleSessionRepository;
            _courseRepository = courseRepository;
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

                var date = ParseToDateOnly(cmd.Date);
                var st = ParseToTimeOnly(cmd.StartTime);
                var startAt = date.ToDateTime(st);
                var newSession = ScheduleSession.Create(
                    date: date,
                    classId: course.Id,
                    className: course.Name,
                    classLabelColor: course.LabelColor,
                    instructorId: course.DefaultInstructorId,
                    instructorName: course.DefaultInstructorName,
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

    }
}
