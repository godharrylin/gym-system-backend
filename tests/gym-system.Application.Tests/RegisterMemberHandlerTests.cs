using gym_system.Application.MembersUseCase.Commands.RegisterMember;
using gym_system.Domain.Entities.Members;
using gym_system.Domain.Entities.Orders;
using gym_system.Domain.Entities.Tickets;
using gym_system.Domain.Entities.Users;
using gym_system.Domain.Enums;
using gym_system.Domain.Repositories;
using Xunit;

namespace gym_system.Application.Tests
{
    public sealed class RegisterMemberHandlerTests
    {
        [Fact]
        public async Task Handle_ShouldThrow_WhenMembersIsEmpty()
        {
            var sut = CreateSut();
            var command = new RegisterMembersCommand { Members = [] };

            await Assert.ThrowsAsync<InvalidOperationException>(() => sut.Handler.Handle(command));
            Assert.Equal(0, sut.UnitOfWork.BeginCount);
        }

        [Fact]
        public async Task Handle_ShouldThrow_WhenRequestContainsDuplicatePhones()
        {
            var sut = CreateSut();
            var command = new RegisterMembersCommand
            {
                Members =
                [
                    new MemberRegisterInput { Name = "A", Phone = "0912" },
                    new MemberRegisterInput { Name = "B", Phone = "0912" }
                ]
            };

            await Assert.ThrowsAsync<InvalidOperationException>(() => sut.Handler.Handle(command));
            Assert.Equal(0, sut.UnitOfWork.BeginCount);
        }

        [Fact]
        public async Task Handle_ShouldThrow_WhenPhoneAlreadyExistsInStorage()
        {
            var sut = CreateSut();
            sut.UserRepository.ExistingPhones.Add("0912345678");

            var command = new RegisterMembersCommand
            {
                Members =
                [
                    new MemberRegisterInput { Name = "王小明", Phone = "0912345678" }
                ]
            };

            await Assert.ThrowsAsync<InvalidOperationException>(() => sut.Handler.Handle(command));
            Assert.Equal(1, sut.UnitOfWork.BeginCount);
            Assert.Equal(1, sut.UnitOfWork.RollbackCount);
        }

        [Fact]
        public async Task Handle_ShouldRegisterWithoutOrder_WhenTicketPurchaseIsNull()
        {
            var sut = CreateSut();
            var command = new RegisterMembersCommand
            {
                Members =
                [
                    new MemberRegisterInput { Name = "王小明", Phone = "0912345678" }
                ]
            };

            var result = await sut.Handler.Handle(command);

            Assert.Single(result.MemberIds);
            Assert.Null(result.OrderId);
            Assert.Null(result.TotalAmount);
            Assert.Null(result.ActualAmount);

            Assert.Single(sut.UserRepository.StoredUsers);
            var role = Assert.Single(sut.RoleRepository.StoredRoles);
            Assert.Equal(UserRoleCode.Student, role.RoleCode);
            Assert.Single(sut.ProfileRepository.StoredProfiles);
            Assert.Empty(sut.OrderRepository.StoredOrders);
            Assert.Empty(sut.PassRepository.StoredPasses);
            Assert.Equal(1, sut.UnitOfWork.CommitCount);
        }

        [Fact]
        public async Task Handle_ShouldRegisterAndIssueTicket_WhenTicketPurchaseProvided()
        {
            var sut = CreateSut();
            var command = new RegisterMembersCommand
            {
                Members =
                [
                    new MemberRegisterInput { Name = "王小明", Phone = "0912000001" }
                ],
                TicketPurchase = new TicketPurchaseInput
                {
                    TicketPlanKindId = "T_002",
                    ActivationDate = new DateOnly(2026, 3, 30),
                    PaymentStatus = PaymentState.Paid
                }
            };

            var result = await sut.Handler.Handle(command);

            Assert.NotNull(result.OrderId);
            Assert.Equal(2300m, result.TotalAmount);
            Assert.Equal(2300m, result.ActualAmount);
            Assert.Single(sut.OrderRepository.StoredOrders);
            Assert.Single(sut.PassRepository.StoredPasses);
            Assert.Single(sut.ProfileRepository.UpdatedSnapshots);
        }

