using gym_system.Application.DevicesUseCase;
using gym_system.Application.GymAccessUseCase;
using gym_system.Application.ClassAttendanceProcessing;
using gym_system.Domain.Entities.Devices;
using gym_system.Domain.Entities.Members;
using gym_system.Domain.Enums;
using gym_system.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace gym_system.Application.Tests;

public sealed class GatePassageServiceTests
{
    [Fact]
    public async Task StudentPreparationDenialNeverDispatchesDoorCommand()
    {
        var f = new Fixture(GateDirection.Entry) { Check = Passed(GymAccessIdentityKind.Student, UserRoleCode.Student) };
        f.Classes = new(f.Work) { Allowed = false };
        var result = await f.Execute();
        Assert.Equal("Denied", result.Outcome);
        Assert.Equal("TEST_CLASS_DENIED", result.Code);
        Assert.Equal(0, f.Turnstile.Calls);
        Assert.Equal(1, f.Classes.Preparations);
        Assert.Equal("Denied", f.Classes.LastOutcome);
    }

    [Fact]
    public async Task PreparedStudentEntryCommitsBeforeDispatchAndReplayDoesNotPrepareOrOpenAgain()
    {
        var f = new Fixture(GateDirection.Entry) { Check = Passed(GymAccessIdentityKind.Student, UserRoleCode.Student) };
        f.Classes = new(f.Work);
        var id = Guid.NewGuid();
        Assert.True((await f.Execute(id)).Succeeded);
        Assert.True((await f.Execute(id)).Replayed);
        Assert.Equal(1, f.Classes.Preparations);
        Assert.Equal(1, f.Classes.Completions);
        Assert.Equal(1, f.Turnstile.Calls);
        Assert.Equal(1, f.Profiles.Updates);
        Assert.Equal(2, f.Work.Commits);
    }

