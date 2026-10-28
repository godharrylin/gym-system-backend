using gym_system.Application.GymAccessUseCase;
using gym_system.Application.TicketsUseCase.Queries;
using gym_system.Application.TicketsUseCase.Services;
using gym_system.Domain.Entities.Members;
using gym_system.Domain.Entities.Tickets;
using gym_system.Domain.Entities.Users;
using gym_system.Domain.Enums;
using gym_system.Domain.Repositories;
using Microsoft.Extensions.Logging;
using Xunit;

namespace gym_system.Application.Tests;

public sealed class GymAccessEligibilityServiceTests
{
    [Fact]
    public async Task GateIdentityCheckDoesNotReconcileTicketsOrStartTransaction()
    {
        var f = new Fixture();
        f.Passes.Snapshot = null;
        f.History.Error = new InvalidOperationException("Identity checks must not read ticket history.");
        var result = await f.Service.IdentifyEntryAsync(" 0912345678 ");
        Assert.True(result.BasicEligibilityPassed);
        Assert.True(result.RequiresClassAndReservationCheck);
        Assert.Equal("GYM_STUDENT_IDENTIFIED", result.Code);
        Assert.Null(result.CurrentTicket);
        Assert.Equal(0, f.Passes.Reconciliations);
        Assert.Equal(0, f.Profiles.Updates);
        Assert.Equal(0, f.Work.Begins);
    }

    [Fact]
    public async Task GateIdentityCheckStillRejectsMissingStudentProfile()
    {
        var f = new Fixture();
        f.Profiles.Profile = null;
        var result = await f.Service.IdentifyEntryAsync("0912345678");
        Assert.False(result.BasicEligibilityPassed);
        Assert.Equal("GYM_STUDENT_PROFILE_MISSING", result.Code);
        Assert.Equal(0, f.Passes.Reconciliations);
        Assert.Equal(0, f.Work.Begins);
    }

    [Theory]
    [InlineData(UserRoleCode.Admin)]
    [InlineData(UserRoleCode.Staff)]
    [InlineData(UserRoleCode.Instructor)]
    public async Task GateIdentityCheckPreservesStaffPrecedenceWithoutTicketWrites(UserRoleCode role)
    {
        var f = new Fixture();
        f.Roles.Items = [Role(role), Role(UserRoleCode.Student)];
        f.Profiles.Profile = null;
        var result = await f.Service.IdentifyEntryAsync("0912345678");
        Assert.True(result.BasicEligibilityPassed);
        Assert.Equal(GymAccessIdentityKind.Staff, result.IdentityKind);
        Assert.False(result.RequiresClassAndReservationCheck);
        Assert.Equal(0, f.Passes.Reconciliations);
        Assert.Equal(0, f.Work.Begins);
    }

    private static readonly DateTime Now = new(2026, 10, 16, 19, 5, 30, 125);
    private static readonly DateOnly Today = DateOnly.FromDateTime(Now);

    [Theory]
    [InlineData(null, "GYM_PHONE_REQUIRED")]
    [InlineData("  ", "GYM_PHONE_REQUIRED")]
    [InlineData("0912", "GYM_PHONE_INVALID")]
    [InlineData("09123456789", "GYM_PHONE_INVALID")]
    [InlineData("0812345678", "GYM_PHONE_INVALID")]
    [InlineData("+886912345678", "GYM_PHONE_INVALID")]
    [InlineData("０９１２３４５６７８", "GYM_PHONE_INVALID")]
    [InlineData("09%3456789", "GYM_PHONE_INVALID")]
    [InlineData("09 2345678", "GYM_PHONE_INVALID")]
    public async Task InvalidPhone_ShouldRejectBothDirectionsWithoutDatabaseLookup(string? phone, string code)
    {
        var f = new Fixture();
        foreach (var result in new[] { await f.Service.CheckEntryAsync(phone), await f.Service.CheckExitAsync(phone) })
        {
            Assert.False(result.BasicEligibilityPassed);
            Assert.Equal(code, result.Code);
            Assert.Null(result.UserId);
        }
        Assert.Equal(0, f.Users.Lookups);
        Assert.Equal(0, f.Passes.Reconciliations);
    }

