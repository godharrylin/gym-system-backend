using Xunit;
using gym_system.Application.ScheduleSessionsUseCase.Command.GetOrEnsureScheduleWeek;
using gym_system.Domain.Entities.ScheduleSessions;
using gym_system.Domain.Repositories;

namespace gym_system.Aplication.Tests;

public sealed class ScheduleSessionStageThreeTests
{
    [Fact]
    public void Session_IsEditableOnlyBeforeOriginalStart()
    {
        var session = CreateSession(new DateTime(2026, 10, 20, 19, 0, 0));

        session.EnsureEditable(new DateTime(2026, 10, 20, 18, 59, 59));
        Assert.Throws<InvalidOperationException>(() =>
            session.EnsureEditable(new DateTime(2026, 10, 20, 19, 0, 0)));
    }

    [Fact]
    public async Task ExistingCancelledSession_PreventsTemplateRegeneration()
    {
        var existing = CreateSession(new DateTime(2026, 10, 20, 19, 0, 0));
        existing.Cancel(new DateTime(2026, 10, 19, 10, 0, 0));
        var repository = new FakeRepository { Sessions = [existing] };
        var handler = new GetOrEnsureScheduleWeekHandler(repository, new FakeWork());

        var result = await handler.Handle(
            new GetOrEnsureScheduleWeekCommand { WeekStart = new DateOnly(2026, 10, 21) },
            CancellationToken.None);

        Assert.True(repository.LockAcquiredBeforeRead);
        Assert.False(repository.TemplatesRead);
        Assert.Empty(result.Sessions);
        Assert.False(result.Created);
        Assert.Equal(new DateOnly(2026, 10, 19), result.WeekStart);
    }

    [Fact]
    public async Task EmptyWeek_GeneratesTemplatesOnceInsideWeekLock()
    {
        var repository = new FakeRepository
        {
            Templates =
            [
                ScheduleSessionTemplate.Rehydrate(
                    "RULE001", "CLS000001", "Yoga", "blue", false,
                    DayOfWeek.Tuesday, TimeSpan.FromHours(19), TimeSpan.FromHours(20),
                    "U001", "Coach")
            ]
        };
        var handler = new GetOrEnsureScheduleWeekHandler(repository, new FakeWork());

        var result = await handler.Handle(
            new GetOrEnsureScheduleWeekCommand { WeekStart = new DateOnly(2026, 10, 19) },
            CancellationToken.None);

        Assert.True(repository.LockAcquiredBeforeRead);
        Assert.Single(repository.Sessions);
        Assert.Single(result.Sessions);
        Assert.True(result.Created);
        Assert.Equal("RULE001", result.Sessions[0].RuleSn);
    }

    [Fact]
    public async Task EmptyWeek_WithConflictingTemplates_FailsWithoutSavingSessions()
    {
        var repository = new FakeRepository
        {
            Templates =
            [
                ScheduleSessionTemplate.Rehydrate(
                    "RULE001", "CLS000001", "Yoga", "blue", false,
                    DayOfWeek.Tuesday, TimeSpan.FromHours(19), TimeSpan.FromHours(20),
                    "U001", "Coach"),
                ScheduleSessionTemplate.Rehydrate(
                    "RULE002", "CLS000002", "Pilates", "green", false,
                    DayOfWeek.Tuesday, TimeSpan.FromHours(20).Add(TimeSpan.FromMinutes(5)),
                    TimeSpan.FromHours(21).Add(TimeSpan.FromMinutes(5)), "U002", "Coach 2")
            ]
        };
        var handler = new GetOrEnsureScheduleWeekHandler(repository, new FakeWork());

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(
            new GetOrEnsureScheduleWeekCommand { WeekStart = new DateOnly(2026, 10, 19) },
            CancellationToken.None));

        Assert.Empty(repository.Sessions);
        Assert.True(repository.LockAcquiredBeforeRead);
    }

    private static ScheduleSession CreateSession(DateTime start) => ScheduleSession.Rehydrate(
        1, "CLSA202610200000001", DateOnly.FromDateTime(start), "CLS000001", "Yoga", "blue",
        "U001", "Coach", start, start.AddHours(1), SessionStatus.Open, "Auto", "RULE001", false);

    private sealed class FakeWork : IUnitOfWork
    {
        public Task BeginAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task CommitAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task RollbackAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeRepository : IScheduleSessionRepository
    {
        private bool _locked;
        public bool LockAcquiredBeforeRead { get; private set; }
        public bool TemplatesRead { get; private set; }
        public List<ScheduleSession> Sessions { get; set; } = [];
        public IReadOnlyList<ScheduleSessionTemplate> Templates { get; init; } = [];

        public Task AcquireWeekGenerationLockAsync(DateOnly weekStart, CancellationToken ct)
        {
            _locked = true;
            return Task.CompletedTask;
        }

        public Task AcquireSessionMutationLockAsync(string sessionId, CancellationToken ct) => Task.CompletedTask;

        public Task<IReadOnlyList<ScheduleSession>> GetByWeekAsync(DateOnly weekStart, DateOnly weekEnd, CancellationToken ct)
        {
            LockAcquiredBeforeRead |= _locked;
            return Task.FromResult<IReadOnlyList<ScheduleSession>>(Sessions.ToList());
        }

        public Task<IReadOnlyList<ScheduleSessionTemplate>> GetActiveTemplatesAsync(CancellationToken ct)
        {
            TemplatesRead = true;
            return Task.FromResult(Templates);
        }

        public Task AddRangeAsync(IReadOnlyList<ScheduleSession> sessions, CancellationToken ct)
        {
            Sessions.AddRange(sessions);
            return Task.CompletedTask;
        }

        public Task<bool> AddAsync(ScheduleSession session, CancellationToken ct) => throw new NotSupportedException();
        public Task<ScheduleSession?> GetOverlappingSchedulesSessionAsync(ScheduleSession session, CancellationToken ct) => throw new NotSupportedException();
        public Task<ScheduleSession?> GetByIdAsync(string id, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> UpdateAsync(ScheduleSession session, CancellationToken ct) => throw new NotSupportedException();
    }
}
