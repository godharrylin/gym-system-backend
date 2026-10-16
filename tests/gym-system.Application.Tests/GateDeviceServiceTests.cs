using gym_system.Application.GateDevicesUseCase;
using gym_system.Domain.Entities.Devices;
using gym_system.Domain.Repositories;
using Xunit;

namespace gym_system.Application.Tests;

public sealed class GateDeviceServiceTests
{
    [Fact]
    public async Task PreviewIsReadOnly_AndWrongCodeNeverQueriesDatabase()
    {
        var f = new Fixture();
        Assert.Equal("ENTRY_MAIN", (await f.Service.PreviewAsync("test-entry", default)).Code);
        Assert.Equal(0, f.Repository.Replacements);
        Assert.Equal(0, f.Work.Begins);
        f.Repository.Reads = 0;
        Assert.Equal("PAIRING_CODE_INVALID", (await Assert.ThrowsAsync<GateDeviceException>(
            () => f.Service.PreviewAsync("wrong", default))).Code);
        Assert.Equal(0, f.Repository.Reads);
    }

    [Fact]
    public async Task ConfirmReplacesOldSession_WithoutAutomaticExpiry()
    {
        var f = new Fixture();
        var old = await f.Service.PairAsync("test-entry", "ENTRY_MAIN", null, null, default);
        var current = await f.Service.PairAsync("test-entry", "ENTRY_MAIN", old.Session.Id, null, default);
        Assert.NotEqual(old.Session.Id, current.Session.Id);
        Assert.Null(current.Session.ExpiresAt);
        Assert.Null(await f.Service.FindValidAsync(old.Session.Id, default));
        f.Clock.At = f.Clock.At.AddYears(20);
        Assert.NotNull(await f.Service.FindValidAsync(current.Session.Id, default));
        Assert.Equal(2, f.Work.Commits);
    }

    [Fact]
    public async Task StalePreviewCannotSilentlyReplaceAnotherNewDevice()
    {
        var f = new Fixture();
        var current = await f.Service.PairAsync("test-entry", "ENTRY_MAIN", null, null, default);
        var error = await Assert.ThrowsAsync<GateDeviceException>(() =>
            f.Service.PairAsync("test-entry", "ENTRY_MAIN", null, null, default));
        Assert.Equal("PAIRING_CONFLICT", error.Code);
        Assert.Equal(1, f.Repository.Replacements);
        Assert.Equal(1, f.Work.Rollbacks);
        Assert.NotNull(await f.Service.FindValidAsync(current.Session.Id, default));
    }

    [Fact]
    public async Task SameBrowserRetainsSession_AndSelfRevocationClearsStation()
    {
        var f = new Fixture();
        var current = await f.Service.PairAsync("test-entry", "ENTRY_MAIN", null, null, default);
        var again = await f.Service.PairAsync("test-entry", "ENTRY_MAIN", current.Session.Id, current.Session.Id, default);
        Assert.Equal(current.Session.Id, again.Session.Id);
        Assert.Equal(1, f.Repository.Replacements);
        await f.Service.RevokeSelfAsync(current.Session.Id, default);
        Assert.Null(f.Repository.Station.CurrentSessionId);
        Assert.Null(await f.Service.FindValidAsync(current.Session.Id, default));
        Assert.Equal("DEVICE_SELF", f.Repository.RevokedBy);
    }

    [Fact]
    public async Task ChangedCodeCannotUsePreviewForAnotherStation_AndDisabledStationCannotPair()
    {
        var f = new Fixture();
        Assert.Equal("PAIRING_PREVIEW_INVALID", (await Assert.ThrowsAsync<GateDeviceException>(() =>
            f.Service.PairAsync("test-exit", "ENTRY_MAIN", null, null, default))).Code);
        Assert.Equal(0, f.Work.Begins);
        f.Repository.Station = f.Repository.Station with { IsActive = false };
        Assert.Equal(403, (await Assert.ThrowsAsync<GateDeviceException>(() =>
            f.Service.PairAsync("test-entry", "ENTRY_MAIN", null, null, default))).StatusCode);
        Assert.Equal(0, f.Repository.Replacements);
        Assert.Equal(1, f.Work.Rollbacks);
    }