    [Fact]
    public async Task PreparedStudentCancellationCompletesUnknownWithoutPretendingPresence()
    {
        var f = new Fixture(GateDirection.Entry) { Check = Passed(GymAccessIdentityKind.Student, UserRoleCode.Student) };
        f.Classes = new(f.Work);
        f.Turnstile.Cancel = true;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Execute());
        Assert.Equal("Unknown", f.Classes.LastOutcome);
        Assert.False(f.Records.Current!.AffectsPresence);
        Assert.Equal(0, f.Profiles.Updates);
    }

    [Fact]
    public async Task PreparedStudentMockSuccessPassesSimulationFlagWithoutUpdatingVisit()
    {
        var f = new Fixture(GateDirection.Entry) { Check = Passed(GymAccessIdentityKind.Student, UserRoleCode.Student) };
        f.Classes = new(f.Work);
        f.Turnstile.Result = TurnstileCommandResult.Accepted(true);
        Assert.True((await f.Execute()).IsSimulated);
        Assert.True(f.Classes.LastSimulated);
        Assert.Equal(0, f.Profiles.Updates);
        Assert.False(f.Records.Current!.AffectsPresence);
    }

    [Fact]
    public async Task ClassCompletionFailureRollsBackResultAndDoesNotRedispatchHardware()
    {
        var f = new Fixture(GateDirection.Entry) { Check = Passed(GymAccessIdentityKind.Student, UserRoleCode.Student) };
        f.Classes = new(f.Work) { FailCompletion = true };
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Execute());
        Assert.Equal(1, f.Turnstile.Calls);
        Assert.Equal(1, f.Work.Rollbacks);
        Assert.Equal(1, f.Work.Commits); // Preparation only; actual hardware success cannot be rolled back.
        Assert.Equal("Requested", f.Records.Current!.Outcome);
    }

    private static readonly DateTime Now = new(2026, 10, 16, 19, 10, 0);

    [Fact]
    public async Task StudentEntryStopsBeforeHardwareUntilClassReservationExists()
    {
        var f = new Fixture(GateDirection.Entry) { Check = Passed(GymAccessIdentityKind.Student, UserRoleCode.Student) };
        var result = await f.Execute();
        Assert.Equal("Denied", result.Outcome);
        Assert.Equal("CLASS_RESERVATION_NOT_IMPLEMENTED", result.Code);
        Assert.Equal(0, f.Turnstile.Calls);
        Assert.False(f.Records.Current!.AffectsPresence);
    }

    [Fact]
    public async Task StaffEntryDispatchesOnce_AndVendorSuccessBecomesPresenceEvent()
    {
        var f = new Fixture(GateDirection.Entry) { Check = Passed(GymAccessIdentityKind.Staff, UserRoleCode.Admin) };
        var id = Guid.NewGuid();
        var first = await f.Execute(id);
        var duplicate = await f.Execute(id);
        Assert.True(first.Succeeded);
        Assert.True(f.Records.Current!.AffectsPresence);
        Assert.Equal("Succeeded", f.Records.Current.Outcome);
        Assert.Equal(1, f.Turnstile.Calls);
        Assert.True(duplicate.Replayed);
        Assert.Equal(0, f.Profiles.Updates);
    }

    [Fact]
    public async Task IdentifiedExitDispatchesDespiteTicketWarnings()
    {
        var f = new Fixture(GateDirection.Exit)
        {
            Check = Passed(GymAccessIdentityKind.IdentifiedUser, UserRoleCode.Student) with
            { WarningCodes = ["GYM_TICKET_EXPIRED"] }
        };
        Assert.True((await f.Execute()).Succeeded);
        Assert.Equal(2, f.Turnstile.LastDoor);
    }

    [Fact]
    public async Task SimulationNeverChangesPresence_AndUnknownNeverPretendsSuccess()
    {
        var simulated = new Fixture(GateDirection.Entry) { Check = Passed(GymAccessIdentityKind.Staff, UserRoleCode.Staff) };
        simulated.Turnstile.Result = TurnstileCommandResult.Accepted(true);
        var simulation = await simulated.Execute();
        Assert.True(simulation.Succeeded);
        Assert.True(simulation.IsSimulated);
        Assert.False(simulated.Records.Current!.AffectsPresence);

        var unknown = new Fixture(GateDirection.Exit);
        unknown.Turnstile.Result = new(TurnstileOutcome.Unknown, false, "CONTROL_COMMAND_TIMEOUT", "unknown");
        var result = await unknown.Execute();
        Assert.False(result.Succeeded);
        Assert.Equal("Unknown", result.Outcome);
        Assert.False(unknown.Records.Current!.AffectsPresence);
    }

    [Fact]
    public async Task CancelledDispatchIsRecordedUnknown_AndRethrownWithoutRetry()
    {
        var f = new Fixture(GateDirection.Exit);
        f.Turnstile.Cancel = true;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Execute());
        Assert.Equal(1, f.Turnstile.Calls);
        Assert.Equal("Unknown", f.Records.Current!.Outcome);
        Assert.Equal("REQUEST_CANCELLED_RESULT_UNKNOWN", f.Records.Current.ResultCode);
    }

    [Fact]
    public async Task FinalPersistenceFailureRollsBackAndCannotReturnSuccess()
    {
        var f = new Fixture(GateDirection.Entry) { Check = Passed(GymAccessIdentityKind.Staff, UserRoleCode.Staff) };
        f.Records.FailComplete = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Execute());
        Assert.Equal(1, f.Turnstile.Calls);
        Assert.Equal(0, f.Work.Commits);
        Assert.Equal(1, f.Work.Rollbacks);
        Assert.Equal("Requested", f.Records.Current!.Outcome);
    }

    private static GymAccessEligibilityResult Passed(GymAccessIdentityKind kind, UserRoleCode role) => new()
    {
        BasicEligibilityPassed = true, Code = "OK", Message = "ok", EvaluatedAt = Now,
        UserId = "U1", IdentityKind = kind, ActiveRoles = [role]
    };

    private sealed class Fixture
    {
        public GymAccessEligibilityResult Check = Passed(GymAccessIdentityKind.IdentifiedUser, UserRoleCode.Student);
        public readonly MemoryRecords Records = new();
        public readonly FakeProfiles Profiles = new();
        public readonly FakeWork Work = new();
        public readonly FakeTurnstile Turnstile = new();
        public FakeClasses? Classes;
        private readonly GateDeviceAuthorization device;
        public Fixture(GateDirection direction)
        {
            Turnstile.IsTransactionOpen = () => Work.InTransaction;
            var id = Guid.NewGuid();
            device = new(new(1, direction == GateDirection.Entry ? "ENTRY_MAIN" : "EXIT_MAIN", "gate",
                direction, true, id), new(id, 1, "Active", Now, null));
        }
        public Task<GatePassageResult> Execute(Guid? id = null)
        {
            var service = new GatePassageService(new FakeEligibility(() => Check), new DeviceRepository(device), Records, Profiles, Work,
                new FakeClock(), Turnstile, new TurnstileSettings { EntryDoorNo = 1, ExitDoorNo = 2 },
                new DeviceTestCommandGate(), NullLogger<GatePassageService>.Instance, Classes);
            return service.ExecuteAsync(device, id ?? Guid.NewGuid(), "0912345678", default);
        }
    }

    private sealed class DeviceRepository(GateDeviceAuthorization device) : IGateDeviceRepository
    {
        public Task<GateDeviceAuthorization?> FindAuthorizationAsync(Guid id, CancellationToken ct) =>
            Task.FromResult<GateDeviceAuthorization?>(id == device.Session.Id ? device : null);
        public Task<GateStation?> FindStationAsync(string code, bool forUpdate, CancellationToken ct) => throw new NotSupportedException();
        public Task ReplaceAsync(GateStation station, Guid sessionId, DateTime now, CancellationToken ct) => throw new NotSupportedException();
        public Task RevokeAsync(GateStation station, string operatorId, DateTime now, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class FakeEligibility(Func<GymAccessEligibilityResult> result) : IGymAccessEligibilityService
    {
        public Task<GymAccessEligibilityResult> CheckEntryAsync(string? phone, CancellationToken ct = default) => Task.FromResult(result());
        public Task<GymAccessEligibilityResult> CheckExitAsync(string? phone, CancellationToken ct = default) => Task.FromResult(result());
    }

    private sealed class MemoryRecords : IGatePassageRepository
    {
        public GatePassageRecord? Current;
        public bool FailComplete;
        public Task<GatePassageRecord?> FindByRequestIdAsync(Guid id, CancellationToken ct) =>
            Task.FromResult(Current?.RequestId == id ? Current : null);
        public Task<bool> TryCreateAsync(Guid id, GateDeviceAuthorization device, string? userId, string identity,
            string roles, DateTime at, CancellationToken ct)
        {
            if (Current?.RequestId == id) return Task.FromResult(false);
            Current = new(id, device.Station.Id, device.Session.Id, device.Station.Direction, "Requested", "REQUESTED",
                userId, identity, false, at, null, false);
            return Task.FromResult(true);
        }
        public Task<bool> CompleteAsync(Guid id, string outcome, string code, bool success, bool simulated,
            bool affects, DateTime at, string? remark, CancellationToken ct)
        {
            if (FailComplete) return Task.FromResult(false);
            Current = Current! with { Outcome = outcome, ResultCode = code, IsSimulated = simulated,
                RespondedAt = at, AffectsPresence = affects };
            return Task.FromResult(true);
        }
    }

    private sealed class FakeTurnstile : ITurnstileCommandClient
    {
        public Func<bool> IsTransactionOpen = () => false;
        public int Calls, LastDoor;
        public bool Cancel;
        public TurnstileCommandResult Result = TurnstileCommandResult.Accepted(false);
        public Task<TurnstileCommandResult> OpenDoorAsync(int controllerSN, int doorNo, CancellationToken ct)
        { Assert.False(IsTransactionOpen()); Calls++; LastDoor = doorNo; if (Cancel) throw new OperationCanceledException(); return Task.FromResult(Result); }
        public Task<TurnstileCommandResult> QueryStatusAsync(int controllerSN, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class FakeProfiles : IStudentProfileRepository
    {
        public int Updates;
        public Task<bool> UpdateLastVisitAsync(string userId, DateTime at, CancellationToken ct) { Updates++; return Task.FromResult(true); }
        public Task AddAsync(StudentProfile profile, CancellationToken ct) => throw new NotSupportedException();
        public Task AddRangeAsync(IReadOnlyList<StudentProfile> profiles, CancellationToken ct) => throw new NotSupportedException();
        public Task<StudentProfile?> FindByUserIdAsync(string userId, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> UpdateCurrentTicketAsync(string userId, CurrentTicketSnapshot snapshot, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> ClearCurrentTicketAsync(string userId, DateTime at, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class FakeWork : IUnitOfWork
    {
        public int Commits, Rollbacks;
        public bool InTransaction;
        public Task BeginAsync(CancellationToken ct) { Assert.False(InTransaction); InTransaction = true; return Task.CompletedTask; }
        public Task CommitAsync(CancellationToken ct) { InTransaction = false; Commits++; return Task.CompletedTask; }
        public Task RollbackAsync(CancellationToken ct) { InTransaction = false; Rollbacks++; return Task.CompletedTask; }
    }

    private sealed class FakeClasses(FakeWork work) : IClassAdmissionService
    {
        public bool Allowed = true, FailCompletion, LastSimulated;
        public int Preparations, Completions;
        public string? LastOutcome;
        public Task LockAsync(CancellationToken ct) { Assert.True(work.InTransaction); return Task.CompletedTask; }
        public Task<ClassPreparation> PrepareEntryAsync(Guid id, string user, CancellationToken ct)
        {
            Assert.True(work.InTransaction);
            Preparations++;
            return Task.FromResult(new ClassPreparation(Allowed, Allowed ? "TEST_READY" : "TEST_CLASS_DENIED", "Test preparation."));
        }
        public Task<ClassCompletion> CompleteAsync(Guid id, string user, GateDirection direction,
            string outcome, bool simulated, DateTime respondedAt, CancellationToken ct)
        {
            Assert.True(work.InTransaction);
            Completions++;
            LastOutcome = outcome;
            LastSimulated = simulated;
            if (FailCompletion) throw new InvalidOperationException("Test completion failure.");
            return Task.FromResult(new ClassCompletion());
        }
    }

    private sealed class FakeClock : IClock
    {
        public DateTime Now() => GatePassageServiceTests.Now;
        public DateOnly Today() => DateOnly.FromDateTime(GatePassageServiceTests.Now);
    }
}
