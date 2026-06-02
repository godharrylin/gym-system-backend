

namespace gym_system.Application.ScheduleRulesUseCase.Queries
{
    public interface IScheduleRulesQueryService
    {
        Task<IReadOnlyList<ScheduleRulesResult>> GetScheduleRulesAsync(CancellationToken ct);
    }
}
