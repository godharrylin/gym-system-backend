using gym_system.Domain.Entities.ScheduleSessions;

namespace gym_system.Application.ClassAttendanceProcessing;

public interface IScheduleAttendanceSynchronizer
{
    // The schedule handler owns the transaction and the canonical session mutation lock.
    Task SynchronizeAsync(ScheduleSession session, DateTime changedAt, string operatorId, CancellationToken ct);
}
