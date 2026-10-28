using gym_system.Domain.Entities.ScheduleSessions;
using gym_system.Domain.Repositories;

namespace gym_system.Application.ScheduleSessionsUseCase.Command.GetOrEnsureScheduleWeek
{
    public sealed class GetOrEnsureScheduleWeekHandler
    {
        private readonly IScheduleSessionRepository _scheduleSessionRepository;
        private readonly IUnitOfWork _unitOfWork;

        public GetOrEnsureScheduleWeekHandler(
            IScheduleSessionRepository scheduleSessionRepository,
            IUnitOfWork unitOfWork)
        {
            _scheduleSessionRepository = scheduleSessionRepository;
            _unitOfWork = unitOfWork;
        }

        public async Task<GetOrEnsureScheduleWeekResult> Handle(GetOrEnsureScheduleWeekCommand command, CancellationToken ct)
        {
            var weekStart = NormalizeToMonday(command.WeekStart);
            var weekEnd = weekStart.AddDays(6);

            await _unitOfWork.BeginAsync(ct);
            try
            {
                // Serialize the read-empty-generate sequence across API processes.
                await _scheduleSessionRepository.AcquireWeekGenerationLockAsync(weekStart, ct);
                var existingSessions = await _scheduleSessionRepository.GetByWeekAsync(weekStart, weekEnd, ct);
                if (existingSessions.Count > 0)
                {
                    await _unitOfWork.CommitAsync(ct);
                    var notCancelSession = existingSessions.Where(session => session.Status != SessionStatus.Cancel).ToList();
                    return BuildResult(weekStart, weekEnd, "REAL", false, notCancelSession);
                }

                var templates = await _scheduleSessionRepository.GetActiveTemplatesAsync(ct);
                var sessions = templates
                    .Select(template => ScheduleSession.CreateAutoFromTemplate(template, weekStart))
                    .ToList();

                EnsureGeneratedSessionsDoNotConflict(sessions);

                if (sessions.Count > 0)
                {
                    await _scheduleSessionRepository.AddRangeAsync(sessions, ct);
                }

                var ensuredSessions = await _scheduleSessionRepository.GetByWeekAsync(weekStart, weekEnd, ct);
                await _unitOfWork.CommitAsync(ct);

                var visibleSessions = ensuredSessions.Where(session => session.Status != SessionStatus.Cancel).ToList();
                return BuildResult(weekStart, weekEnd, "TEMPLATE", ensuredSessions.Count > 0, visibleSessions);
            }
            catch
            {
                await _unitOfWork.RollbackAsync(ct);
                throw;
            }
        }

        private static DateOnly NormalizeToMonday(DateOnly date)
        {
            var daysSinceMonday = ((int)date.DayOfWeek + 6) % 7;
            return date.AddDays(-daysSinceMonday);
        }

        private static void EnsureGeneratedSessionsDoNotConflict(IReadOnlyList<ScheduleSession> sessions)
        {
            var ordered = sessions.OrderBy(session => session.StartAt).ToList();
            for (var index = 1; index < ordered.Count; index++)
            {
                var previous = ordered[index - 1];
                var current = ordered[index];
                if (current.StartAt < previous.EndAt.AddMinutes(10))
                    throw new InvalidOperationException(
                        $"啟用中的課表模板在 {current.Date:yyyy-MM-dd} 有重疊或未保留十分鐘間隔，無法生成實際場次。");
            }
        }

        private static GetOrEnsureScheduleWeekResult BuildResult(
            DateOnly weekStart,
            DateOnly weekEnd,
            string source,
            bool created,
            IReadOnlyList<ScheduleSession> sessions)
        {
            return new GetOrEnsureScheduleWeekResult
            {
                WeekStart = weekStart,
                WeekEnd = weekEnd,
                Source = source,
                Created = created,
                Sessions = sessions.Select(ToResult).ToList()
            };
        }

        private static ScheduleWeekSessionResult ToResult(ScheduleSession session)
        {
            return new ScheduleWeekSessionResult
            {
                ArrangeSn = session.ArrangeSn,
                ArrangeId = session.ArrangeId,
                Date = session.Date,
                DayOfWeek = session.Date.DayOfWeek,
                StartTime = session.StartAt.ToString("HH:mm"),
                EndTime = session.EndAt.ToString("HH:mm"),
                ClassDefId = session.ClassId,
                ClassName = session.ClassName,
                InstructorId = session.InstructorId,
                InstructorName = session.InstructorName,
                Duration = (int)(session.EndAt - session.StartAt).TotalMinutes,
                Color = session.ClassLabelColor,
                IsFree = session.IsFree,
                Status = session.Status.ToString(),
                Source = session.Source,
                RuleSn = session.RuleSn
            };
        }

    }
}
