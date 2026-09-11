using gym_system.Domain.Entities.Orders;
using gym_system.Domain.Entities.Tickets;
using gym_system.Domain.Enums;
using gym_system.Domain.Repositories;
using gym_system.Infrastructures;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace gym_system.Api.Tests;

// Exercises the actual in-memory repository through DI, not a test double.
public sealed class InMemoryTicketPassReconcileTests
{
    private static readonly DateOnly Today = new(2026, 9, 7);
    private static readonly DateTime Now = new(2026, 9, 7, 10, 0, 0);

    [Fact] // S09: the just-ended source's direct renewal precedes older unrelated renewals.
    public async Task Reconcile_ShouldPrioritizeDirectSuccessorOfJustEndedSource()
    {
        using var fixture = new Fixture();
        var old = Source("old", TicketValidStatus.Expire, Today.AddDays(-2));
        var current = Source("current", TicketValidStatus.Active, Today.AddDays(-1));
        var unrelated = Queue("unrelated", source: 1, paidAt: Now.AddDays(-10));
        var direct = Queue("direct", source: 2, paidAt: Now.AddDays(-2));
        await fixture.Seed(old, current, unrelated, direct);

        var snapshot = await fixture.Reconcile();

        Assert.Equal("direct", snapshot!.TicketId);
        Assert.Equal(TicketValidStatus.Expire, current.ValidStatus);
        Assert.Equal(TicketValidStatus.UnActive, unrelated.ValidStatus);
        Assert.Equal("direct", (await fixture.Reconcile())!.TicketId);
    }

    [Fact] // S01/S02/S04/S11: preserve single credits, payment and FIFO when yielding/resuming.
    public async Task Single_ShouldYieldWithoutLosingCreditsAndResumeAtOriginalPosition()
    {
        using var fixture = new Fixture();
        var single = Queue("single", code: "SINGLE", paidAt: Now.AddDays(-5));
        single.Activate(Today.AddDays(-5));
        single.UseCredit(Now.AddDays(-1));
        var otherSingle = Queue("other-single", code: "SINGLE", paidAt: Now.AddDays(-4));
        var monthly = Queue("monthly");
        await fixture.Seed(single, otherSingle, monthly);

        Assert.Equal("monthly", (await fixture.Reconcile())!.TicketId);
        Assert.Equal(TicketValidStatus.UnActive, single.ValidStatus);
        Assert.Equal(2, single.CreditsRemaining);
        Assert.Equal(Now.AddDays(-5), single.PaidAt);
        Assert.Null(single.ValidStartDate);
        Assert.Null(single.ValidEndDate);
        Assert.Null(single.EndedAt);
        Assert.Null(single.EndReason);
        Assert.Equal("monthly", (await fixture.Reconcile())!.TicketId);
        Assert.Equal("single", (await fixture.Reconcile(Today.AddDays(30)))!.TicketId);
        Assert.Equal(2, single.CreditsRemaining);
        Assert.Equal(TicketValidStatus.UnActive, otherSingle.ValidStatus);
    }

    [Fact] // S03/S10: paid non-single tickets queue globally; next non-single precedes singles.
    public async Task ActiveNonSingle_ShouldNotBePreemptedByOtherFamily()
    {
        using var fixture = new Fixture();
        var current = Queue("current");
        current.Activate(Today);
        var otherFamily = Queue("other-family", code: "YEAR", family: "YEAR");
        var single = Queue("single", code: "SINGLE", paidAt: Now.AddDays(-5));
        await fixture.Seed(current, otherFamily, single);

        Assert.Equal("current", (await fixture.Reconcile())!.TicketId);
        Assert.Equal(TicketValidStatus.UnActive, otherFamily.ValidStatus);
        Assert.Equal("other-family", (await fixture.Reconcile(Today.AddDays(30)))!.TicketId);
        Assert.Equal(TicketValidStatus.UnActive, single.ValidStatus);
    }

    [Theory] // S05: a future renewal cannot block the single or a later usable standard ticket.
    [InlineData(false)]
    [InlineData(true)]
    public async Task FutureRenewal_ShouldNotBlockAvailableTicket(bool addStandard)
    {
        using var fixture = new Fixture();
        var source = Source("source", TicketValidStatus.Depleted, Today);
        var future = Queue("future", source: 1, paidAt: Now.AddDays(-1));
        var single = Queue("single", code: "SINGLE");
        single.Activate(Today);
        var standard = Queue("standard");
        await fixture.Seed(addStandard ? [source, future, single, standard] : [source, future, single]);

        Assert.Equal(addStandard ? "standard" : "single", (await fixture.Reconcile())!.TicketId);
        Assert.Equal(TicketValidStatus.UnActive, future.ValidStatus);
    }