    [Fact]
    public async Task Lookup_ShouldUseTrimmedExactPhone_AndUnknownUserNeverPasses()
    {
        var f = new Fixture();
        f.Users.User = null;
        Assert.Equal("GYM_USER_NOT_FOUND", (await f.Service.CheckEntryAsync(" 0912345678 ")).Code);
        Assert.Equal("0912345678", f.Users.LastPhone);
        Assert.False((await f.Service.CheckExitAsync("0912345678")).BasicEligibilityPassed);
    }

    [Theory]
    [InlineData(UserRoleCode.Admin)]
    [InlineData(UserRoleCode.Staff)]
    [InlineData(UserRoleCode.Instructor)]
    public async Task StaffWithStudentRole_ShouldSkipStudentProfileTicketAndTransaction(UserRoleCode role)
    {
        var f = new Fixture();
        f.Roles.Items = [Role(role), Role(UserRoleCode.Student)];
        f.Profiles.Profile = null;
        var result = await f.Service.CheckEntryAsync("0912345678");
        Assert.True(result.BasicEligibilityPassed);
        Assert.Equal(GymAccessIdentityKind.Staff, result.IdentityKind);
        Assert.Contains(role, result.ActiveRoles);
        Assert.Contains(UserRoleCode.Student, result.ActiveRoles);
        Assert.False(result.RequiresClassAndReservationCheck);
        Assert.Null(result.CurrentTicket);
        Assert.Equal(0, f.Profiles.Lookups);
        Assert.Equal(0, f.Passes.Reconciliations);
        Assert.Equal(0, f.Work.Begins);
    }

    [Fact]
    public async Task MultipleStaffRoles_ShouldPreserveAllRolesWithoutSelectingANewPriority()
    {
        var f = new Fixture();
        f.Roles.Items = [Role(UserRoleCode.Staff), Role(UserRoleCode.Admin), Role(UserRoleCode.Instructor)];
        var result = await f.Service.CheckEntryAsync("0912345678");
        Assert.Equal(GymAccessIdentityKind.Staff, result.IdentityKind);
        Assert.Equal(3, result.ActiveRoles.Count);
    }

    [Fact]
    public async Task InactiveStaffRole_ShouldFallBackToActiveStudent()
    {
        var f = new Fixture();
        f.Roles.Items = [Role(UserRoleCode.Admin, false), Role(UserRoleCode.Student)];
        var result = await f.Service.CheckEntryAsync("0912345678");
        Assert.True(result.BasicEligibilityPassed);
        Assert.Equal(GymAccessIdentityKind.Student, result.IdentityKind);
        Assert.DoesNotContain(UserRoleCode.Admin, result.ActiveRoles);
        Assert.Equal(1, f.Passes.Reconciliations);
    }

    [Fact]
    public async Task DisabledAccount_ShouldRejectEntryButNotExit()
    {
        var f = new Fixture();
        f.Users.User!.Deactivate();
        Assert.Equal("GYM_ACCOUNT_INACTIVE", (await f.Service.CheckEntryAsync("0912345678")).Code);
        var exit = await f.Service.CheckExitAsync("0912345678");
        Assert.True(exit.BasicEligibilityPassed);
        Assert.Contains("GYM_ACCOUNT_INACTIVE", exit.WarningCodes);
        Assert.Equal(0, f.Passes.Reconciliations);
        Assert.Contains(f.Log.Messages, x => x.Contains("GYM_ACCOUNT_INACTIVE"));
    }

