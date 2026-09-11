using gym_system.Application.MembersUseCase.Commands.RegisterMember;
using gym_system.Application.OrdersUseCase.Services;
using gym_system.Application.TicketPlansUseCase.Queries;
using gym_system.Domain.Entities.Members;
using gym_system.Domain.Entities.Orders;
using gym_system.Domain.Entities.Tickets;
using gym_system.Domain.Entities.Users;
using gym_system.Domain.Enums;
using gym_system.Domain.Exceptions;
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
                    TicketPlanKindId = "PACK_10",
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
        public async Task Handle_ShouldRejectFamilyTicketPurchase_WhenMembersCountGreaterThanOne()
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
                    TicketPlanKindId = "PACK_10",
                    PaymentStatus = PaymentState.Paid
                }
            };

            var error = await Assert.ThrowsAsync<TicketPurchaseRejectedException>(
                () => sut.Handler.Handle(command));

            Assert.Equal("FAMILY_PURCHASE_NOT_AVAILABLE", error.Code);
            Assert.Equal("家庭購票功能尚未開放", error.Message);
            Assert.Empty(sut.PassRepository.StoredPasses);
            Assert.Equal(1, sut.UnitOfWork.RollbackCount);
        }

        [Fact]
        public async Task Handle_ShouldCreateUnpaidOrderWithoutPass()
        {
            var sut = CreateSut();
            var command = new RegisterMembersCommand
            {
                Members = [new MemberRegisterInput { Name = "A", Phone = "0912000001" }],
                TicketPurchase = new TicketPurchaseInput
                {
                    TicketPlanKindId = "PACK_10",
                    PaymentStatus = PaymentState.UnPaid
                }
            };

            await sut.Handler.Handle(command);

            var order = Assert.Single(sut.OrderRepository.StoredOrders);
            var item = Assert.Single(order.Items);
            Assert.Equal(OrderItemPaymentState.UnPaid, item.PaymentState);
            Assert.Null(item.PaidAt);
            Assert.Empty(sut.PassRepository.StoredPasses);
            Assert.Empty(sut.ProfileRepository.UpdatedSnapshots);
        }

        [Fact]
        public async Task Handle_ShouldRejectNewOnlyTicketPurchase_InRegistrationContext()
        {
            var sut = CreateSut();
            var command = new RegisterMembersCommand
            {
                Members = [new MemberRegisterInput { Name = "A", Phone = "0912000001" }],
                TicketPurchase = new TicketPurchaseInput
                {
                    TicketPlanKindId = "NEW_ONLY",
                    PaymentStatus = PaymentState.UnPaid
                }
            };

            var error = await Assert.ThrowsAsync<TicketPurchaseRejectedException>(
                () => sut.Handler.Handle(command));

            Assert.Equal("TICKET_PLAN_NOT_AVAILABLE", error.Code);
            Assert.Empty(sut.OrderRepository.StoredOrders);
            Assert.Empty(sut.PassRepository.StoredPasses);
            Assert.Equal(1, sut.UnitOfWork.RollbackCount);
        }

        [Fact]
        public async Task Handle_ShouldIssueFiveSinglePasses_WhenQuantityIsFive()
        {
            var sut = CreateSut();
            var command = new RegisterMembersCommand
            {
                Members = [new MemberRegisterInput { Name = "A", Phone = "0912000001" }],
                TicketPurchase = new TicketPurchaseInput
                {
                    TicketPlanKindId = "SINGLE",
                    Quantity = 5,
                    PaymentStatus = PaymentState.Paid
                }
            };

            var result = await sut.Handler.Handle(command);

            Assert.Equal(1250m, result.TotalAmount);
            var item = Assert.Single(Assert.Single(sut.OrderRepository.StoredOrders).Items);
            Assert.Equal(5, item.Quantity);
            Assert.Equal(5, sut.PassRepository.StoredPasses.Count);
            Assert.Single(sut.PassRepository.StoredPasses, x => x.ValidStatus == TicketValidStatus.Active);
            Assert.Equal(4, sut.PassRepository.StoredPasses.Count(x => x.ValidStatus == TicketValidStatus.UnActive));
        }

        [Fact]
        public async Task Handle_ShouldRejectSingleQuantityGreaterThanFive()
        {
            var sut = CreateSut();
            var command = new RegisterMembersCommand
            {
                Members = [new MemberRegisterInput { Name = "A", Phone = "0912000001" }],
                TicketPurchase = new TicketPurchaseInput
                {
                    TicketPlanKindId = "SINGLE",
                    Quantity = 6,
                    PaymentStatus = PaymentState.Paid
                }
            };

            await Assert.ThrowsAsync<InvalidOperationException>(() => sut.Handler.Handle(command));

            Assert.Empty(sut.OrderRepository.StoredOrders);
            Assert.Empty(sut.PassRepository.StoredPasses);
            Assert.Equal(1, sut.UnitOfWork.RollbackCount);
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
                    PaymentStatus = PaymentState.UnPaid
                }
            };

            await Assert.ThrowsAsync<KeyNotFoundException>(() => sut.Handler.Handle(command));
            Assert.Equal(1, sut.UnitOfWork.RollbackCount);
        }

        [Fact]
        public async Task Handle_ShouldActivatePaidTicketImmediately_WhenNoCurrentTicket()
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
                    TicketPlanKindId = "PACK_10",
                    PaymentStatus = PaymentState.Paid
                }
            };

            await sut.Handler.Handle(command);

            var snapshot = Assert.Single(sut.ProfileRepository.UpdatedSnapshots);
            Assert.Equal("Active", snapshot.TicketValidState);
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
                    TicketPlanKindId = "PACK_10",
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
                    TicketPlanKindId = "PACK_10",
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

            var eligibilityService = new TicketPlanEligibilityService(
                profileRepository,
                roleRepository,
                clock,
                []);
            var purchaseService = new TicketPurchaseService(
                userRepository,
                roleRepository,
                profileRepository,
                ticketPlanRepository,
                new FakeTicketPlanCatalogQueryService(),
                eligibilityService,
                new RenewalTicketPassEligibilityService(passRepository),
                orderRepository,
                passRepository,
                clock);
            var handler = new RegisterMemberHandler(
                userRepository,
                roleRepository,
                profileRepository,
                purchaseService,
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
                var userId = $"U{_nextId++:0000000000}";
                StoredUsers.Add(User.Rehydrate(
                    userId,
                    user.Name,
                    user.Phone,
                    user.Password,
                    user.IsActive));
                ExistingPhones.Add(user.Phone);
                return Task.FromResult(userId);
            }

            public Task<User?> FindUserByIdAsync(string userId, CancellationToken ct)
            {
                return Task.FromResult(StoredUsers.FirstOrDefault(x => x.Id == userId));
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
            public Task<bool> ClearCurrentTicketAsync(string userId, DateTime updatedAt, CancellationToken ct)
            {
                var profile = StoredProfiles.FirstOrDefault(x => x.UserId == userId);
                if (profile is null)
                {
                    return Task.FromResult(false);
                }

                profile.ClearCurrentTicket();
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

                var isSingle = ticketPlanKindId.Equals("SINGLE", StringComparison.OrdinalIgnoreCase);
                var plan = new TicketPlanKind
                {
                    Id = ticketPlanKindId,
                    Name = isSingle ? "Single" : "Pack 10",
                    Type = TicketPlanType.Pack,
                    Price = isSingle ? 250m : 2300m,
                    DefaultCredit = isSingle ? 1 : 10,
                    DefaultExpireDays = isSingle ? null : 90,
                    IsActive = true
                };

                return Task.FromResult<TicketPlanKind?>(plan);
            }
        }

        private sealed class FakeTicketPlanCatalogQueryService : ITicketPlanCatalogQueryService
        {
            public Task<IReadOnlyList<TicketPlanResult>> GetActiveTicketPlansAsync(CancellationToken ct)
            {
                IReadOnlyList<TicketPlanResult> plans =
                [
                    new TicketPlanResult
                    {
                        Id = "PACK_10",
                        Type = "PACK",
                        Name = "Pack 10",
                        Price = 2300m,
                        Days = 90,
                        Sessions = 10
                    },
                    new TicketPlanResult
                    {
                        Id = "SINGLE",
                        Type = "PACK",
                        Name = "Single",
                        Price = 250m,
                        Days = 0,
                        Sessions = 1
                    },
                    new TicketPlanResult
                    {
                        Id = "NEW_ONLY",
                        Type = "PACK",
                        Name = "New Only",
                        Price = 2300m,
                        Days = 90,
                        Sessions = 10,
                        Tags = ["NEW_ONLY"],
                        EligibilityRuleCodes = ["NEW_ONLY"]
                    }
                ];
                return Task.FromResult(plans);
            }
        }

        private sealed class FakeOrderRepository : IOrderRepository
        {
            public bool ThrowOnAdd { get; set; }
            public List<Order> StoredOrders { get; } = [];

            public Task<OrderPersistenceResult> AddAsync(Order order, CancellationToken ct)
            {
                if (ThrowOnAdd)
                {
                    throw new Exception("order add failed");
                }

                StoredOrders.Add(order);
                return Task.FromResult(new OrderPersistenceResult
                {
                    OrderSn = StoredOrders.Count,
                    OrderId = order.Id,
                    Items = order.Items.Select((item, index) => new OrderItemPersistenceResult
                    {
                        ClientItemId = item.Id,
                        OrderItemSn = index + 1,
                        OrderItemId = item.Id
                    }).ToList()
                });
            }

            public Task<UnpaidTicketOrder?> FindUnpaidTicketOrderAsync(
                string orderId,
                bool acquireLock,
                CancellationToken ct) => throw new NotSupportedException();

            public Task MarkPaidAsync(
                int orderSn,
                int orderItemSn,
                DateTime paidAt,
                string paymentMethod,
                string operatorId,
                CancellationToken ct) => throw new NotSupportedException();
        }

        private sealed class FakeTicketPassRepository : ITicketPassRepository
        {
            public List<TicketPass> StoredPasses { get; } = [];

            public Task<IReadOnlyList<TicketPassPersistenceResult>> AddRangeAsync(
                IReadOnlyList<TicketPass> passes,
                OrderPersistenceResult orderPersistence,
                CancellationToken ct)
            {
                StoredPasses.AddRange(passes);
                IReadOnlyList<TicketPassPersistenceResult> result = passes.Select((pass, index) => new TicketPassPersistenceResult
                {
                    ClientPassId = pass.Id,
                    OwnerId = pass.OwnerId,
                    PassSn = index + 1,
                    PassId = pass.Id
                }).ToList();
                return Task.FromResult(result);
            }

            public Task<TicketPass?> FindLatestMonthlyPassAsync(string ownerId, CancellationToken ct)
            {
                return Task.FromResult<TicketPass?>(null);
            }

            public Task<RenewalSourcePass?> FindLatestRenewalSourceAsync(
                string ownerId,
                string familyCode,
                bool acquireLock,
                CancellationToken ct) => throw new NotSupportedException();

            public Task<bool> HasQueuedPassAsync(string ownerId, CancellationToken ct) =>
                throw new NotSupportedException();

            public Task LockOwnerAsync(string ownerId, CancellationToken ct) =>
                Task.CompletedTask;

            public Task<CancelledTicketPassResult?> CancelQueuedRenewalAsync(
                string passId,
                DateTime cancelledAt,
                string operatorId,
                CancellationToken ct) => throw new NotSupportedException();

            public Task<CurrentTicketSnapshot?> ReconcileCurrentAsync(
                string ownerId,
                DateOnly today,
                DateTime updatedAt,
                string operatorId,
                CancellationToken ct)
            {
                var active = StoredPasses
                    .Where(x => x.OwnerId == ownerId && x.ValidStatus == TicketValidStatus.Active)
                    .ToList();
                if (active.Count == 0)
                {
                    var next = StoredPasses.FirstOrDefault(
                        x => x.OwnerId == ownerId && x.ValidStatus == TicketValidStatus.UnActive);
                    next?.Activate(today);
                    active = next is null ? [] : [next];
                }

                return Task.FromResult(active.SingleOrDefault()?.ToSnapshot());
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
