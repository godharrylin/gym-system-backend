using gym_system.Domain.Entities.Users;

namespace gym_system.Domain.Repositories
{
    public interface IUserRepository
    {
        /// <summary>
        /// 從資料庫找是否有重複的電話號碼
        /// </summary>
        /// <param name="phones">手機號碼</param>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task<IReadOnlyList<string>> GetExistingPhonesAsync(IReadOnlyList<string> phones, CancellationToken ct);
        Task<string> AddAsync(User user, CancellationToken ct);
        Task<User?> FindUserByIdAsync(string userId, CancellationToken ct);
        Task<User?> FindUserByPhone(string phone, CancellationToken ct);
        Task<bool> ExistsPhoneForOtherUserAsync(string userId, string phone, CancellationToken ct);
        Task<bool> UpdateBasicProfileAsync(string userId, string name, string phone, CancellationToken ct);
    }
}
