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

        public async Task EnsurePhoneIsNotExistAsync(string phone, CancellationToken ct)
        {
            var existPhone = await _userRepo.GetExistingPhonesAsync(new[] { phone }, ct);
            if (existPhone is not null && existPhone.Any())
                throw new InvalidOperationException("Phone already exists");
        }

        public async Task EnsurePhoneAreNotExistAsync(IReadOnlyList<string> phones, CancellationToken ct)
        {
            var existPhones = await _userRepo.GetExistingPhonesAsync(phones, ct);
            if (existPhones is not null && existPhones.Any())
                throw new InvalidOperationException("Phone already exists");
        }
    }
}