    [Fact]
    public async Task NoActiveRole_ShouldRejectEntryWithRegistrationMessage_ButAllowIdentifiedExit()
    {
        var f = new Fixture();
        f.Roles.Items = [Role(UserRoleCode.Student, false)];
        var entry = await f.Service.CheckEntryAsync("0912345678");
        Assert.Equal("尚未註冊會員", entry.Message);
        Assert.False(entry.BasicEligibilityPassed);
        Assert.True((await f.Service.CheckExitAsync("0912345678")).BasicEligibilityPassed);
    }

    [Fact]
    public async Task MissingProfile_ShouldRejectAndLogEntry_ButAllowExitWithAnomaly()
    {
        var f = new Fixture();
        f.Profiles.Profile = null;
        var entry = await f.Service.CheckEntryAsync("0912345678");
        Assert.Equal("GYM_STUDENT_PROFILE_MISSING", entry.Code);
        Assert.Equal("會員資料異常，請洽工作人員", entry.Message);
        var exit = await f.Service.CheckExitAsync("0912345678");
        Assert.True(exit.BasicEligibilityPassed);
        Assert.Contains("GYM_STUDENT_PROFILE_MISSING", exit.WarningCodes);
        Assert.Contains(f.Log.Messages, x => x.Contains("GYM_STUDENT_PROFILE_MISSING"));
        Assert.DoesNotContain(f.Log.Messages, x => x.Contains("0912345678"));
        Assert.Equal(0, f.Work.Begins);
    }

    [Fact]
    public async Task ExpiryDay_ShouldRemainValid_UsingOneServerTimeAndAuthoritativeTicket()
    {
        var f = new Fixture();
        f.Profiles.Profile!.UpdateCurrentTicket(new() { TicketId = "STALE-PROFILE" });
        var result = await f.Service.CheckEntryAsync("0912345678");
        Assert.True(result.BasicEligibilityPassed);
        Assert.True(result.RequiresClassAndReservationCheck);
        Assert.Equal("PASS-1", result.CurrentTicket!.TicketId);
        Assert.Equal(Now, result.EvaluatedAt);
        Assert.Equal(Today, f.Passes.EvaluatedDate);
        Assert.Equal(Now, f.Passes.UpdatedAt);
        Assert.Equal("SYSTEM", f.Passes.Operator);
        Assert.Same(result.CurrentTicket, f.Profiles.Profile.CurrentTicket);
        Assert.Equal((1, 1, 0), (f.Work.Begins, f.Work.Commits, f.Work.Rollbacks));
    }

    [Fact]
    public async Task SnapshotAlone_ShouldNotGrantEntry_WhenNoAuthoritativeTicketExists()
    {
        var f = new Fixture();
        f.Profiles.Profile!.UpdateCurrentTicket(f.Passes.Snapshot!);
        f.Passes.Snapshot = null;
        var result = await f.Service.CheckEntryAsync("0912345678");
        Assert.False(result.BasicEligibilityPassed);
        Assert.Equal("GYM_NO_TICKET", result.Code);
        Assert.Null(f.Profiles.Profile.CurrentTicket);
        Assert.Equal(Now, f.Profiles.ClearedAt);
    }

    [Theory]
    [InlineData("Expire", "GYM_TICKET_EXPIRED")]
    [InlineData("Cancelled", "GYM_TICKET_CANCELLED")]
    [InlineData("Depleted", "GYM_TICKET_DEPLETED")]
    [InlineData("UnActive", "GYM_TICKET_NOT_ACTIVE")]
    public async Task NoCurrentTicket_ShouldExplainHistoryButNeverAuthorizeFromIt(string status, string code)
    {
        var f = new Fixture();
        f.Passes.Snapshot = null;
        f.History.Items = [History(status)];
        var result = await f.Service.CheckEntryAsync("0912345678");
        Assert.False(result.BasicEligibilityPassed);
        Assert.Equal(code, result.Code);
        Assert.Equal(1, f.Work.Commits);
    }

