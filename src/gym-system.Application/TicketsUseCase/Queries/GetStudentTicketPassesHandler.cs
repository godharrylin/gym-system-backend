using gym_system.Domain.Repositories;

namespace gym_system.Application.TicketsUseCase.Queries
{
    public sealed class GetStudentTicketPassesHandler
    {
        private readonly IStudentTicketPassQueryService _queryService;
        private readonly ITicketPassRepository _ticketPassRepository;
        private readonly IStudentProfileRepository _studentProfileRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IClock _clock;

        public GetStudentTicketPassesHandler(
            IStudentTicketPassQueryService queryService,
            ITicketPassRepository ticketPassRepository,
            IStudentProfileRepository studentProfileRepository,
            IUnitOfWork unitOfWork,
            IClock clock)
        {
            _queryService = queryService;
            _ticketPassRepository = ticketPassRepository;
            _studentProfileRepository = studentProfileRepository;
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
                var snapshot = await _ticketPassRepository.ReconcileCurrentAsync(
                    normalizedStudentId,
                    DateOnly.FromDateTime(now),
                    now,
                    "SYSTEM",
                    ct);
                var updated = snapshot is null
                    ? await _studentProfileRepository.ClearCurrentTicketAsync(
                        normalizedStudentId,
                        now,
                        ct)
                    : await _studentProfileRepository.UpdateCurrentTicketAsync(
                        normalizedStudentId,
                        snapshot,
                        ct);
                if (!updated)
                {
                    throw new KeyNotFoundException("學生不存在或沒有 Profile");
                }

                await _unitOfWork.CommitAsync(ct);
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
