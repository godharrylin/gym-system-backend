using gym_system.Domain.Entities.ScheduleSessions;

namespace gym_system.Domain.Repositories
{
    public interface IScheduleSessionLogRepository
    {
        Task AddAsync(ScheduleSessionLog log, CancellationToken ct);
    }
}
