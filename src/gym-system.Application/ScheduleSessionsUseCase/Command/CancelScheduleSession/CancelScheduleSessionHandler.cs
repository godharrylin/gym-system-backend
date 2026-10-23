using gym_system.Application.ScheduleSessionsUseCase.Command;
using gym_system.Domain.Entities.ScheduleSessions;
using gym_system.Domain.Repositories;
using gym_system.Application.ClassAttendanceProcessing;

namespace gym_system.Application.ScheduleSessionsUseCase.Command.CancelScheduleSession
{
    public class CancelScheduleSessionHandler
    {
        private readonly IScheduleSessionRepository _scheduleSessionRepository;
        private readonly IScheduleSessionLogRepository _scheduleSessionLogRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IClock _clock;
        private readonly IScheduleAttendanceSynchronizer _attendanceSynchronizer;

        public CancelScheduleSessionHandler(
            IScheduleSessionRepository scheduleSessionRepository,
            IScheduleSessionLogRepository scheduleSessionLogRepository,
            IUnitOfWork unitOfWork,
            IClock clock,
            IScheduleAttendanceSynchronizer attendanceSynchronizer)
        {
            _scheduleSessionRepository = scheduleSessionRepository;
            _scheduleSessionLogRepository = scheduleSessionLogRepository;
            _unitOfWork = unitOfWork;
            _clock = clock;
            _attendanceSynchronizer = attendanceSynchronizer;
        }

        public async Task<bool> Handle(CancelScheduleSessionCommand cmd, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(cmd.ArrangeId))
                throw new ArgumentException("排課 ID 不可為空");
            var operatorId = cmd.OperatorId?.Trim();
            ArgumentException.ThrowIfNullOrWhiteSpace(operatorId);

            await _unitOfWork.BeginAsync(ct);
            try
            {
                await _scheduleSessionRepository.AcquireSessionMutationLockAsync(cmd.ArrangeId.Trim(), ct);
                var session = await _scheduleSessionRepository.GetByIdAsync(cmd.ArrangeId.Trim(), ct)
                    ?? throw new InvalidOperationException("找不到排課資料");

                var daysSinceMonday = ((int)session.Date.DayOfWeek + 6) % 7;
                await _scheduleSessionRepository.AcquireWeekGenerationLockAsync(
                    session.Date.AddDays(-daysSinceMonday), ct);

                session.EnsureEditable(_clock.Now());

                var before = ScheduleSessionSnapshot.From(session);

                session.Cancel(_clock.Now());

                var changedData = ScheduleSessionChangeLogBuilder.Build(before, session);
                if (changedData is null)
                {
                    await _unitOfWork.CommitAsync(ct);
                    return true;
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
    }
}
