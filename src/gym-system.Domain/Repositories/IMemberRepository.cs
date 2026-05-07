using gym_system.Domain.Entities.Members;

namespace gym_system.Domain.Repositories
{
    public interface IMemberRepository
    {
        /// <summary>
        /// 從資料庫找是否有重複的電話號碼
        /// </summary>
        /// <param name="phones">手機號碼</param>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task<bool> AnyPhoneExistsAsync(IReadOnlyList<string> phones, CancellationToken ct);
        Task AddRangeAsync(IReadOnlyList<Member> members, CancellationToken ct);
        Task<List<string>> GenerateIdsAsync(int count, CancellationToken ct);
    }
}
