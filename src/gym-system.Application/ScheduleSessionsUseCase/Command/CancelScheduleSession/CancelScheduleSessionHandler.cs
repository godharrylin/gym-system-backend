using gym_system.Domain.Repositories;

namespace gym_system.Application.ScheduleSessionsUseCase.Command.CancelScheduleSession
{
    public class CancelScheduleSessionHandler
    {
        private readonly IScheduleSessionRepository _scheduleSessionRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IClock _clock;

        public CancelScheduleSessionHandler(
            IScheduleSessionRepository scheduleSessionRepository,
            IUnitOfWork unitOfWork,
            IClock clock)
        {
            _scheduleSessionRepository = scheduleSessionRepository;
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

                session.Cancel(_clock.Now());

                var updated = await _scheduleSessionRepository.UpdateAsync(session, ct);
                if (!updated)
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
    }
}
