using gym_system.Application.AuthUseCase.LoginByPhone;
using gym_system.Domain.Entities.Users;
using gym_system.Domain.Enums;
using gym_system.Domain.Repositories;
using Xunit;

namespace gym_system.Application.Tests
{
    public sealed class LoginByPhoneHandlerTests
    {
        [Fact]
        public async Task Handle_ShouldReturnTokenAndUser_WhenPhoneExistsAndUserIsActive()
        {
            var sut = CreateSut();
            sut.UserRepository.Users.Add(User.Rehydrate(
                "U0000000001",
                "王小明",
                "0912345678",
                "0912345678",
                true));
            sut.RoleRepository.Roles.Add(UserRole.Assign(
                "U0000000001",
                UserRoleCode.Admin,
                new DateTime(2026, 6, 28),
                true));

            var result = await sut.Handler.Handle(new LoginByPhoneCommand { Phone = " 0912345678 " });

            Assert.Equal("fake.jwt.token", result.AccessToken);
            Assert.Equal("U0000000001", result.User.Id);
            Assert.Equal("0912345678", result.User.Phone);
            Assert.Contains("Admin", result.User.Roles);
        }

        [Fact]
        public async Task Handle_ShouldThrow_WhenPhoneIsEmpty()
        {
            var sut = CreateSut();

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => sut.Handler.Handle(new LoginByPhoneCommand { Phone = "" }));
        }

        [Fact]
        public async Task Handle_ShouldThrow_WhenUserNotFound()
        {
            var sut = CreateSut();

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => sut.Handler.Handle(new LoginByPhoneCommand { Phone = "0912345678" }));
        }

        [Fact]
        public async Task Handle_ShouldThrow_WhenUserIsInactive()
        {
            var sut = CreateSut();
            sut.UserRepository.Users.Add(User.Rehydrate(
                "U0000000001",
                "王小明",
                "0912345678",
                "0912345678",
                false));

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => sut.Handler.Handle(new LoginByPhoneCommand { Phone = "0912345678" }));
        }

        private static SutBundle CreateSut()
        {
            var userRepository = new FakeUserRepository();
            var roleRepository = new FakeUserRoleRepository();
            var tokenGenerator = new FakeAccessTokenGenerator();
            var handler = new LoginByPhoneHandler(userRepository, roleRepository, tokenGenerator);

            return new SutBundle(handler, userRepository, roleRepository);
        }

        private sealed record SutBundle(
            LoginByPhoneHandler Handler,
            FakeUserRepository UserRepository,
            FakeUserRoleRepository RoleRepository);

        private sealed class FakeUserRepository : IUserRepository
        {
            public List<User> Users { get; } = [];

            public Task<IReadOnlyList<string>> GetExistingPhonesAsync(IReadOnlyList<string> phones, CancellationToken ct)
            {
                IReadOnlyList<string> result = Users
                    .Where(user => phones.Contains(user.Phone, StringComparer.Ordinal))
                    .Select(user => user.Phone)
                    .ToList();
                return Task.FromResult(result);
            }

            public Task<string> AddAsync(User user, CancellationToken ct)
            {
                Users.Add(user);
                return Task.FromResult(user.Id);
            }

            public Task<User?> FindUserByIdAsync(string userId, CancellationToken ct)
            {
                return Task.FromResult(Users.FirstOrDefault(user => user.Id == userId));
            }

            public Task<User?> FindUserByPhoneAsync(string phone, CancellationToken ct)
            {
                return Task.FromResult(Users.FirstOrDefault(user => user.Phone == phone));
            }

            public Task<bool> ExistsPhoneForOtherUserAsync(string userId, string phone, CancellationToken ct)
            {
                return Task.FromResult(false);
            }

            public Task<bool> UpdateBasicProfileAsync(string userId, string name, string phone, CancellationToken ct)
            {
                return Task.FromResult(false);
            }
        }

        private sealed class FakeUserRoleRepository : IUserRoleRepository
        {
            public List<UserRole> Roles { get; } = [];

            public Task<UserRole?> GetUserRoleAsync(string userId, UserRoleCode roleType, CancellationToken ct)
            {
                return Task.FromResult(Roles.FirstOrDefault(role => role.UserId == userId && role.RoleCode == roleType));
            }

            public Task<IReadOnlyList<UserRole>> GetActiveRolesAsync(string userId, CancellationToken ct)
            {
                IReadOnlyList<UserRole> result = Roles
                    .Where(role => role.UserId == userId && role.IsActive)
                    .ToList();
                return Task.FromResult(result);
            }

            public Task<bool> AddRoleAsync(UserRole userRole, CancellationToken ct)
            {
                Roles.Add(userRole);
                return Task.FromResult(true);
            }

            public Task<bool> ReactivateRoleAsync(string userId, UserRoleCode roleType, CancellationToken ct)
            {
                return Task.FromResult(false);
            }

            public Task<bool> SetRoleActiveAsync(string userId, UserRoleCode roleType, bool isActive, CancellationToken ct)
            {
                return Task.FromResult(false);
            }
        }

        private sealed class FakeAccessTokenGenerator : IAccessTokenGenerator
        {
            public AccessTokenResult Generate(User user, IReadOnlyList<string> roles)
            {
                return new AccessTokenResult
                {
                    AccessToken = "fake.jwt.token",
                    ExpiresAt = new DateTime(2026, 6, 28, 10, 0, 0, DateTimeKind.Utc)
                };
            }
        }
    }
}
