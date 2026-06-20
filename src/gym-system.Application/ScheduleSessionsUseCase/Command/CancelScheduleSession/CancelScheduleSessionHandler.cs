using gym_system.Application.ScheduleSessionsUseCase.Command;
using gym_system.Domain.Entities.ScheduleSessions;
using gym_system.Domain.Repositories;

namespace gym_system.Application.ScheduleSessionsUseCase.Command.CancelScheduleSession
{
    public class CancelScheduleSessionHandler
    {
        private readonly IScheduleSessionRepository _scheduleSessionRepository;
        private readonly IScheduleSessionLogRepository _scheduleSessionLogRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IClock _clock;

        public CancelScheduleSessionHandler(
            IScheduleSessionRepository scheduleSessionRepository,
            IScheduleSessionLogRepository scheduleSessionLogRepository,
            IUnitOfWork unitOfWork,
            IClock clock)
        {
            _scheduleSessionRepository = scheduleSessionRepository;
            _scheduleSessionLogRepository = scheduleSessionLogRepository;
            _unitOfWork = unitOfWork;
            _clock = clock;
        }

        public async Task<bool> Handle(CancelScheduleSessionCommand cmd, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(cmd.ArrangeId))
                throw new ArgumentException("排課 ID 不可為空");

            await _unitOfWork.BeginAsync(ct);
            try
            {
                var session = await _scheduleSessionRepository.GetByIdAsync(cmd.ArrangeId.Trim(), ct)
                    ?? throw new InvalidOperationException("找不到排課資料");
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

                var log = ScheduleSessionLog.Create(
                    arrangeSn: session.ArrangeSn,
                    changedData: changedData,
                    operatorId: cmd.OperatorId,
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
