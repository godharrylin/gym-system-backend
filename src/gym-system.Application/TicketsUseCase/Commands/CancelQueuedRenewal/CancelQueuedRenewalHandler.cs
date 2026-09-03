using gym_system.Domain.Repositories;

namespace gym_system.Application.TicketsUseCase.Commands.CancelQueuedRenewal
{
    public sealed class CancelQueuedRenewalHandler
    {
        private readonly ITicketPassRepository _ticketPassRepository;
        private readonly IStudentProfileRepository _studentProfileRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IClock _clock;

        public CancelQueuedRenewalHandler(
            ITicketPassRepository ticketPassRepository,
            IStudentProfileRepository studentProfileRepository,
            IUnitOfWork unitOfWork,
            IClock clock)
        {
            _ticketPassRepository = ticketPassRepository;
            _studentProfileRepository = studentProfileRepository;
            _unitOfWork = unitOfWork;
            _clock = clock;
        }

        public async Task Handle(
            string passId,
            string operatorId,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(passId))
            {
                throw new InvalidOperationException("票券 ID 必填");
            }

            var now = _clock.Now();
            await _unitOfWork.BeginAsync(ct);
            try
            {
                var cancelled = await _ticketPassRepository.CancelQueuedRenewalAsync(
                    passId.Trim(),
                    now,
                    operatorId,
                    ct) ?? throw new KeyNotFoundException("票券不存在");
                var snapshot = await _ticketPassRepository.ReconcileCurrentAsync(
                    cancelled.OwnerId,
                    DateOnly.FromDateTime(now),
                    now,
                    operatorId,
                    ct);
                var updated = snapshot is null
                    ? await _studentProfileRepository.ClearCurrentTicketAsync(
                        cancelled.OwnerId,
                        now,
                        ct)
                    : await _studentProfileRepository.UpdateCurrentTicketAsync(
                        cancelled.OwnerId,
                        snapshot,
                        ct);
                if (!updated)
                {
                    throw new InvalidOperationException("更新會員票券快照失敗");
                }

                await _unitOfWork.CommitAsync(ct);
            }
            catch
            {
                await _unitOfWork.RollbackAsync(ct);
                throw;
            }
        }
    }
}
