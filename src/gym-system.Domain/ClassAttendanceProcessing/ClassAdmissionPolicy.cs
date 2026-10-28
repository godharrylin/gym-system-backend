namespace gym_system.Domain.ClassAttendanceProcessing;

public sealed record CourseSessionWindow(int SessionSn, string SessionId, DateTime StartAt,
    DateTime EndAt, string InstructorId, bool Cancelled = false)
{
    public DateTime CutoffAt => StartAt.AddMinutes(10);
    public DateTime ReturnUntil => EndAt.AddMinutes(10);
}

public enum ClassAdmissionKind { RegularEntry, Return, PreEntry }
public sealed record AttendancePermit(int SessionSn, bool Confirmed, bool Eligible);
public sealed record ClassAdmissionDecision(bool Allowed, string Code,
    CourseSessionWindow? Session = null, ClassAdmissionKind? Kind = null);

/// <summary>Time-window selection only; identity, ticket, capacity, and presence require transactional revalidation.</summary>
public static class ClassAdmissionPolicy
{
    public static ClassAdmissionDecision Select(DateTime now, IReadOnlyList<CourseSessionWindow> sessions,
        IReadOnlyList<AttendancePermit> permits)
    {
        var today = DateOnly.FromDateTime(now);
        var courses = sessions.Where(x => !x.Cancelled && DateOnly.FromDateTime(x.StartAt) == today)
            .OrderBy(x => x.StartAt).ThenBy(x => x.SessionSn).ToArray();
        if (courses.Any(x => x.SessionSn <= 0 || string.IsNullOrWhiteSpace(x.SessionId)
            || string.IsNullOrWhiteSpace(x.InstructorId) || x.EndAt <= x.StartAt
            || DateOnly.FromDateTime(x.EndAt) != today)
            || courses.Select(x => x.SessionSn).Distinct().Count() != courses.Length)
            return new(false, "CLASS_SCHEDULE_INVALID");
        for (var i = 1; i < courses.Length; i++)
            if (courses[i].StartAt < courses[i - 1].EndAt.AddMinutes(10))
                return new(false, "CLASS_SCHEDULE_CONFLICT");

        // An existing eligible attendance takes precedence over pre-enrolment in the next course.
        var returns = courses.Where(c => now >= c.CutoffAt && now <= c.ReturnUntil
            && permits.Any(p => p.SessionSn == c.SessionSn && p.Confirmed && p.Eligible)).ToArray();
        if (returns.Length > 1) return new(false, "CLASS_ATTENDANCE_AMBIGUOUS");
        if (returns.Length == 1) return new(true, "CLASS_RETURN_ALLOWED", returns[0], ClassAdmissionKind.Return);

        var current = courses.SingleOrDefault(c => now >= c.StartAt && now <= c.CutoffAt && now < c.EndAt);
        if (current is not null) return new(true, "CLASS_ENTRY_ALLOWED", current, ClassAdmissionKind.RegularEntry);
        for (var i = 0; i + 1 < courses.Length; i++)
        {
            var previous = courses[i];
            var next = courses[i + 1];
            if (now >= previous.EndAt && now <= previous.ReturnUntil && now < next.StartAt)
                return new(true, "CLASS_PREENTRY_ALLOWED", next, ClassAdmissionKind.PreEntry);
        }
        return new(false, "CLASS_WINDOW_CLOSED");
    }
}