    [Fact]
    public async Task StorageFailureRollsBackAndDoesNotPretendPairingSucceeded()
    {
        var f = new Fixture();
        f.Repository.FailWrite = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            f.Service.PairAsync("test-entry", "ENTRY_MAIN", null, null, default));
        Assert.Equal(0, f.Work.Commits);
        Assert.Equal(1, f.Work.Rollbacks);
    }

    [Theory]
    [InlineData(false, "Active", true, false)]
    [InlineData(true, "Revoked", true, false)]
    [InlineData(true, "Active", false, false)]
    [InlineData(true, "Active", true, true)]
    public void EveryAuthorizationChecksStationPointerAndStatus(bool active, string status, bool current, bool expected)
    {
        var id = Guid.NewGuid();
        var authorization = new GateDeviceAuthorization(
            new(1, "ENTRY_MAIN", "entry", GateDirection.Entry, active, current ? id : Guid.NewGuid()),
            new(id, 1, status, DateTime.MinValue, null));
        Assert.Equal(expected, authorization.IsValidAt(DateTime.Now));
    }

    private sealed class Fixture
    {
        public readonly FakeRepository Repository = new();
        public readonly FakeWork Work = new();
        public readonly FakeClock Clock = new();
        public GateDeviceService Service { get; }
        public Fixture() => Service = new(Repository, Work, Clock,
            new() { Enabled = true, EntryPairingCode = "test-entry", ExitPairingCode = "test-exit" });
    }

    private sealed class FakeClock : IClock
    {
        public DateTime At = new(2026, 10, 16, 19, 10, 0);
        public DateTime Now() => At;
        public DateOnly Today() => DateOnly.FromDateTime(At);
    }

    private sealed class FakeWork : IUnitOfWork
    {
        public int Begins, Commits, Rollbacks;
        public Task BeginAsync(CancellationToken ct) { Begins++; return Task.CompletedTask; }
        public Task CommitAsync(CancellationToken ct) { Commits++; return Task.CompletedTask; }
        public Task RollbackAsync(CancellationToken ct) { Rollbacks++; return Task.CompletedTask; }
    }

    private sealed class FakeRepository : IGateDeviceRepository
    {
        public GateStation Station = new(1, "ENTRY_MAIN", "entry", GateDirection.Entry, true, null);
        public readonly Dictionary<Guid, GateDeviceSession> Sessions = [];
        public int Reads, Replacements;
        public bool FailWrite;
        public string? RevokedBy;
        public Task<GateStation?> FindStationAsync(string code, bool forUpdate, CancellationToken ct)
        { Reads++; return Task.FromResult<GateStation?>(Station.Code == code ? Station : null); }
        public Task<GateDeviceAuthorization?> FindAuthorizationAsync(Guid id, CancellationToken ct) =>
            Task.FromResult(Sessions.TryGetValue(id, out var session) ? new GateDeviceAuthorization(Station, session) : null);
        public Task ReplaceAsync(GateStation station, Guid id, DateTime now, CancellationToken ct)
        {
            if (FailWrite) throw new InvalidOperationException("storage failure");
            Replacements++;
            if (station.CurrentSessionId is { } old) Sessions[old] = Sessions[old] with { Status = "Revoked" };
            Sessions[id] = new(id, station.Id, "Active", now, null);
            Station = station with { CurrentSessionId = id };
            return Task.CompletedTask;
        }
        public Task RevokeAsync(GateStation station, string operatorId, DateTime now, CancellationToken ct)
        {
            RevokedBy = operatorId;
            Sessions[station.CurrentSessionId!.Value] = Sessions[station.CurrentSessionId.Value] with { Status = "Revoked" };
            Station = station with { CurrentSessionId = null };
            return Task.CompletedTask;
        }
    }
}