        [Fact]
        public async Task Handle_ShouldApplyFamilyDiscount_WhenMembersCountGreaterThanOrEqualToTwo()
        {
            var sut = CreateSut();
            var command = new RegisterMembersCommand
            {
                Members =
                [
                    new MemberRegisterInput { Name = "A", Phone = "0912000001" },
                    new MemberRegisterInput { Name = "B", Phone = "0912000002" }
                ],
                TicketPurchase = new TicketPurchaseInput
                {
                    TicketPlanKindId = "T_002",
                    ActivationDate = new DateOnly(2026, 3, 30),
                    PaymentStatus = PaymentState.Paid
                }
            };

            var result = await sut.Handler.Handle(command);

            Assert.Equal(4600m, result.TotalAmount);
            Assert.Equal(4370m, result.ActualAmount);
            Assert.Equal(2, sut.PassRepository.StoredPasses.Count);
        }

        [Fact]
        public async Task Handle_ShouldThrow_WhenTicketPlanNotFound()
        {
            var sut = CreateSut();
            sut.TicketPlanRepository.ReturnNull = true;

            var command = new RegisterMembersCommand
            {
                Members =
                [
                    new MemberRegisterInput { Name = "A", Phone = "0912000001" }
                ],
                TicketPurchase = new TicketPurchaseInput
                {
                    TicketPlanKindId = "UNKNOWN",
                    ActivationDate = new DateOnly(2026, 3, 30),
                    PaymentStatus = PaymentState.UnPaid
                }
            };

            await Assert.ThrowsAsync<InvalidOperationException>(() => sut.Handler.Handle(command));
            Assert.Equal(1, sut.UnitOfWork.RollbackCount);
        }

        [Fact]
        public async Task Handle_ShouldMarkTicketAsUnActive_WhenActivationDateInFuture()
        {
            var sut = CreateSut();
            var command = new RegisterMembersCommand
            {
                Members =
                [
                    new MemberRegisterInput { Name = "A", Phone = "0912000001" }
                ],
                TicketPurchase = new TicketPurchaseInput
                {
                    TicketPlanKindId = "T_002",
                    ActivationDate = new DateOnly(2026, 4, 1),
                    PaymentStatus = PaymentState.Paid
                }
            };

            await sut.Handler.Handle(command);

            var snapshot = Assert.Single(sut.ProfileRepository.UpdatedSnapshots);
            Assert.Equal("UnActive", snapshot.TicketValidState);
        }

        [Fact]
        public async Task Handle_ShouldRollback_WhenRepositoryThrows()
        {
            var sut = CreateSut();
            sut.OrderRepository.ThrowOnAdd = true;

            var command = new RegisterMembersCommand
            {
                Members =
                [
                    new MemberRegisterInput { Name = "A", Phone = "0912000001" }
                ],
                TicketPurchase = new TicketPurchaseInput
                {
                    TicketPlanKindId = "T_002",
                    ActivationDate = new DateOnly(2026, 3, 30),
                    PaymentStatus = PaymentState.Paid
                }
            };

            await Assert.ThrowsAsync<Exception>(() => sut.Handler.Handle(command));
            Assert.Equal(1, sut.UnitOfWork.BeginCount);
            Assert.Equal(1, sut.UnitOfWork.RollbackCount);
            Assert.Equal(0, sut.UnitOfWork.CommitCount);
        }

        [Fact]
        public async Task Handle_ShouldRollback_WhenStudentRoleCannotBeCreated()
        {
            var sut = CreateSut();
            sut.RoleRepository.AddRoleResult = false;
            var command = new RegisterMembersCommand
            {
                Members = [new MemberRegisterInput { Name = "A", Phone = "0912000001" }]
            };

            await Assert.ThrowsAsync<InvalidOperationException>(() => sut.Handler.Handle(command));

            Assert.Equal(1, sut.UnitOfWork.RollbackCount);
            Assert.Empty(sut.ProfileRepository.StoredProfiles);
        }

