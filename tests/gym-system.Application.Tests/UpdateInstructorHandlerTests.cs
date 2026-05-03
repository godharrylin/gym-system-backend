using gym_system.Application.InstructorUseCase.Command.UpdateInstructor;
using gym_system.Domain.Entities.Users;
using gym_system.Domain.Enums;
using gym_system.Domain.Repositories;
using Xunit;

namespace gym_system.Application.Tests
{
    public sealed class UpdateInstructorHandlerTests
    {
        [Fact]
        public async Task Handle_ShouldUpdateProfileAndRoleStatus_WhenInputValid()
        {
            var sut = CreateSut();
            sut.UserRepository.FindByIdResult = User.Rehydrate("U0000000001", "Old", "0912000000", "pw");

            var command = new UpdateInstructorCommand
            {
                UserId = "U0000000001",
                Name = "New Name",
                Phone = "0912999888",
                IsEmployed = true
            };

            var result = await sut.Handler.Handle(command, CancellationToken.None);

            Assert.True(result);
            Assert.Equal(1, sut.UserRepository.UpdateProfileCallCount);
            Assert.Equal("U0000000001", sut.UserRepository.LastUpdatedUserId);
            Assert.Equal("New Name", sut.UserRepository.LastUpdatedName);
            Assert.Equal("0912999888", sut.UserRepository.LastUpdatedPhone);
            Assert.Equal(1, sut.RoleRepository.SetRoleActiveCallCount);
            Assert.True(sut.RoleRepository.LastSetRoleActiveValue);
            Assert.Equal(1, sut.UnitOfWork.CommitCount);
            Assert.Equal(0, sut.UnitOfWork.RollbackCount);
        }

        [Fact]
        public async Task Handle_ShouldRollbackAndThrow_WhenUserNotFound()
        {
            var sut = CreateSut();
            sut.UserRepository.FindByIdResult = null;

            var command = new UpdateInstructorCommand
            {
                UserId = "U0000000001",
                Name = "New Name",
                Phone = "0912999888",
                IsEmployed = false
            };

            await Assert.ThrowsAsync<InvalidOperationException>(() => sut.Handler.Handle(command, CancellationToken.None));
            Assert.Equal(1, sut.UnitOfWork.RollbackCount);
            Assert.Equal(0, sut.UnitOfWork.CommitCount);
        }

        [Fact]
        public async Task Handle_ShouldRollbackAndThrow_WhenPhoneUsedByOtherUser()
        {
            var sut = CreateSut();
            sut.UserRepository.FindByIdResult = User.Rehydrate("U0000000001", "Old", "0912000000", "pw");
            sut.UserRepository.ExistsPhoneInOtherUser = true;

            var command = new UpdateInstructorCommand
            {
                UserId = "U0000000001",
                Name = "New Name",
                Phone = "0912999888",
                IsEmployed = false
            };

            await Assert.ThrowsAsync<InvalidOperationException>(() => sut.Handler.Handle(command, CancellationToken.None));
            Assert.Equal(1, sut.UnitOfWork.RollbackCount);
            Assert.Equal(0, sut.UnitOfWork.CommitCount);
        }

        [Fact]
        public async Task Handle_ShouldRollbackAndThrow_WhenInstructorRoleNotFound()
        {
            var sut = CreateSut();
            sut.UserRepository.FindByIdResult = User.Rehydrate("U0000000001", "Old", "0912000000", "pw");
            sut.RoleRepository.GetRoleResult = null;

            var command = new UpdateInstructorCommand
            {
                UserId = "U0000000001",
                Name = "New Name",
                Phone = "0912999888",
                IsEmployed = false
            };

            await Assert.ThrowsAsync<InvalidOperationException>(() => sut.Handler.Handle(command, CancellationToken.None));
            Assert.Equal(1, sut.UnitOfWork.RollbackCount);
            Assert.Equal(0, sut.UnitOfWork.CommitCount);
        }

        [Fact]
        public async Task Handle_ShouldRollbackAndReturnFalse_WhenSetRoleActiveFails()
        {
            var sut = CreateSut();
            sut.UserRepository.FindByIdResult = User.Rehydrate("U0000000001", "Old", "0912000000", "pw");
            sut.RoleRepository.GetRoleResult = UserRole.Assign("U0000000001", UserRoleCode.Instructor, DateTime.UtcNow, true);
            sut.RoleRepository.SetRoleActiveResult = false;

            var command = new UpdateInstructorCommand
            {
                UserId = "U0000000001",
                Name = "New Name",
                Phone = "0912999888",
                IsEmployed = false
            };

            var result = await sut.Handler.Handle(command, CancellationToken.None);

            Assert.False(result);
            Assert.Equal(1, sut.UnitOfWork.RollbackCount);
            Assert.Equal(0, sut.UnitOfWork.CommitCount);
        }

