using gym_system.Domain.Repositories;
using gym_system.Application.TicketsUseCase.Services;

namespace gym_system.Application.TicketsUseCase.Queries
{
    public sealed class GetStudentTicketPassesHandler
    {
        private readonly IStudentTicketPassQueryService _queryService;
        private readonly ICurrentTicketService _currentTickets;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IClock _clock;

        public GetStudentTicketPassesHandler(
            IStudentTicketPassQueryService queryService,
            ICurrentTicketService currentTickets,
            IUnitOfWork unitOfWork,
            IClock clock)
        {
            _queryService = queryService;
            _currentTickets = currentTickets;
            _unitOfWork = unitOfWork;
            _clock = clock;
        }

        public async Task<IReadOnlyList<StudentTicketPassResult>> Handle(
            string studentId,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(studentId))
            {
                throw new InvalidOperationException("學生 ID 必填");
            }

            var normalizedStudentId = studentId.Trim();
            var now = _clock.Now();
            await _unitOfWork.BeginAsync(ct);
            try
            {
                await _currentTickets.ReconcileAsync(normalizedStudentId, now, "SYSTEM", ct);

                await _unitOfWork.CommitAsync(ct);
            }
            catch (StudentProfileSnapshotUpdateException ex)
            {
                await _unitOfWork.RollbackAsync(CancellationToken.None);
                // Preserve the existing query endpoint's missing-profile -> 404 behavior.
                throw new KeyNotFoundException("學生不存在或沒有 Profile", ex);
            }
            catch
            {
                await _unitOfWork.RollbackAsync(ct);
                throw;
            }

            return await _queryService.GetByStudentIdAsync(normalizedStudentId, ct);
        }
    }
}
