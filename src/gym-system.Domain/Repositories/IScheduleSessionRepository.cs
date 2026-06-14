using gym_system.Domain.Entities.ScheduleRules;
using gym_system.Domain.Entities.ScheduleSessions;

namespace gym_system.Domain.Repositories
{
    public interface IScheduleSessionRepository
    {
        Task<IReadOnlyList<ScheduleSession>> GetByWeekAsync(DateOnly weekStart, DateOnly weekEnd, CancellationToken ct);
        Task<IReadOnlyList<ScheduleSessionTemplate>> GetActiveTemplatesAsync(CancellationToken ct);
        Task AddRangeAsync(IReadOnlyList<ScheduleSession> sessions, CancellationToken ct);
        Task<ScheduleSession?> GetOverlappingSchedulesSessionAsync(ScheduleSession editScheduleSession, CancellationToken ct);
        Task<ScheduleSession?> GetByIdAsync(string sessionSn, CancellationToken ct);
        Task<bool> UpdateAsync(ScheduleSession scheduleSession, CancellationToken ct);
    }
}