    [Fact]
    public async Task NoExpirySingle_ShouldPassWithoutUsingCredits()
    {
        var f = new Fixture();
        f.Passes.Snapshot = Snapshot("SINGLE", "PACK", null, null, 2);
        var result = await f.Service.CheckEntryAsync("0912345678");
        Assert.True(result.BasicEligibilityPassed);
        Assert.Equal(2, result.CurrentTicket!.TicketRemainCount);
    }

    [Theory]
    [InlineData("expired", "GYM_TICKET_EXPIRED")]
    [InlineData("future", "GYM_TICKET_NOT_ACTIVE")]
    [InlineData("unpaid", "GYM_NO_USABLE_TICKET")]
    [InlineData("empty-pack", "GYM_TICKET_DEPLETED")]
    [InlineData("unknown-credit", "GYM_TICKET_DATA_INVALID")]
    [InlineData("missing-dates", "GYM_TICKET_DATA_INVALID")]
    public async Task DefensiveTicketValidation_ShouldNotGrantBasicEligibility(string scenario, string expected)
    {
        var f = new Fixture();
        f.Passes.Snapshot = scenario switch
        {
            "expired" => Snapshot(end: Today.AddDays(-1)),
            "future" => Snapshot(start: Today.AddDays(1), end: Today.AddDays(30)),
            "unpaid" => Snapshot(payment: "UnPaid"),
            "empty-pack" => Snapshot("SINGLE", "PACK", null, null, 0),
            "unknown-credit" => Snapshot("SINGLE", "PACK", null, null, null),
            _ => Snapshot(start: null, end: null)
        };
        var result = await f.Service.CheckEntryAsync("0912345678");
        Assert.False(result.BasicEligibilityPassed);
        Assert.Equal(expected, result.Code);
    }

    [Fact]
    public async Task LifecycleOrProfileFailure_ShouldRollbackWithoutCommit()
    {
        var f = new Fixture();
        f.Profiles.AllowUpdate = false;
        await Assert.ThrowsAsync<StudentProfileSnapshotUpdateException>(() => f.Service.CheckEntryAsync("0912345678"));
        Assert.Equal((1, 0, 1), (f.Work.Begins, f.Work.Commits, f.Work.Rollbacks));
    }

    [Fact]
    public async Task Exit_ShouldReadAnomaliesWithoutReconcilingOrStartingTransaction()
    {
        var f = new Fixture();
        f.History.Items = [History("Active", "PACK", 0)];
        var result = await f.Service.CheckExitAsync("0912345678");
        Assert.True(result.BasicEligibilityPassed);
        Assert.False(result.RequiresClassAndReservationCheck);
        Assert.Contains("GYM_TICKET_DEPLETED", result.WarningCodes);
        Assert.Equal(0, f.Passes.Reconciliations);
        Assert.Equal(0, f.Profiles.Updates);
        Assert.Equal(0, f.Work.Begins);
    }

    [Fact]
    public async Task OptionalExitDiagnosisFailure_ShouldNotTurnIntoTicketDenial()
    {
        var f = new Fixture();
        f.History.Error = new InvalidOperationException("diagnosis unavailable");
        var result = await f.Service.CheckExitAsync("0912345678");
        Assert.True(result.BasicEligibilityPassed);
        Assert.Contains("GYM_EXIT_DIAGNOSIS_UNAVAILABLE", result.WarningCodes);
    }

