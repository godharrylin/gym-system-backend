using gym_system.Domain.Entities.Members;

namespace gym_system.Domain.Repositories
{
    public interface IStudentProfileRepository
    {
        Task AddAsync(StudentProfile profile, CancellationToken ct);
        Task AddRangeAsync(IReadOnlyList<StudentProfile> profiles, CancellationToken ct);
        Task<StudentProfile?> FindByUserIdAsync(string userId, CancellationToken ct);
        Task<bool> UpdateLastVisitAsync(string userId, DateTime lastVisitAt, CancellationToken ct);
        Task<bool> UpdateCurrentTicketAsync(string userId, CurrentTicketSnapshot snapshot, CancellationToken ct);
        Task<bool> ClearCurrentTicketAsync(string userId, DateTime updatedAt, CancellationToken ct);
    }
}
