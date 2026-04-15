using gym_system.Domain.Repositories;

namespace gym_system.Application.Service
{
    public sealed class UserValidationService
    {
        private IUserRepository _userRepo;

        public UserValidationService(IUserRepository userRepo)
        {
            _userRepo = userRepo;
        }

        public async Task EnsurePhoneisNotExistAsync(string phone, CancellationToken ct)
        {
            if(await _userRepo.AnyPhoneExistsAsync(new[] { phone }, ct))
                throw new InvalidOperationException("Phone already exists");
        }

        public async Task EnsurePhoneAreNotExistAsync(IReadOnlyList<string> phones, CancellationToken ct)
        {
            if (await _userRepo.AnyPhoneExistsAsync(phones, ct))
                throw new InvalidOperationException("Phone already exists");
        }
    }
}
