using gym_system.Application.ClassAttendanceProcessing;
using gym_system.Domain.ClassAttendanceProcessing;
using Xunit;

namespace gym_system.Application.Tests;

public sealed class ClassPresenceHistoryTests
{
    private static readonly DateTime Cutoff = new(2026, 10, 17, 19, 10, 0);
    private static ClassGateEvent Event(int direction, DateTime response, string outcome = "Succeeded",
        string identity = "Student", bool affectsPresence = true)
        => new(Guid.NewGuid(), direction, identity, outcome, response.AddSeconds(-1), response, affectsPresence, null);

    [Theory]
    [InlineData(-1, true)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public void ReceiptAtCutoffIsIncludedButLaterReceiptIsNot(int milliseconds, bool inside)
    {
        var entry = Event(1, Cutoff.AddMilliseconds(milliseconds));
        var result = ClassPresenceHistory.At([entry], Cutoff);
        Assert.True(result.Known);
        Assert.Equal(inside, result.Inside);
        Assert.Equal(inside, result.Student);
    }

    [Fact]
    public void DepartureBeforeCutoffDoesNotAttendEvenIfPreviouslyInside()
    {
        var result = ClassPresenceHistory.At([Event(1, Cutoff.AddMinutes(-10)), Event(2, Cutoff.AddMinutes(-5))], Cutoff);
        Assert.True(result.Known);
        Assert.False(result.Inside);
    }

    [Fact]
    public void LateReplayUsesOriginalCutoffRatherThanCurrentPresence()
    {
        var result = ClassPresenceHistory.At([Event(1, Cutoff.AddMinutes(-2)), Event(2, Cutoff.AddMinutes(1))], Cutoff);
        Assert.True(result.Inside);
        Assert.True(result.Student);
    }

    [Theory]
    [InlineData("Requested")]
    [InlineData("Unknown")]
    [InlineData("Dispatched")]
    [InlineData(null)]
    public void UnresolvedAttemptDoesNotGuessAttendance(string? outcome)
    {
        var attempt = Event(1, Cutoff) with { Outcome = outcome, RespondedAt = null };
        Assert.Equal("GATE_RESULT_UNRESOLVED", ClassPresenceHistory.At([attempt], Cutoff).Reason);
    }

    [Fact]
    public void SameTimestampDoesNotGuessEventOrdering()
        => Assert.Equal("GATE_EVENTS_SAME_TIME", ClassPresenceHistory.At([Event(1, Cutoff), Event(2, Cutoff)], Cutoff).Reason);

    [Fact]
    public void SuccessWithoutReceiptTimeRequiresReview()
        => Assert.Equal("GATE_HISTORY_INCOMPLETE",
            ClassPresenceHistory.At([Event(1, Cutoff) with { RespondedAt = null }], Cutoff).Reason);

    [Fact]
    public void ReceiptBeforeRequestRequiresReview()
        => Assert.Equal("GATE_HISTORY_INCOMPLETE",
            ClassPresenceHistory.At([Event(1, Cutoff) with { RequestedAt = Cutoff.AddSeconds(1), RespondedAt = Cutoff.AddSeconds(-1) }], Cutoff.AddSeconds(2)).Reason);

    [Fact]
    public void KnownLateReceiptRoundedToCutoffDoesNotBecomeEligible()
        => Assert.Equal("GATE_CUTOFF_PRECISION_CONFLICT",
            ClassPresenceHistory.At([Event(1, Cutoff) with { ResultCode = "CLASS_ENTRY_LATE" }], Cutoff).Reason);

    [Fact]
    public void DeviceTestAndFailedAttemptsDoNotChangePresence()
    {
        var result = ClassPresenceHistory.At([Event(1, Cutoff, affectsPresence: false), Event(1, Cutoff, "Failed")], Cutoff);
        Assert.True(result.Known);
        Assert.False(result.Inside);
    }

    [Fact]
    public void StaffEntryIsInsideButNotStudentAttendance()
    {
        var result = ClassPresenceHistory.At([Event(1, Cutoff, identity: "Staff")], Cutoff);
        Assert.True(result.Inside);
        Assert.False(result.Student);
    }

    [Fact]
    public void CurrentPreparationCanExcludeOwnUnresolvedRequest()
    {
        var unresolved = Event(1, Cutoff, "Requested");
        Assert.True(ClassPresenceHistory.At([unresolved], Cutoff, unresolved.RequestId).Known);
        Assert.False(ClassPresenceHistory.At([unresolved], Cutoff).Known);
    }
}
