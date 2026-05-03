using gym_system.Application.InstructorUseCase.Command.CreateInstructor;
using gym_system.Domain.Entities.Users;
using gym_system.Domain.Enums;
using gym_system.Domain.Repositories;
using Xunit;

namespace gym_system.Application.Tests
{
    public sealed class CreateInstructorHandlerTests
    {
        [Fact]
        public async Task Handle_ShouldCreateUserAndAssignInstructorRole_WhenUserNotExists()
        {
            // Covers handler branch: user == null (Create user) + role == null (Add role).
            var sut = CreateSut();
            sut.UserRepository.FindUserResult = null;
            sut.UserRepository.AddResultUserId = "U0000000001";

            var command = new CreateInstructorCommand
            {
                Name = " 王小明 ",
                Phone = " 0912345678 "
            };

            var result = await sut.Handler.Handle(command, CancellationToken.None);

            Assert.True(result);
            Assert.Equal(1, sut.UserRepository.AddCallCount);
            Assert.NotNull(sut.UserRepository.LastAddedUser);
            Assert.Equal("王小明", sut.UserRepository.LastAddedUser!.Name);
            Assert.Equal("0912345678", sut.UserRepository.LastFindPhone);
            Assert.Equal("U0000000001", sut.RoleRepository.LastGetRoleUserId);
            Assert.Equal(1, sut.RoleRepository.AddRoleCallCount);
            Assert.Equal(1, sut.UnitOfWork.CommitCount);
            Assert.Equal(0, sut.UnitOfWork.RollbackCount);
        }

        [Fact]
        public async Task Handle_ShouldAssignInstructorRole_WhenUserExistsAndRoleNotExists()
        {
            // Covers handler branch: user exists + role == null (Add role only).
            var sut = CreateSut();
            sut.UserRepository.FindUserResult = User.Rehydrate("U0000000002", "Amy", "0911222333", "pw");
            sut.RoleRepository.GetRoleResult = null;

            var command = new CreateInstructorCommand
            {
                Name = "Amy",
                Phone = "0911222333"
            };

            var result = await sut.Handler.Handle(command, CancellationToken.None);

            Assert.True(result);
            Assert.Equal(0, sut.UserRepository.AddCallCount);
            Assert.Equal(1, sut.RoleRepository.AddRoleCallCount);
            Assert.Equal("U0000000002", sut.RoleRepository.LastAddedRole!.UserId);
            Assert.Equal(UserRoleCode.Instructor, sut.RoleRepository.LastAddedRole!.RoleCode);
            Assert.Equal(1, sut.UnitOfWork.CommitCount);
            Assert.Equal(0, sut.UnitOfWork.RollbackCount);
        }

        [Fact]
        public async Task Handle_ShouldReactivateRole_WhenRoleExistsButInactive()
        {
            // Covers handler branch: role exists but inactive (ReactiveRole).
            var sut = CreateSut();
            sut.UserRepository.FindUserResult = User.Rehydrate("U0000000003", "Ben", "0911000000", "pw");
            sut.RoleRepository.GetRoleResult = UserRole.Assign(
                "U0000000003",
                UserRoleCode.Instructor,
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                false);

            var command = new CreateInstructorCommand
            {
                Name = "Ben",
                Phone = "0911000000"
            };

            var result = await sut.Handler.Handle(command, CancellationToken.None);

            Assert.True(result);
            Assert.Equal(0, sut.RoleRepository.AddRoleCallCount);
            Assert.Equal(1, sut.RoleRepository.ReactiveCallCount);
            Assert.Equal("U0000000003", sut.RoleRepository.LastReactiveUserId);
            Assert.Equal(UserRoleCode.Instructor, sut.RoleRepository.LastReactiveRoleCode);
            Assert.Equal(1, sut.UnitOfWork.CommitCount);
            Assert.Equal(0, sut.UnitOfWork.RollbackCount);
        }

        [Fact]
        public async Task Handle_ShouldBeIdempotentSuccess_WhenRoleAlreadyActive()
        {
            // Covers handler branch: role exists and active (idempotent success).
            var sut = CreateSut();
            sut.UserRepository.FindUserResult = User.Rehydrate("U0000000004", "Cat", "0922000000", "pw");
            sut.RoleRepository.GetRoleResult = UserRole.Assign(
                "U0000000004",
                UserRoleCode.Instructor,
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                true);

            var command = new CreateInstructorCommand
            {
                Name = "Cat",
                Phone = "0922000000"
            };

            var result = await sut.Handler.Handle(command, CancellationToken.None);

            Assert.True(result);
            Assert.Equal(0, sut.RoleRepository.AddRoleCallCount);
            Assert.Equal(0, sut.RoleRepository.ReactiveCallCount);
            Assert.Equal(1, sut.UnitOfWork.CommitCount);
            Assert.Equal(0, sut.UnitOfWork.RollbackCount);
        }

