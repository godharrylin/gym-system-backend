using gym_system.Application.TicketPlansUseCase.Queries;
using gym_system.Domain.Entities.Members;
using gym_system.Domain.Entities.Tickets;
using gym_system.Domain.Repositories;
using Xunit;

namespace gym_system.Application.Tests;

public sealed class RenewalTicketPassEligibilityServiceTests
{
    [Theory]
    [InlineData(9, true)]
    [InlineData(10, false)]
    public async Task FindEligibleSourceAsync_ShouldUseEndDatePlusNineDays(
        int daysAfterEnd,
        bool expected)
    {
        var repository = CreateRepository();
        var service = new RenewalTicketPassEligibilityService(repository);

        var result = await service.FindEligibleSourceAsync(
            "U1",
            "MONTHLY",
            new DateTime(2026, 9, 1).AddDays(daysAfterEnd),
            acquireLock: false,
            CancellationToken.None);

        Assert.Equal(expected, result is not null);
    }

    [Fact]
    public async Task FindEligibleSourceAsync_ShouldFailClosed_WhenTargetFamilyIsNull()
    {
        var repository = CreateRepository();
        var service = new RenewalTicketPassEligibilityService(repository);

        var result = await service.FindEligibleSourceAsync(
            "U1",
            null,
            new DateTime(2026, 9, 1),
            acquireLock: false,
            CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task FindEligibleSourceAsync_ShouldReject_WhenSourceAlreadyHasActiveRenewal()
    {
        var repository = CreateRepository(source => source.HasNonCancelledRenewal = true);
        var service = new RenewalTicketPassEligibilityService(repository);

        var result = await service.FindEligibleSourceAsync(
            "U1",
            "MONTHLY",
            new DateTime(2026, 9, 1),
            acquireLock: false,
            CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task FindEligibleSourceAsync_ShouldRejectNormalRenewal_WhenAnotherPassIsQueued()
    {
        var repository = CreateRepository();
        repository.HasQueuedPass = true;
        var service = new RenewalTicketPassEligibilityService(repository);

        var result = await service.FindEligibleSourceAsync(
            "U1",
            "MONTHLY",
            new DateTime(2026, 9, 1),
            acquireLock: false,
            CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task FindEligibleSourceAsync_ShouldAllowCancellationRetryToday_EvenWithQueuedPass()
    {
        var repository = CreateRepository(source =>
        {
            source.HasCancelledRenewal = true;
            source.LastCancelledRenewalAt = new DateTime(2026, 9, 1, 15, 0, 0);
        });
        repository.HasQueuedPass = true;
        var service = new RenewalTicketPassEligibilityService(repository);

        var result = await service.FindEligibleSourceAsync(
            "U1",
            "MONTHLY",
            new DateTime(2026, 9, 1, 20, 0, 0),
            acquireLock: true,
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result.IsCancellationRetry);
        Assert.True(repository.AcquireLockRequested);
    }

    [Fact]
    public async Task FindEligibleSourceAsync_ShouldAllowCancellationRetryOnFollowingDay()
    {
        var repository = CreateRepository(source =>
        {
            source.HasCancelledRenewal = true;
            source.LastCancelledRenewalAt = new DateTime(2026, 9, 1, 23, 59, 0);
        });
        var service = new RenewalTicketPassEligibilityService(repository);

        var result = await service.FindEligibleSourceAsync(
            "U1",
            "MONTHLY",
            new DateTime(2026, 9, 2),
            acquireLock: false,
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result.IsCancellationRetry);
    }

    [Fact]
    public async Task FindEligibleSourceAsync_ShouldRejectCancellationRetryAfterFollowingDay()
    {
        var repository = CreateRepository(source =>
        {
            source.HasCancelledRenewal = true;
            source.LastCancelledRenewalAt = new DateTime(2026, 9, 1, 23, 59, 0);
        });
        var service = new RenewalTicketPassEligibilityService(repository);

        var result = await service.FindEligibleSourceAsync(
            "U1",
            "MONTHLY",
            new DateTime(2026, 9, 3),
            acquireLock: false,
            CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task FindEligibleSourceAsync_ShouldUseDepletedAtAsEffectiveEndDate()
    {
        var repository = CreateRepository(source =>
        {
            source.FamilyCode = "PACK_10";
            source.ValidStatus = TicketValidStatus.Depleted;
            source.ValidEndDate = new DateOnly(2026, 10, 1);
            source.EndedAt = new DateTime(2026, 8, 20, 18, 0, 0);
            source.EndReason = TicketEndReason.Depleted;
        });
        var service = new RenewalTicketPassEligibilityService(repository);

        var result = await service.FindEligibleSourceAsync(
            "U1",
            "PACK_10",
            new DateTime(2026, 8, 29),
            acquireLock: false,
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(new DateOnly(2026, 8, 20), result.SourceEffectiveEndDate);
    }

    private static FakeTicketPassRepository CreateRepository(
        Action<MutableRenewalSource>? configure = null)
    {
        var source = new MutableRenewalSource
        {
            PassSn = 10,
            FamilyCode = "MONTHLY",
            ValidStatus = TicketValidStatus.Expire,
            ValidStartDate = new DateOnly(2026, 8, 3),
            ValidEndDate = new DateOnly(2026, 9, 1)
        };
        configure?.Invoke(source);
        return new FakeTicketPassRepository { Source = source.ToResult() };
    }

    private sealed class MutableRenewalSource
    {
        public int PassSn { get; set; }
        public string FamilyCode { get; set; } = string.Empty;
        public TicketValidStatus ValidStatus { get; set; }
        public DateOnly? ValidStartDate { get; set; }
        public DateOnly? ValidEndDate { get; set; }
        public DateTime? EndedAt { get; set; }
        public TicketEndReason? EndReason { get; set; }
        public bool HasNonCancelledRenewal { get; set; }
        public bool HasCancelledRenewal { get; set; }
        public DateTime? LastCancelledRenewalAt { get; set; }

        public RenewalSourcePass ToResult() => new()
        {
            PassSn = PassSn,
            FamilyCode = FamilyCode,
            ValidStatus = ValidStatus,
            ValidStartDate = ValidStartDate,
            ValidEndDate = ValidEndDate,
            EndedAt = EndedAt,
            EndReason = EndReason,
            HasNonCancelledRenewal = HasNonCancelledRenewal,
            HasCancelledRenewal = HasCancelledRenewal,
            LastCancelledRenewalAt = LastCancelledRenewalAt
        };
    }

    private sealed class FakeTicketPassRepository : ITicketPassRepository
    {
        public RenewalSourcePass? Source { get; init; }
        public bool HasQueuedPass { get; set; }
        public bool AcquireLockRequested { get; private set; }

        public Task<RenewalSourcePass?> FindLatestRenewalSourceAsync(
            string ownerId,
            string familyCode,
            bool acquireLock,
            CancellationToken ct)
        {
            AcquireLockRequested = acquireLock;
            if (Source is null
                || !Source.FamilyCode.Equals(familyCode, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult<RenewalSourcePass?>(null);
            }

            return Task.FromResult<RenewalSourcePass?>(Source);
        }

        public Task<bool> HasQueuedPassAsync(string ownerId, CancellationToken ct) =>
            Task.FromResult(HasQueuedPass);

        public Task LockOwnerAsync(string ownerId, CancellationToken ct) =>
            Task.CompletedTask;

        public Task<CancelledTicketPassResult?> CancelQueuedRenewalAsync(
            string passId,
            DateTime cancelledAt,
            string operatorId,
            CancellationToken ct) => throw new NotSupportedException();

        public Task<IReadOnlyList<TicketPassPersistenceResult>> AddRangeAsync(
            IReadOnlyList<TicketPass> passes,
            OrderPersistenceResult orderPersistence,
            CancellationToken ct) => throw new NotSupportedException();

        public Task<CurrentTicketSnapshot?> ReconcileCurrentAsync(
            string ownerId,
            DateOnly today,
            DateTime updatedAt,
            string operatorId,
            CancellationToken ct) => throw new NotSupportedException();
    }
}
