using gym_system.Domain.Entities.ScheduleSessions;

namespace gym_system.Application.ScheduleSessionsUseCase.Command
{
    internal sealed record ScheduleSessionSnapshot(
        DateOnly Date,
        string ClassId,
        string ClassName,
        string ClassLabelColor,
        string InstructorId,
        string InstructorName,
        DateTime StartAt,
        DateTime EndAt,
        SessionStatus Status,
        bool IsFree)
    {
        internal static ScheduleSessionSnapshot From(ScheduleSession session)
        {
            return new ScheduleSessionSnapshot(
                session.Date,
                session.ClassId,
                session.ClassName,
                session.ClassLabelColor,
                session.InstructorId,
                session.InstructorName,
                session.StartAt,
                session.EndAt,
                session.Status,
                session.IsFree);
        }
    }
}