    [Fact] // S05/S09: expire an unusable candidate and give its successor priority.
    public async Task ExpiredCandidate_ShouldBeSkippedAndItsSuccessorActivated()
    {
        using var fixture = new Fixture();
        var source = Source("source", TicketValidStatus.Expire, Today.AddDays(-60));
        var expired = Queue("expired", source: 1, paidAt: Now.AddDays(-60));
        var child = Queue("child", source: 2, paidAt: Now.AddDays(-5));
        var standard = Queue("standard");
        var single = Queue("single", code: "SINGLE");
        single.Activate(Today);
        await fixture.Seed(source, expired, child, standard, single);

        Assert.Equal("child", (await fixture.Reconcile())!.TicketId);
        Assert.Equal(TicketValidStatus.Expire, expired.ValidStatus);
        Assert.Equal(TicketValidStatus.UnActive, single.ValidStatus);
        Assert.Equal(TicketValidStatus.UnActive, standard.ValidStatus);
    }

    [Theory] // S06/S07/A06: only SKU SINGLE is exempt, regardless of family name.
    [InlineData("SINGLE", "OTHER", false)]
    [InlineData("PACK_10", "SINGLE", true)]
    [InlineData("MONTHLY", "MONTHLY", true)]
    public async Task QueueConflict_ShouldUseSkuNotFamily(string code, string family, bool conflict)
    {
        using var fixture = new Fixture();
        await fixture.Seed(Queue("queued", code, family));
        Assert.Equal(conflict, await fixture.Repository.HasQueuedPassAsync("U1", CancellationToken.None));
    }

    [Fact] // A06: a non-SINGLE SKU must never use the single's yield operation.
    public void NonSingleWithSingleFamily_ShouldKeepValidityAndRejectYield()
    {
        var pass = Queue("pack", code: "PACK_10", family: "SINGLE");
        pass.Activate(Today);
        Assert.Equal(Today.AddDays(29), pass.ValidEndDate);
        Assert.Throws<InvalidOperationException>(pass.YieldSingleToQueue);
        Assert.Equal(TicketValidStatus.Active, pass.ValidStatus);
    }

    private static TicketPlanKind Plan(string code, string? family = null) => new()
    {
        Id = code, Name = code, FamilyCode = family ?? code,
        Type = code == "SINGLE" ? TicketPlanType.Pack : TicketPlanType.MPass,
        DefaultCredit = code == "SINGLE" ? 3 : null,
        DefaultExpireDays = code == "SINGLE" ? null : 30,
        Price = 100, IsActive = true
    };

    private static TicketPass Queue(string id, string code = "MONTHLY", string? family = null,
        int? source = null, DateTime? paidAt = null) => TicketPass.IssueQueued(
            id, "U1", "ORDER", "ITEM", Plan(code, family), source, paidAt ?? Now);

    private static TicketPass Source(string id, TicketValidStatus status, DateOnly end) =>
        TicketPass.Rehydrate(id, "U1", "ORDER", "ITEM", Plan("MONTHLY"),
            end.AddDays(-29), end, status, PaymentState.Paid, 0, 0,
            endedAt: status == TicketValidStatus.Active ? null : end.ToDateTime(TimeOnly.MinValue),
            endReason: status == TicketValidStatus.Active ? null
                : status == TicketValidStatus.Depleted ? TicketEndReason.Depleted : TicketEndReason.Expire,
            paidAt: end.AddDays(-29).ToDateTime(TimeOnly.MinValue));

    private sealed class Fixture : IDisposable
    {
        private readonly ServiceProvider _provider = new ServiceCollection()
            .AddInfrastructureInMemory().BuildServiceProvider();
        public ITicketPassRepository Repository { get; }
        public Fixture() => Repository = _provider.GetRequiredService<ITicketPassRepository>();
        public Task<IReadOnlyList<TicketPassPersistenceResult>> Seed(params TicketPass[] passes) =>
            Repository.AddRangeAsync(passes, new OrderPersistenceResult { OrderSn = 1, OrderId = "ORDER" },
                CancellationToken.None);
        public Task<gym_system.Domain.Entities.Members.CurrentTicketSnapshot?> Reconcile(DateOnly? today = null) =>
            Repository.ReconcileCurrentAsync("U1", today ?? Today, Now, "ADMIN", CancellationToken.None);
        public void Dispose() => _provider.Dispose();
    }
}
