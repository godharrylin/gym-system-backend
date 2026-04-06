using gym_system.Domain.Entities.Members;

namespace gym_system.Domain.Repositories
{
    public interface IStudentProfileRepository
    {
        Task AddRangeAsync(IReadOnlyList<StudentProfile> profiles, CancellationToken ct);
        Task UpdateCurrentTicketAsync(string userId, CurrentTicketSnapshot snapshot, CancellationToken ct);
    }
}
