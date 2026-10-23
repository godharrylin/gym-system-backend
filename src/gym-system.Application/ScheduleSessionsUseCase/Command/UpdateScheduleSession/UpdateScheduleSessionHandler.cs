using gym_system.Application.Common;
using gym_system.Application.Common.Authorization;
using gym_system.Application.ScheduleSessionsUseCase.Command;
using gym_system.Domain.Entities.Courses;
using gym_system.Domain.Entities.ScheduleSessions;
using gym_system.Domain.Repositories;
using System.Globalization;
using gym_system.Domain.Enums;
using gym_system.Application.ClassAttendanceProcessing;

namespace gym_system.Application.ScheduleSessionsUseCase.Command.UpdateScheduleSession
{
    public class UpdateScheduleSessionHandler
    {
        private readonly IScheduleSessionRepository _scheduleSessionRepository;
        private readonly ICourseRepository _courseRepository;
        private readonly IUserRepository _userRepository;
        private readonly IUserRoleRepository _roleRepository;
        private readonly IUserPermissionService _userPermissionService;
        private readonly IScheduleSessionLogRepository _scheduleSessionLogRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IClock _clock;
        private readonly IScheduleAttendanceSynchronizer _attendanceSynchronizer;

        public UpdateScheduleSessionHandler(
            IScheduleSessionRepository scheduleSessionRepository,
            ICourseRepository courseRepository,
            IUnitOfWork unitOfWork,
            IClock clock,
            IUserRepository userRepository,
            IUserRoleRepository roleRepository,
            IUserPermissionService userPermissionService,
            IScheduleSessionLogRepository scheduleSessionLogRepository,
            IScheduleAttendanceSynchronizer attendanceSynchronizer)
        {
            _scheduleSessionRepository = scheduleSessionRepository;
            _courseRepository = courseRepository;
            _unitOfWork = unitOfWork;
            _clock = clock;
            _userRepository = userRepository;
            _roleRepository = roleRepository;
            _userPermissionService = userPermissionService;
            _scheduleSessionLogRepository = scheduleSessionLogRepository;
            _attendanceSynchronizer = attendanceSynchronizer;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="cmd"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        /// <remarks>not finished 要加上寫編輯紀錄的log</remarks>
        public async Task<bool> Handle(UpdateScheduleSessionCommand cmd, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(cmd.SessionId))
                throw new ArgumentException("排課 ID 不可為空");
            var operatorId = cmd.OperatorId?.Trim();
            ArgumentException.ThrowIfNullOrWhiteSpace(operatorId);

            await _userPermissionService.EnsureHasAnyRoleAsync(
                operatorId,
                [UserRoleCode.Admin, UserRoleCode.Staff],
                ct);

            await _unitOfWork.BeginAsync(ct);
            try
            {
                await _scheduleSessionRepository.AcquireSessionMutationLockAsync(cmd.SessionId.Trim(), ct);
                var session = await _scheduleSessionRepository.GetByIdAsync(cmd.SessionId.Trim(), ct)
                    ?? throw new InvalidOperationException("找不到排課資料");

                session.EnsureEditable(_clock.Now());

                var before = ScheduleSessionSnapshot.From(session);

                var requestedClassId = NormalizeOptional(cmd.ClassId);
                var classChanged =
                    requestedClassId is not null &&
                    requestedClassId != session.ClassId;

                var effectiveClassId = requestedClassId ?? session.ClassId;
                var currentCourse = await _courseRepository.GetByIdAsync(effectiveClassId, ct)
                    ?? throw new InvalidOperationException("選擇的課程不存在");
                if (!currentCourse.IsActive)
                    throw new InvalidOperationException("選擇的課程已下架");
                Course? newCourse = classChanged ? currentCourse : null;

                var date = ParseDateOrDefault(cmd.Date, session.Date);
                var startTime = ParseTimeOrDefault(cmd.StartTime, TimeOnly.FromDateTime(session.StartAt));
                var startAt = date.ToDateTime(startTime);

                foreach (var weekStart in new[] { MondayOf(session.Date), MondayOf(date) }.Distinct().Order())
                    await _scheduleSessionRepository.AcquireWeekGenerationLockAsync(weekStart, ct);

                var duration = newCourse is null
                    ? session.EndAt - session.StartAt
                    : TimeSpan.FromMinutes(newCourse.Duration);
                var endAt = startAt.Add(duration);

                var instructorId = session.InstructorId;
                var instructorName = session.InstructorName;
                var requestedInstructorId = NormalizeOptional(cmd.InstructorId);
                var effectiveInstructorId = requestedInstructorId ?? session.InstructorId;
                var instructor = await _userRepository.FindUserByIdAsync(effectiveInstructorId, ct);
                if (instructor is null || !instructor.IsActive)
                    throw new InvalidOperationException("選擇的老師不存在或已停用");

                var instructorRole = await _roleRepository.GetUserRoleAsync(
                    effectiveInstructorId, UserRoleCode.Instructor, ct);
                if (instructorRole is null || !instructorRole.IsActive)
                    throw new InvalidOperationException("選擇的使用者目前不是有效老師");

                if (requestedInstructorId is not null &&
                    requestedInstructorId != session.InstructorId)
                {
                    instructorId = instructor.Id;
                    instructorName = instructor.Name;
                }

                session.Update(
                    date: date,
                    classId: newCourse?.Id ?? session.ClassId,
                    className: newCourse?.Name ?? session.ClassName,
                    classLabelColor: newCourse?.LabelColor ?? session.ClassLabelColor,
                    instructorId: instructorId,
                    instructorName: instructorName,
                    startAt: startAt,
                    endAt: endAt,
                    status: ParseStatusOrDefault(cmd.Status, session.Status),
                    isFree: cmd.IsFree ?? newCourse?.IsFree ?? session.IsFree,
                    updateTime: _clock.Now());

                var changedData = ScheduleSessionChangeLogBuilder.Build(before, session);
                if (changedData is null)
                {
                    await _unitOfWork.CommitAsync(ct);
                    return true;
                }

                if (session.Status != SessionStatus.Cancel)
                {
                    var conflictSession =
                        await _scheduleSessionRepository.GetOverlappingSchedulesSessionAsync(session, ct);

                    if (conflictSession is not null)
                        throw new InvalidOperationException($"該時段與排課 {conflictSession.ArrangeId} 衝堂");
                }

                var updated = await _scheduleSessionRepository.UpdateAsync(session, ct);
                if (!updated)
                {
                    await _unitOfWork.RollbackAsync(ct);
                    return false;
                }

                await _attendanceSynchronizer.SynchronizeAsync(session, session.UpdateTime!.Value, operatorId, ct);

                var log = ScheduleSessionLog.Create(
                    arrangeSn: session.ArrangeSn,
                    changedData: changedData,
                    operatorId: operatorId,
                    remark: cmd.Remark);

                await _scheduleSessionLogRepository.AddAsync(log, ct);

                await _unitOfWork.CommitAsync(ct);
                return true;
            }
            catch
            {
                await _unitOfWork.RollbackAsync(ct);
                throw;
            }
        }