        private static SutBundle CreateSut()
        {
            var userRepository = new FakeUserRepository();
            var roleRepository = new FakeUserRoleRepository();
            var unitOfWork = new FakeUnitOfWork();

            var handler = new UpdateInstructorHandler(userRepository, roleRepository, unitOfWork);
            return new SutBundle(handler, userRepository, roleRepository, unitOfWork);
        }

        private sealed record SutBundle(
            UpdateInstructorHandler Handler,
            FakeUserRepository UserRepository,
            FakeUserRoleRepository RoleRepository,
            FakeUnitOfWork UnitOfWork);

        private sealed class FakeUserRepository : IUserRepository
        {
            public User? FindByIdResult { get; set; }
            public bool ExistsPhoneInOtherUser { get; set; }
            public int UpdateProfileCallCount { get; private set; }
            public string LastUpdatedUserId { get; private set; } = string.Empty;
            public string LastUpdatedName { get; private set; } = string.Empty;
            public string LastUpdatedPhone { get; private set; } = string.Empty;

            public Task<IReadOnlyList<string>> GetExistingPhonesAsync(IReadOnlyList<string> phones, CancellationToken ct)
            {
                return Task.FromResult<IReadOnlyList<string>>([]);
            }

            public Task<string> AddAsync(User user, CancellationToken ct)
            {
                return Task.FromResult("U0000000001");
            }

            public Task<User?> FindUserByIdAsync(string userId, CancellationToken ct)
            {
                return Task.FromResult(FindByIdResult);
            }

            public Task<User?> FindUserByPhone(string phone, CancellationToken ct)
            {
                return Task.FromResult<User?>(null);
            }

            public Task<bool> ExistsPhoneForOtherUserAsync(string userId, string phone, CancellationToken ct)
            {
                return Task.FromResult(ExistsPhoneInOtherUser);
            }

            public Task<bool> UpdateBasicProfileAsync(string userId, string name, string phone, CancellationToken ct)
            {
                UpdateProfileCallCount++;
                LastUpdatedUserId = userId;
                LastUpdatedName = name;
                LastUpdatedPhone = phone;
                return Task.FromResult(true);
            }
        }

        private sealed class FakeUserRoleRepository : IUserRoleRepository
        {
            public UserRole? GetRoleResult { get; set; } = UserRole.Assign("U0000000001", UserRoleCode.Instructor, DateTime.UtcNow, true);
            public bool SetRoleActiveResult { get; set; } = true;
            public int SetRoleActiveCallCount { get; private set; }
            public bool LastSetRoleActiveValue { get; private set; }

            public Task<UserRole?> GetUserRoleAsync(string userId, UserRoleCode roleType, CancellationToken ct)
            {
                return Task.FromResult(GetRoleResult);
            }

            public Task<bool> AddRoleAsync(UserRole userRole, CancellationToken ct)
            {
                return Task.FromResult(true);
            }

            public Task<bool> ReactiveRole(string userId, UserRoleCode roleType, CancellationToken ct)
            {
                return Task.FromResult(true);
            }

            public Task<bool> SetRoleActiveAsync(string userId, UserRoleCode roleType, bool isActive, CancellationToken ct)
            {
                SetRoleActiveCallCount++;
                LastSetRoleActiveValue = isActive;
                return Task.FromResult(SetRoleActiveResult);
            }
        }

        private sealed class FakeUnitOfWork : IUnitOfWork
        {
            public int BeginCount { get; private set; }
            public int CommitCount { get; private set; }
            public int RollbackCount { get; private set; }

            public Task BeginAsync(CancellationToken ct)
            {
                BeginCount++;
                return Task.CompletedTask;
            }

            public Task CommitAsync(CancellationToken ct)
            {
                CommitCount++;
                return Task.CompletedTask;
            }

            public Task RollbackAsync(CancellationToken ct)
            {
                RollbackCount++;
                return Task.CompletedTask;
            }
        }
    }
}
