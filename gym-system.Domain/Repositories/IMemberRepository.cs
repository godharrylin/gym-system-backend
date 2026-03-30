using gym_system.Domain.Entities.Members;

namespace gym_system.Domain.Repositories
{
    public interface IMemberRepository
    {
        Task<bool> AnyPhoneExistsAsync(IReadOnlyList<string> phones, CancellationToken ct);
        Task AddRangeAsync(IReadOnlyList<Member> members, CancellationToken ct);
        Task<List<string>> GenerateIdsAsync(int count, CancellationToken ct);
    }
}