        [Fact]
        public async Task Handle_ShouldReturnFalseAndRollback_WhenAddRoleFails()
        {
            // Covers handler branch: result == false, then rollback.
            var sut = CreateSut();
            sut.UserRepository.FindUserResult = User.Rehydrate("U0000000005", "Dan", "0933000000", "pw");
            sut.RoleRepository.GetRoleResult = null;
            sut.RoleRepository.AddRoleResult = false;

            var command = new CreateInstructorCommand
            {
                Name = "Dan",
                Phone = "0933000000"
            };

            var result = await sut.Handler.Handle(command, CancellationToken.None);

            Assert.False(result);
            Assert.Equal(0, sut.UnitOfWork.CommitCount);
            Assert.Equal(1, sut.UnitOfWork.RollbackCount);
        }

        [Fact]
        public async Task Handle_ShouldThrowAndRollback_WhenCreateUserReturnsEmptyId()
        {
            // Covers handler branch: created userId is empty => throw => catch rollback.
            var sut = CreateSut();
            sut.UserRepository.FindUserResult = null;
            sut.UserRepository.AddResultUserId = string.Empty;

            var command = new CreateInstructorCommand
            {
                Name = "Eve",
                Phone = "0944000000"
            };

            await Assert.ThrowsAsync<InvalidOperationException>(() => sut.Handler.Handle(command, CancellationToken.None));
            Assert.Equal(1, sut.UnitOfWork.RollbackCount);
            Assert.Equal(0, sut.UnitOfWork.CommitCount);
        }

        [Fact]
        public async Task Handle_ShouldThrowAndRollback_WhenRoleRepositoryThrows()
        {
            // Covers handler catch path: repository throws => rollback and rethrow.
            var sut = CreateSut();
            sut.UserRepository.FindUserResult = User.Rehydrate("U0000000006", "Fox", "0955000000", "pw");
            sut.RoleRepository.ThrowOnGetRole = true;

            var command = new CreateInstructorCommand
            {
                Name = "Fox",
                Phone = "0955000000"
            };

            await Assert.ThrowsAsync<Exception>(() => sut.Handler.Handle(command, CancellationToken.None));
            Assert.Equal(1, sut.UnitOfWork.BeginCount);
            Assert.Equal(1, sut.UnitOfWork.RollbackCount);
            Assert.Equal(0, sut.UnitOfWork.CommitCount);
        }

        private static SutBundle CreateSut()
        {
            var userRepository = new FakeUserRepository();
            var roleRepository = new FakeUserRoleRepository();
            var unitOfWork = new FakeUnitOfWork();
            var clock = new FakeClock();

            var handler = new CreateInstructorHandler(
                userRepository,
                roleRepository,
                clock,
                unitOfWork);

            return new SutBundle(handler, userRepository, roleRepository, unitOfWork);
        }

        private sealed record SutBundle(
            CreateInstructorHandler Handler,
            FakeUserRepository UserRepository,
            FakeUserRoleRepository RoleRepository,
            FakeUnitOfWork UnitOfWork);

        private sealed class FakeUserRepository : IUserRepository
        {
            public User? FindUserResult { get; set; }
            public string AddResultUserId { get; set; } = "U0000000001";
            public int AddCallCount { get; private set; }
            public string? LastFindPhone { get; private set; }
            public User? LastAddedUser { get; private set; }

            public Task<IReadOnlyList<string>> GetExistingPhonesAsync(IReadOnlyList<string> phones, CancellationToken ct)
            {
                return Task.FromResult<IReadOnlyList<string>>([]);
            }

            public Task<string> AddAsync(User user, CancellationToken ct)
            {
                AddCallCount++;
                LastAddedUser = user;
                return Task.FromResult(AddResultUserId);
            }

            public Task<User?> FindUserByPhone(string phone, CancellationToken ct)
            {
                LastFindPhone = phone;
                return Task.FromResult(FindUserResult);
            }
        }

        private sealed class FakeUserRoleRepository : IUserRoleRepository
        {
            public UserRole? GetRoleResult { get; set; }
            public bool AddRoleResult { get; set; } = true;
            public bool ReactiveResult { get; set; } = true;
            public bool ThrowOnGetRole { get; set; }
            public int AddRoleCallCount { get; private set; }
            public int ReactiveCallCount { get; private set; }
            public string? LastGetRoleUserId { get; private set; }
            public UserRole? LastAddedRole { get; private set; }
            public string? LastReactiveUserId { get; private set; }
            public UserRoleCode? LastReactiveRoleCode { get; private set; }

            public Task<UserRole?> GetUserRoleAsync(string userId, UserRoleCode roleType, CancellationToken ct)
            {
                if (ThrowOnGetRole)
                {
                    throw new Exception("get role failed");
                }

                LastGetRoleUserId = userId;
                return Task.FromResult(GetRoleResult);
            }

            public Task<bool> AddRoleAsync(UserRole userRole, CancellationToken ct)
            {
                AddRoleCallCount++;
                LastAddedRole = userRole;
                return Task.FromResult(AddRoleResult);
            }

            public Task<bool> ReactiveRole(string userId, UserRoleCode roleType, CancellationToken ct)
            {
                ReactiveCallCount++;
                LastReactiveUserId = userId;
                LastReactiveRoleCode = roleType;
                return Task.FromResult(ReactiveResult);
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

        private sealed class FakeClock : IClock
        {
            public DateTime Now() => new(2026, 5, 3, 10, 0, 0, DateTimeKind.Utc);
            public DateOnly Today() => new(2026, 5, 3);
        }
    }
}