        private DateOnly ParseDateOrDefault(string? value, DateOnly defaultValue)
        {
            if (value is null) return defaultValue;

            if (!DateOnly.TryParseExact(
                    value,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var result))
            {
                throw new ArgumentException("日期格式錯誤，請使用 yyyy-MM-dd");
            }

            return result;
        }

        private static TimeOnly ParseTimeOrDefault(string? value, TimeOnly defaultValue)
        {
            if (value is null) return defaultValue;

            if (!TimeOnly.TryParseExact(
                    value,
                    ["HH:mm", "HH:mm:ss"],
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var result))
            {
                throw new ArgumentException("開始時間格式錯誤，請使用 HH:mm");
            }

            return result;
        }

        private static SessionStatus ParseStatusOrDefault(
            string? editSessionStatus,
            SessionStatus originSessionStatus)
        {
            if (editSessionStatus is null) return originSessionStatus;

            if (!Enum.TryParse<SessionStatus>(editSessionStatus, ignoreCase: true, out var result) ||
                !Enum.IsDefined(result))
            {
                throw new ArgumentException($"無效的 SessionStatus: {editSessionStatus}");
            }

            return result;
        }

        private static string? NormalizeOptional(string? value)
        {
            if (value is null) return null;
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("更新欄位不可為空字串");

            return value.Trim();
        }

        private static DateOnly MondayOf(DateOnly date)
        {
            var daysSinceMonday = ((int)date.DayOfWeek + 6) % 7;
            return date.AddDays(-daysSinceMonday);
        }

    }
}
