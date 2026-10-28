using gym_system.Domain.Repositories;
using gym_system.Application.TicketsUseCase.Services;

namespace gym_system.Application.TicketsUseCase.Commands.CancelQueuedRenewal
{
    public sealed class CancelQueuedRenewalHandler
    {
        private readonly ITicketPassRepository _ticketPassRepository;
        private readonly ICurrentTicketService _currentTickets;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IClock _clock;

        public CancelQueuedRenewalHandler(
            ITicketPassRepository ticketPassRepository,
            ICurrentTicketService currentTickets,
            IUnitOfWork unitOfWork,
            IClock clock)
        {
            _ticketPassRepository = ticketPassRepository;
            _currentTickets = currentTickets;
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
                await _currentTickets.ReconcileAsync(cancelled.OwnerId, now, operatorId, ct);

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
