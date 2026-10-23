using gym_system.Domain.ClassAttendanceProcessing;

namespace gym_system.Application.ClassAttendanceProcessing;

public sealed record ClassPresence(bool Known, bool Inside, bool Student, string? Reason = null);

public static class ClassPresenceHistory
{
    public static ClassPresence At(IReadOnlyList<ClassGateEvent> events, DateTime cutoff, Guid? excludeRequest = null)
    {
        var relevant = events.Where(x => x.RequestId != excludeRequest || excludeRequest is null)
            .Where(x => x.RequestedAt <= cutoff).ToArray();
        if (relevant.Any(x => x.Outcome is null or "Requested" or "Dispatched" or "Unknown"))
            return new(false, false, false, "GATE_RESULT_UNRESOLVED");
        if (relevant.Any(x => x.AffectsPresence && x.Outcome == "Succeeded"
            && (x.RespondedAt is null || x.RespondedAt < x.RequestedAt)))
            return new(false, false, false, "GATE_HISTORY_INCOMPLETE");
        var successful = relevant.Where(x => x.AffectsPresence && x.Outcome == "Succeeded"
            && x.RespondedAt is DateTime at && at <= cutoff).OrderBy(x => x.RespondedAt).ToArray();
        if (successful.Any(x => x.Direction is not (1 or 2) || x.IdentityKind is null))
            return new(false, false, false, "GATE_HISTORY_INCOMPLETE");
        if (successful.GroupBy(x => x.RespondedAt).Any(x => x.Count() > 1))
            return new(false, false, false, "GATE_EVENTS_SAME_TIME");
        // Do not recover lost sub-millisecond cutoff precision by pretending a known late entry was on time.
        if (successful.Any(x => x.ResultCode == "CLASS_ENTRY_LATE" && x.RespondedAt == cutoff))
            return new(false, false, false, "GATE_CUTOFF_PRECISION_CONFLICT");
        var last = successful.LastOrDefault();
        return last is null ? new(true, false, false)
            : new(true, last.Direction == 1, last.Direction == 1 && last.IdentityKind == "Student");
    }
}