        [Fact]
        public async Task Handle_ShouldRollback_WhenStudentProfileCannotBeCreated()
        {
            var sut = CreateSut();
            sut.ProfileRepository.ThrowOnAdd = true;
            var command = new RegisterMembersCommand
            {
                Members = [new MemberRegisterInput { Name = "A", Phone = "0912000001" }]
            };

            await Assert.ThrowsAsync<InvalidOperationException>(() => sut.Handler.Handle(command));

            Assert.Equal(1, sut.UnitOfWork.RollbackCount);
            Assert.Equal(0, sut.UnitOfWork.CommitCount);
        }

        [Fact]
        public async Task Handle_ShouldRollback_WhenTicketSnapshotCannotBeUpdated()
        {
            var sut = CreateSut();
            sut.ProfileRepository.UpdateCurrentTicketResult = false;
            var command = new RegisterMembersCommand
            {
                Members = [new MemberRegisterInput { Name = "A", Phone = "0912000001" }],
                TicketPurchase = new TicketPurchaseInput
                {
                    TicketPlanKindId = "T_002",
                    ActivationDate = new DateOnly(2026, 3, 30),
                    PaymentStatus = PaymentState.Paid
                }
            };

            await Assert.ThrowsAsync<InvalidOperationException>(() => sut.Handler.Handle(command));

            Assert.Equal(1, sut.UnitOfWork.RollbackCount);
            Assert.Equal(0, sut.UnitOfWork.CommitCount);
        }

        private static SutBundle CreateSut()
        {
            var userRepository = new FakeUserRepository();
            var roleRepository = new FakeUserRoleRepository();
            var profileRepository = new FakeStudentProfileRepository();
            var ticketPlanRepository = new FakeTicketPlanRepository();
            var orderRepository = new FakeOrderRepository();
            var passRepository = new FakeTicketPassRepository();
            var unitOfWork = new FakeUnitOfWork();
            var clock = new FakeClock();

            var handler = new RegisterMemberHandler(
                userRepository,
                roleRepository,
                profileRepository,
                ticketPlanRepository,
                orderRepository,
                passRepository,
                unitOfWork,
                clock);

            return new SutBundle(
                handler,
                userRepository,
                roleRepository,
                profileRepository,
                ticketPlanRepository,
                orderRepository,
                passRepository,
                unitOfWork);
        }

        private sealed record SutBundle(
            RegisterMemberHandler Handler,
            FakeUserRepository UserRepository,
            FakeUserRoleRepository RoleRepository,
            FakeStudentProfileRepository ProfileRepository,
            FakeTicketPlanRepository TicketPlanRepository,
            FakeOrderRepository OrderRepository,
            FakeTicketPassRepository PassRepository,
            FakeUnitOfWork UnitOfWork);

        private sealed class FakeUserRepository : IUserRepository
        {
            public List<User> StoredUsers { get; } = [];
            public HashSet<string> ExistingPhones { get; } = [];
            private int _nextId = 1;

            public Task<IReadOnlyList<string>> GetExistingPhonesAsync(IReadOnlyList<string> phones, CancellationToken ct)
            {
                IReadOnlyList<string> result = phones.Where(ExistingPhones.Contains).ToList();
                return Task.FromResult(result);
            }

            public Task<string> AddAsync(User user, CancellationToken ct)
            {
                StoredUsers.Add(user);
                ExistingPhones.Add(user.Phone);
                return Task.FromResult($"U{_nextId++:0000000000}");
            }

            public Task<User?> FindUserByIdAsync(string userId, CancellationToken ct)
            {
                return Task.FromResult<User?>(null);
            }

