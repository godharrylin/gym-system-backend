using gym_system.Domain.ClassAttendanceProcessing;
using Xunit;

namespace gym_system.Application.Tests;

public sealed class ClassAdmissionPolicyTests
{
    [Theory]
    [InlineData(0, null, "RegularEntry", false, false)]
    [InlineData(1, 0L, "RegularEntry", false, false)]
    [InlineData(1, null, "Unknown", false, false)]
    [InlineData(1, null, "RegularEntry", false, true)]
    [InlineData(1, 2L, "Return", true, false)]
    [InlineData(1, 2L, "Return", false, true)]
    public void InvalidGateBindingCannotGuessOwnershipOrPreparationFlags(int attendance, long? reservation,
        string kind, bool pending, bool held)
        => Assert.Throws<ArgumentException>(() => new ClassGateContext(attendance, reservation, kind, pending, held).Validate());

    [Theory]
    [InlineData("RegularEntry", true, true)]
    [InlineData("PreEntry", true, true)]
    [InlineData("Return", false, false)]
    public void ValidGateBindingPreservesPreparationContext(string kind, bool pending, bool held)
        => new ClassGateContext(1, 2, kind, pending, held).Validate();

    private static readonly DateTime Day = new(2026, 10, 17);
    private static CourseSessionWindow Course(int id, int hour, int minute = 0)
        => new(id, "SESSION" + id, Day.AddHours(hour).AddMinutes(minute),
            Day.AddHours(hour + 1).AddMinutes(minute), "coach");

    [Theory]
    [InlineData(0, true)]
    [InlineData(600000, true)]
    [InlineData(600001, false)]
    [InlineData(-1, false)]
    public void RegularEntryIncludesExactlyTheTenthMinute(int milliseconds, bool allowed)
    {
        var course = Course(1, 19);
        Assert.Equal(allowed, ClassAdmissionPolicy.Select(course.StartAt.AddMilliseconds(milliseconds), [course], []).Allowed);
    }

    [Fact]
    public void ExistingAttendanceReturnsUntilEndPlusTenMinutesIncludingLastCourse()
    {
        var course = Course(1, 19);
        var permit = new AttendancePermit(1, true, true);
        Assert.Equal(ClassAdmissionKind.Return, ClassAdmissionPolicy.Select(Day.AddHours(20).AddMinutes(10), [course], [permit]).Kind);
        Assert.False(ClassAdmissionPolicy.Select(Day.AddHours(20).AddMinutes(10).AddMilliseconds(1), [course], [permit]).Allowed);
        Assert.False(ClassAdmissionPolicy.Select(Day.AddHours(20).AddMinutes(5), [course], []).Allowed);
    }

    [Fact]
    public void GapPrefersPreviousReturnOtherwisePreEnrolsNextOnlyWithinTenMinutes()
    {
        var previous = Course(1, 18);
        var next = Course(2, 19, 30);
        var now = Day.AddHours(19).AddMinutes(5);
        var returning = ClassAdmissionPolicy.Select(now, [previous, next], [new(1, true, true)]);
        Assert.Equal(previous, returning.Session);
        Assert.Equal(ClassAdmissionKind.Return, returning.Kind);
        var newStudent = ClassAdmissionPolicy.Select(now, [previous, next], []);
        Assert.Equal(next, newStudent.Session);
        Assert.Equal(ClassAdmissionKind.PreEntry, newStudent.Kind);
        Assert.False(ClassAdmissionPolicy.Select(Day.AddHours(19).AddMinutes(11), [previous, next], []).Allowed);
        Assert.Equal(ClassAdmissionKind.RegularEntry,
            ClassAdmissionPolicy.Select(next.StartAt, [previous, next], []).Kind);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void PendingOrIneligibleAttendanceDoesNotGrantReturn(bool confirmed, bool eligible)
    {
        var course = Course(1, 19);
        Assert.False(ClassAdmissionPolicy.Select(Day.AddHours(19).AddMinutes(30), [course], [new(1, confirmed, eligible)]).Allowed);
    }

    [Fact]
    public void NoCourseCancelledCourseOrYesterdayPresenceDoesNotGrantEntry()
    {
        Assert.False(ClassAdmissionPolicy.Select(Day.AddHours(19), [], []).Allowed);
        Assert.False(ClassAdmissionPolicy.Select(Day.AddHours(19), [Course(1, 19) with { Cancelled = true }], []).Allowed);
        Assert.False(ClassAdmissionPolicy.Select(Day.AddDays(1).AddHours(19), [Course(1, 19)], [new(1, true, true)]).Allowed);
    }

    [Fact]
    public void ConflictingScheduleFailsClosedAndLateEntryDoesNotBecomePreEntry()
    {
        Assert.Equal("CLASS_SCHEDULE_CONFLICT", ClassAdmissionPolicy.Select(Day.AddHours(19), [Course(1, 18), Course(2, 19)], []).Code);
        Assert.False(ClassAdmissionPolicy.Select(Day.AddHours(19).AddMinutes(20), [Course(1, 19), Course(2, 20, 10)], []).Allowed);
    }
}