    [Fact]
    public async Task CancelledRequest_ShouldNotReturnAPassingDecision()
    {
        var f = new Fixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Service.CheckExitAsync("0912345678", cancellation.Token));
        Assert.Equal(0, f.Users.Lookups);
    }

    [Fact]
    public async Task SharedLifecycle_ShouldNotOwnTransaction_AndShouldValidateIdentifiers()
    {
        var f = new Fixture();
        var shared = new CurrentTicketService(f.Passes, f.Profiles);
        await shared.ReconcileAsync(" U1 ", Now, "ADMIN");
        Assert.Equal("U1", f.Passes.Owner);
        Assert.Equal(0, f.Work.Begins);
        Assert.Equal(0, f.Work.Commits);
        await Assert.ThrowsAsync<InvalidOperationException>(() => shared.ReconcileAsync(" ", Now, "ADMIN"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => shared.ReconcileAsync("U1", Now, " "));
    }

    [Fact]
    public async Task TicketQueryRefactor_ShouldPreserveMissingProfileNotFoundAndRollback()
    {
        var f = new Fixture();
        f.Profiles.AllowUpdate = false;
        var handler = new GetStudentTicketPassesHandler(f.History,
            new CurrentTicketService(f.Passes, f.Profiles), f.Work, new Clock());
        await Assert.ThrowsAsync<KeyNotFoundException>(() => handler.Handle("U1"));
        Assert.Equal((1, 0, 1), (f.Work.Begins, f.Work.Commits, f.Work.Rollbacks));
    }

    private static UserRole Role(UserRoleCode role, bool active = true) => UserRole.Assign("U1", role, Now, active);
    private static CurrentTicketSnapshot Snapshot(string code = "MONTHLY", string type = "M_PASS",
        DateOnly? start = null, DateOnly? end = null, int? credits = null, string payment = "Paid") => new()
    {
        TicketId = "PASS-1", PlanCode = code, TicketType = type,
        TicketValidState = "Active", TicketPaymentState = payment,
        TicketStartDate = start, TicketExpireDate = end, TicketRemainCount = credits, UpdatedAt = Now
    };
    private static StudentTicketPassResult History(string status, string type = "M_PASS", int? credits = null) => new()
    {
        PassId = "PASS-1", PlanCode = type == "PACK" ? "SINGLE" : "MONTHLY", PlanName = "Test",
        PlanType = type, ValidStatus = status, PaymentStatus = "Paid", UnitPrice = 100,
        PaidAt = Now.AddDays(-1), CreditsRemaining = credits
    };

    private sealed class Fixture
    {
        public Users Users { get; } = new();
        public Roles Roles { get; } = new();
        public Profiles Profiles { get; } = new();
        public Passes Passes { get; } = new();
        public PassHistory History { get; } = new();
        public Work Work { get; } = new();
        public Log Log { get; } = new();
        public GymAccessEligibilityService Service => new(Users, Roles, Profiles,
            new CurrentTicketService(Passes, Profiles), History, Work, new Clock(), Log);
    }

    private sealed class Clock : IClock
    {
        public DateTime Now() => GymAccessEligibilityServiceTests.Now;
        public DateOnly Today() => throw new InvalidOperationException("Use the same captured Now, not a second clock read.");
    }
    private sealed class Work : IUnitOfWork
    {
        public int Begins, Commits, Rollbacks;
        public Task BeginAsync(CancellationToken ct) { Begins++; return Task.CompletedTask; }
        public Task CommitAsync(CancellationToken ct) { Commits++; return Task.CompletedTask; }
        public Task RollbackAsync(CancellationToken ct) { Rollbacks++; return Task.CompletedTask; }
    }
    private sealed class Users : IUserRepository
    {
        public User? User = gym_system.Domain.Entities.Users.User.Rehydrate("U1", "Test", "0912345678", "test-only", true);
        public int Lookups;
        public string? LastPhone;
        public Task<User?> FindUserByPhoneAsync(string phone, CancellationToken ct)
        { Lookups++; LastPhone = phone; return Task.FromResult(User); }
        public Task<User?> FindUserByIdAsync(string userId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> GetExistingPhonesAsync(IReadOnlyList<string> phones, CancellationToken ct) => throw new NotSupportedException();
        public Task<string> AddAsync(User user, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> ExistsPhoneForOtherUserAsync(string userId, string phone, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> UpdateBasicProfileAsync(string userId, string name, string phone, CancellationToken ct) => throw new NotSupportedException();
    }
    private sealed class Roles : IUserRoleRepository
    {
        public IReadOnlyList<UserRole> Items = [Role(UserRoleCode.Student)];
        public Task<IReadOnlyList<UserRole>> GetActiveRolesAsync(string userId, CancellationToken ct) => Task.FromResult(Items);
        public Task<UserRole?> GetUserRoleAsync(string userId, UserRoleCode roleType, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> AddRoleAsync(UserRole role, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> ReactivateRoleAsync(string userId, UserRoleCode roleType, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> SetRoleActiveAsync(string userId, UserRoleCode roleType, bool isActive, CancellationToken ct) => throw new NotSupportedException();
    }
    private sealed class Profiles : IStudentProfileRepository
    {
        public StudentProfile? Profile = StudentProfile.Create("U1");
        public int Lookups, Updates;
        public bool AllowUpdate = true;
        public DateTime? ClearedAt;
        public Task<StudentProfile?> FindByUserIdAsync(string userId, CancellationToken ct)
        { Lookups++; return Task.FromResult(Profile); }
        public Task<bool> UpdateCurrentTicketAsync(string userId, CurrentTicketSnapshot snapshot, CancellationToken ct)
        { Updates++; if (AllowUpdate) Profile?.UpdateCurrentTicket(snapshot); return Task.FromResult(AllowUpdate && Profile is not null); }
        public Task<bool> ClearCurrentTicketAsync(string userId, DateTime now, CancellationToken ct)
        { ClearedAt = now; if (AllowUpdate) Profile?.ClearCurrentTicket(); return Task.FromResult(AllowUpdate && Profile is not null); }
        public Task AddAsync(StudentProfile profile, CancellationToken ct) => throw new NotSupportedException();
        public Task AddRangeAsync(IReadOnlyList<StudentProfile> profiles, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> UpdateLastVisitAsync(string userId, DateTime now, CancellationToken ct) => throw new NotSupportedException();
    }
    private sealed class Passes : ITicketPassRepository
    {
        public CurrentTicketSnapshot? Snapshot = GymAccessEligibilityServiceTests.Snapshot(start: Today.AddDays(-29), end: Today);
        public int Reconciliations;
        public DateOnly? EvaluatedDate;
        public DateTime? UpdatedAt;
        public string? Owner, Operator;
        public Task<CurrentTicketSnapshot?> ReconcileCurrentAsync(string ownerId, DateOnly today, DateTime now, string operatorId, CancellationToken ct)
        { Reconciliations++; EvaluatedDate = today; UpdatedAt = now; Owner = ownerId; Operator = operatorId; return Task.FromResult(Snapshot); }
        public Task<IReadOnlyList<TicketPassPersistenceResult>> AddRangeAsync(IReadOnlyList<TicketPass> passes, OrderPersistenceResult order, CancellationToken ct) => throw new NotSupportedException();
        public Task<RenewalSourcePass?> FindLatestRenewalSourceAsync(string ownerId, string family, bool acquireLock, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> HasQueuedPassAsync(string ownerId, CancellationToken ct) => throw new NotSupportedException();
        public Task LockOwnerAsync(string ownerId, CancellationToken ct) => throw new NotSupportedException();
        public Task<CancelledTicketPassResult?> CancelQueuedRenewalAsync(string passId, DateTime now, string operatorId, CancellationToken ct) => throw new NotSupportedException();
    }
    private sealed class PassHistory : IStudentTicketPassQueryService
    {
        public IReadOnlyList<StudentTicketPassResult> Items = [];
        public Exception? Error;
        public Task<IReadOnlyList<StudentTicketPassResult>> GetByStudentIdAsync(string studentId, CancellationToken ct) =>
            Error is null ? Task.FromResult(Items) : Task.FromException<IReadOnlyList<StudentTicketPassResult>>(Error);
    }
    private sealed class Log : ILogger<GymAccessEligibilityService>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        void ILogger.Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
