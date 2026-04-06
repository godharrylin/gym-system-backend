using gym_system.Application.TicketPlansUseCase.Queries;
using Xunit;

namespace gym_system.Application.Tests
{
    public sealed class GetActiveTicketPlansHandlerTests
    {
        [Fact]
        public async Task ShouldReturnPlans_FromQueryService()
        {
            var fake = new FakeTicketPlanCatalogQuerySerivce
            {
                Result =
                [
                    new TicketPlanResult
                    {
                        Id = "T_001",
                        Name = "Single",
                        Price = 250,
                        Days = 1,
                        Sessions = "1",
                        Type = "SESSION",
                        Tags = [],
                        Description = "任何人皆可購買"
                    }
                ]
            };

            var sut = new GetActiveTicketPlansHandler(fake);

            var actual = await sut.Handle();
            var plan = Assert.Single(actual);
            Assert.Equal("T_001", plan.Id);
            Assert.Equal(1, fake.CallCount);
        }

        [Fact]
        public async Task Handle_ShouldReturnEmpty_WhenNoPlans()
        {
            var fake = new FakeTicketPlanCatalogQuerySerivce { Result = [] };
            var sut = new GetActiveTicketPlansHandler(fake);

            var actual = await sut.Handle();

            Assert.Empty(actual);
        }

        [Fact]
        public async Task Handle_ShouldPassCancellationToken_ToQueryService()
        {
            var fake = new FakeTicketPlanCatalogQuerySerivce { Result = [] };
            var sut = new GetActiveTicketPlansHandler(fake);
            using var cts = new CancellationTokenSource();

            await sut.Handle(cts.Token);

            Assert.Equal(cts.Token, fake.LastToken);
        }

        [Fact]
        public async Task Handle_ShouldThrow_WhenQueryServiceThrows()
        {
            var fake = new FakeTicketPlanCatalogQuerySerivce
            {
                ExceptionToThrow = new InvalidOperationException("query failed")
            };
            var sut = new GetActiveTicketPlansHandler(fake);

            await Assert.ThrowsAsync<InvalidOperationException>(() => sut.Handle());
        }
        private sealed class FakeTicketPlanCatalogQuerySerivce: ITicketPlanCatalogQuerySerivce
        {
            public IReadOnlyList<TicketPlanResult> Result { get; set; } = [];
            public Exception? ExceptionToThrow { get; set; }
            public int CallCount { get; private set; }
            public CancellationToken LastToken { get; private set; }
            public Task<IReadOnlyList<TicketPlanResult>> GetActiveTicketPlansAsync(CancellationToken ct)
            {
                CallCount++;
                LastToken = ct;

                if (ExceptionToThrow is not null)
                {
                    throw ExceptionToThrow;
                }

                return Task.FromResult(Result);
            }
        }
    }
}
