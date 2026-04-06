using gym_system.Application.MembersUseCase.Commands.RegisterMember;
using gym_system.Domain.Entities.Members;
using gym_system.Domain.Entities.Orders;
using gym_system.Domain.Entities.Tickets;
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
            sut.MemberRepository.ExistingPhones.Add("0912345678");

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

            Assert.Single(sut.MemberRepository.StoredMembers);
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

        private static SutBundle CreateSut()
        {
            var memberRepository = new FakeMemberRepository();
            var profileRepository = new FakeStudentProfileRepository();
            var ticketPlanRepository = new FakeTicketPlanRepository();
            var orderRepository = new FakeOrderRepository();
            var passRepository = new FakeTicketPassRepository();
            var unitOfWork = new FakeUnitOfWork();
            var clock = new FakeClock();

            var handler = new RegisterMemberHandler(
                memberRepository,
                profileRepository,
                ticketPlanRepository,
                orderRepository,
                passRepository,
                unitOfWork,
                clock);

            return new SutBundle(
                handler,
                memberRepository,
                profileRepository,
                ticketPlanRepository,
                orderRepository,
                passRepository,
                unitOfWork);
        }

        private sealed record SutBundle(
            RegisterMemberHandler Handler,
            FakeMemberRepository MemberRepository,
            FakeStudentProfileRepository ProfileRepository,
            FakeTicketPlanRepository TicketPlanRepository,
            FakeOrderRepository OrderRepository,
            FakeTicketPassRepository PassRepository,
            FakeUnitOfWork UnitOfWork);

        private sealed class FakeMemberRepository : IMemberRepository
        {
            public List<Member> StoredMembers { get; } = [];
            public HashSet<string> ExistingPhones { get; } = [];

            public Task<bool> AnyPhoneExistsAsync(IReadOnlyList<string> phones, CancellationToken ct)
            {
                return Task.FromResult(phones.Any(p => ExistingPhones.Contains(p)));
            }

            public Task AddRangeAsync(IReadOnlyList<Member> members, CancellationToken ct)
            {
                StoredMembers.AddRange(members);
                foreach (var member in members)
                {
                    ExistingPhones.Add(member.Phone);
                }

                return Task.CompletedTask;
            }

            public Task<List<string>> GenerateIdsAsync(int count, CancellationToken ct)
            {
                var ids = Enumerable.Range(1, count).Select(x => $"C{x:000000}").ToList();
                return Task.FromResult(ids);
            }
        }

        private sealed class FakeStudentProfileRepository : IStudentProfileRepository
        {
            public List<StudentProfile> StoredProfiles { get; } = [];
            public List<CurrentTicketSnapshot> UpdatedSnapshots { get; } = [];

            public Task AddRangeAsync(IReadOnlyList<StudentProfile> profiles, CancellationToken ct)
            {
                StoredProfiles.AddRange(profiles);
                return Task.CompletedTask;
            }

            public Task UpdateCurrentTicketAsync(string userId, CurrentTicketSnapshot snapshot, CancellationToken ct)
            {
                UpdatedSnapshots.Add(snapshot);
                return Task.CompletedTask;
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
