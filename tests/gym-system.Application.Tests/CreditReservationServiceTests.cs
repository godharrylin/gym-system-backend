using gym_system.Application.ClassAttendanceProcessing;
using gym_system.Domain.ClassAttendanceProcessing;
using gym_system.Domain.Entities.Members;
using gym_system.Domain.Entities.Tickets;
using gym_system.Domain.Repositories;
using Xunit;

namespace gym_system.Application.Tests;

public sealed class CreditReservationServiceTests
{
    private static readonly DateTime Now = new(2026, 10, 17, 19, 5, 0);
    private static readonly Guid Request = Guid.Parse("2b0bbe10-94ca-450d-b541-cf11f8689988");

    [Fact]
    public async Task SameRequestReturnsExistingWithoutConsumingCapacityAgain()
    {
        var f = new Fixture();
        var first = await f.Reserve();
        var again = await f.Reserve();
        Assert.True(first.Created);
        Assert.False(again.Created);
        Assert.Equal(first.Reservation.ReservationSn, again.Reservation.ReservationSn);
        Assert.Single(f.Rows);
        Assert.Equal("owner", f.Calls[0]);
        Assert.Equal("pass", f.Calls[1]);
        Assert.Equal("attendance", f.Calls[2]);
    }

    [Fact]
    public async Task AnotherRequestCannotCreateASecondHoldForSameAttendance()
    {
        var f = new Fixture();
        await f.Reserve();
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Reserve(key: "different-request"));
        Assert.Single(f.Rows);
    }

    [Fact]
    public async Task SameKeyWithChangedRequestContentIsRejected()
    {
        var f = new Fixture();
        await f.Reserve();
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.ReserveAsync(
            "student", 10, 20, "key", Now, Guid.NewGuid()));
    }

    [Fact]
    public async Task ReleasedHistoryIsReplayedButNewReturnCanReserveWithNewKey()
    {
        var f = new Fixture();
        var first = await f.Reserve();
        await f.Service.ReleaseAsync("student", first.Reservation.ReservationSn!.Value,
            Now.AddMinutes(1), "EXIT", "Left before cutoff.");
        var replay = await f.Reserve();
        Assert.False(replay.Created);
        Assert.Equal(CreditReservationState.Released, replay.Reservation.State);
        var returned = await f.Service.ReserveAsync("student", 10, 20, "return", Now.AddMinutes(2),
            Guid.NewGuid(), previousReservationSn: first.Reservation.ReservationSn);
        Assert.True(returned.Created);
        Assert.Equal(2, f.Rows.Count);
        Assert.Single(f.Rows.Values.Where(x => x.HoldsCredit));
    }

    [Theory]
    [InlineData(CreditReservationState.Reserved, false)]
    [InlineData(CreditReservationState.Fixed, false)]
    [InlineData(CreditReservationState.NeedsReview, false)]
    [InlineData(CreditReservationState.Released, true)]
    [InlineData(CreditReservationState.Consumed, true)]
    public async Task CapacityCountsOnlyLiveHolds(CreditReservationState state, bool allowed)
    {
        var f = new Fixture();
        var hold = CreditReservation.Reserve(11, 20, "other", Now, Request);
        hold = state switch
        {
            CreditReservationState.Fixed => hold.Fix(Now),
            CreditReservationState.NeedsReview => hold.RequireReview(Now, "UNKNOWN", "Pending result."),
            CreditReservationState.Released => hold.Release(Now, "EXIT", "Left."),
            CreditReservationState.Consumed => hold.Fix(Now).Consume(Now, 30),
            _ => hold
        };
        f.Seed(hold);
        if (allowed) Assert.True((await f.Reserve()).Created);
        else await Assert.ThrowsAsync<InvalidOperationException>(() => f.Reserve());
    }

    [Fact]
    public async Task ConsumedAttendanceCannotAcquireAnotherCreditEvenWithCapacity()
    {
        var f = new Fixture();
        f.Pass = f.Pass with { CreditsRemaining = 10 };
        f.Seed(CreditReservation.Reserve(10, 20, "consumed", Now, Request).Fix(Now).Consume(Now, 30));
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Reserve());
    }

    [Theory]
    [InlineData("UnActive", "Paid", "PACK", 1)]
    [InlineData("Cancelled", "Paid", "PACK", 1)]
    [InlineData("Active", "UnPaid", "PACK", 1)]
    [InlineData("Active", "Paid", "M_PASS", 1)]
    [InlineData("Active", "Paid", "PACK", 0)]
    public async Task UnusablePassDoesNotCreateHold(string state, string payment, string type, int credits)
    {
        var f = new Fixture();
        f.Pass = f.Pass with { ValidStatus = state, PaymentState = payment, TicketType = type, CreditsRemaining = credits };
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Reserve());
        Assert.Empty(f.Rows);
    }

    [Fact]
    public async Task EndDateIsInclusiveAndNoExpirySingleIsAllowed()
    {
        var f = new Fixture();
        Assert.True((await f.Reserve()).Created);
        var expired = new Fixture();
        expired.Pass = expired.Pass with { ValidEndDate = Now.Date.AddDays(-1) };
        await Assert.ThrowsAsync<InvalidOperationException>(() => expired.Reserve());
        var single = new Fixture();
        single.Pass = single.Pass with { PlanCode = "SINGLE", ValidStartDate = null, ValidEndDate = null };
        Assert.True((await single.Reserve()).Created);
    }

    [Fact]
    public async Task UnknownOwnerAttendanceOrPredecessorIsRejected()
    {
        var f = new Fixture { AttendanceMatches = false };
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Reserve());
        f.AttendanceMatches = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.ReserveAsync(
            "someone-else", 10, 20, "key", Now, Request));
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.ReserveAsync(
            "student", 10, 20, "key", Now, Request, previousReservationSn: 999));
        Assert.Empty(f.Rows);
    }

    [Fact]
    public async Task FixAndReviewPreserveHoldAndNormalExitCannotReleaseFixed()
    {
        var f = new Fixture();
        var first = await f.Reserve();
        var id = first.Reservation.ReservationSn!.Value;
        await f.Service.FixAsync("student", id, Now.AddMinutes(5));
        await f.Service.RequireReviewAsync("student", id, Now.AddMinutes(6), "CONFLICT", "Ticket changed.");
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.ReleaseAsync(
            "student", id, Now.AddMinutes(7), "EXIT", "Left."));
        Assert.True(f.Rows[id].HoldsCredit);
        Assert.Equal(CreditReservationState.Fixed, f.Rows[id].ReviewFromState);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FixRevalidatesTicketAndCapacityChangedSinceReservation(bool cancelled)
    {
        var f = new Fixture();
        var id = (await f.Reserve()).Reservation.ReservationSn!.Value;
        f.Pass = cancelled ? f.Pass with { ValidStatus = "Cancelled" }
                           : f.Pass with { CreditsRemaining = 0 };
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.FixAsync("student", id, Now.AddMinutes(5)));
        Assert.Equal(CreditReservationState.Reserved, f.Rows[id].State);
    }

    private sealed class Fixture : ICreditReservationRepository, ITicketPassRepository
    {
        public Dictionary<long, CreditReservation> Rows { get; } = [];
        public List<string> Calls { get; } = [];
        public CreditPassCapacity Pass { get; set; } = new(20, "student", "Active", "Paid", "PACK", 1,
            Now.Date, Now.Date, "PACK_10");
        public bool AttendanceMatches { get; set; } = true;
        public CreditReservationService Service => new(this, this);
        public Task<ReserveCreditResult> Reserve(string key = "key") => Service.ReserveAsync("student", 10, 20, key, Now, Request);
        public CreditReservation Seed(CreditReservation value)
        {
            var saved = CreditReservation.Rehydrate(value.ToData() with { ReservationSn = Rows.Count + 1L });
            Rows.Add(saved.ReservationSn!.Value, saved);
            return saved;
        }
        public Task LockOwnerAsync(string ownerId, CancellationToken ct) { Calls.Add("owner"); return Task.CompletedTask; }
        public Task<CreditPassCapacity?> LockPassAsync(string studentId, int passSn, CancellationToken ct)
        { Calls.Add("pass"); return Task.FromResult<CreditPassCapacity?>(Pass.OwnerId == studentId && Pass.PassSn == passSn ? Pass : null); }
        public Task<bool> AttendanceBelongsToAsync(int attendanceSn, string studentId, CancellationToken ct)
        { Calls.Add("attendance"); return Task.FromResult(AttendanceMatches && studentId == "student"); }
        public Task<int?> FindPassSnAsync(long reservationSn, string studentId, CancellationToken ct)
            => Task.FromResult<int?>(studentId == "student" && Rows.TryGetValue(reservationSn, out var row) ? row.PassSn : null);
        public Task<CreditReservation?> FindByKeyAsync(string key, CancellationToken ct)
            => Task.FromResult(Rows.Values.SingleOrDefault(x => x.IdempotencyKey == key));
        public Task<CreditReservation?> FindByIdAsync(long reservationSn, CancellationToken ct)
            => Task.FromResult(Rows.GetValueOrDefault(reservationSn));
        public Task<IReadOnlyList<CreditReservation>> GetByAttendanceAsync(int attendanceSn, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<CreditReservation>>(Rows.Values.Where(x => x.AttendanceSn == attendanceSn).ToArray());
        public Task<IReadOnlyList<CreditReservation>> GetByPassAsync(int passSn, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<CreditReservation>>(Rows.Values.Where(x => x.PassSn == passSn).ToArray());
        public Task<CreditReservation> InsertAsync(CreditReservation reservation, CancellationToken ct) => Task.FromResult(Seed(reservation));
        public Task<CreditReservation> UpdateAsync(CreditReservation original, CreditReservation replacement, CancellationToken ct)
        {
            Assert.Equal(original, Rows[original.ReservationSn!.Value]);
            Rows[original.ReservationSn.Value] = replacement;
            return Task.FromResult(replacement);
        }
        public Task<IReadOnlyList<TicketPassPersistenceResult>> AddRangeAsync(IReadOnlyList<TicketPass> p, OrderPersistenceResult o, CancellationToken ct) => throw new NotSupportedException();
        public Task<RenewalSourcePass?> FindLatestRenewalSourceAsync(string o, string f, bool a, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> HasQueuedPassAsync(string o, CancellationToken ct) => throw new NotSupportedException();
        public Task<CancelledTicketPassResult?> CancelQueuedRenewalAsync(string p, DateTime a, string o, CancellationToken ct) => throw new NotSupportedException();
        public Task<CurrentTicketSnapshot?> ReconcileCurrentAsync(string o, DateOnly t, DateTime a, string p, CancellationToken ct) => throw new NotSupportedException();
    }
}