            public Task<User?> FindUserByPhoneAsync(string phone, CancellationToken ct)
            {
                return Task.FromResult(StoredUsers.FirstOrDefault(x => x.Phone == phone));
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
            public List<UserRole> StoredRoles { get; } = [];
            public bool AddRoleResult { get; set; } = true;

            public Task<UserRole?> GetUserRoleAsync(string userId, UserRoleCode roleType, CancellationToken ct)
            {
                return Task.FromResult(StoredRoles.FirstOrDefault(x => x.UserId == userId && x.RoleCode == roleType));
            }

            public Task<IReadOnlyList<UserRole>> GetActiveRolesAsync(string userId, CancellationToken ct)
            {
                IReadOnlyList<UserRole> result = StoredRoles.Where(x => x.UserId == userId && x.IsActive).ToList();
                return Task.FromResult(result);
            }

            public Task<bool> AddRoleAsync(UserRole userRole, CancellationToken ct)
            {
                if (AddRoleResult)
                {
                    StoredRoles.Add(userRole);
                }

                return Task.FromResult(AddRoleResult);
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

        private sealed class FakeStudentProfileRepository : IStudentProfileRepository
        {
            public List<StudentProfile> StoredProfiles { get; } = [];
            public List<CurrentTicketSnapshot> UpdatedSnapshots { get; } = [];
            public bool ThrowOnAdd { get; set; }
            public bool UpdateCurrentTicketResult { get; set; } = true;

            public Task AddAsync(StudentProfile profile, CancellationToken ct)
            {
                if (ThrowOnAdd)
                {
                    throw new InvalidOperationException("profile add failed");
                }

                StoredProfiles.Add(profile);
                return Task.CompletedTask;
            }

            public Task AddRangeAsync(IReadOnlyList<StudentProfile> profiles, CancellationToken ct)
            {
                StoredProfiles.AddRange(profiles);
                return Task.CompletedTask;
            }

            public Task<StudentProfile?> FindByUserIdAsync(string userId, CancellationToken ct)
            {
                var profile = StoredProfiles.FirstOrDefault(x => x.UserId == userId);
                return Task.FromResult(profile);
            }

            public Task<bool> UpdateLastVisitAsync(string userId, DateTime lastVisitAt, CancellationToken ct)
            {
                var profile = StoredProfiles.FirstOrDefault(x => x.UserId == userId);
                if (profile is null)
                {
                    return Task.FromResult(false);
                }

                profile.RecordVisit(lastVisitAt);
                return Task.FromResult(true);
            }

            public Task<bool> UpdateCurrentTicketAsync(string userId, CurrentTicketSnapshot snapshot, CancellationToken ct)
            {
                if (!UpdateCurrentTicketResult)
                {
                    return Task.FromResult(false);
                }

                var profile = StoredProfiles.FirstOrDefault(x => x.UserId == userId);
                if (profile is null)
                {
                    return Task.FromResult(false);
                }

                profile.UpdateCurrentTicket(snapshot);
                UpdatedSnapshots.Add(snapshot);
                return Task.FromResult(true);
            }
        }

        private sealed class FakeTicketPlanRepository : ITicketPlanRepository
        {
            public bool ReturnNull { get; set; }

            public Task<TicketPlanKind?> GetActiveByIdAsync(string ticketPlanKindId, CancellationToken ct)
            {
                if (ReturnNull)
                {
                    return Task.FromResult<TicketPlanKind?>(null);
                }

                var plan = new TicketPlanKind
                {
                    Id = ticketPlanKindId,
                    Name = "Pack 10",
                    Type = TicketPlanType.Pack,
                    Price = 2300m,
                    DefaultCredit = 10,
                    DefaultExpireDays = 90,
                    IsActive = true
                };

                return Task.FromResult<TicketPlanKind?>(plan);
            }
        }

        private sealed class FakeOrderRepository : IOrderRepository
        {
            public bool ThrowOnAdd { get; set; }
            public List<Order> StoredOrders { get; } = [];

            public Task AddAsync(Order order, CancellationToken ct)
            {
                if (ThrowOnAdd)
                {
                    throw new Exception("order add failed");
                }

                StoredOrders.Add(order);
                return Task.CompletedTask;
            }
        }

        private sealed class FakeTicketPassRepository : ITicketPassRepository
        {
            public List<TicketPass> StoredPasses { get; } = [];

            public Task AddRangeAsync(IReadOnlyList<TicketPass> passes, CancellationToken ct)
            {
                StoredPasses.AddRange(passes);
                return Task.CompletedTask;
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
            public DateTime Now() => new(2026, 3, 30, 10, 0, 0, DateTimeKind.Utc);
            public DateOnly Today() => new(2026, 3, 30);
        }
    }
}
