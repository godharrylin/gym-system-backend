namespace gym_system.Domain.ClassAttendanceProcessing;

public enum CreditReservationState { Reserved, Fixed, Released, Consumed, NeedsReview }

/// <summary>
/// State transitions only. Callers must validate identity, ticket eligibility, capacity,
/// idempotency, and historical evidence in a database transaction before persisting.
/// </summary>
public sealed record CreditReservation
{
    private CreditReservation() { }

    public long? ReservationSn { get; private init; }
    public int AttendanceSn { get; private init; }
    public int PassSn { get; private init; }
    public int ReservedCredits => 1;
    public CreditReservationState State { get; private init; }
    public CreditReservationState? ReviewFromState { get; private init; }
    public string IdempotencyKey { get; private init; } = string.Empty;
    public Guid? GateRequestId { get; private init; }
    public long? ProcessingItemSn { get; private init; }
    public long? PreviousReservationSn { get; private init; }
    public int? UsageSn { get; private init; }
    public DateTime CreatedAt { get; private init; }
    public DateTime UpdatedAt { get; private init; }
    public DateTime? FixedAt { get; private init; }
    public DateTime? ReleasedAt { get; private init; }
    public DateTime? ConsumedAt { get; private init; }
    public string? ReasonCode { get; private init; }
    public string? ReasonMessage { get; private init; }
    public bool HoldsCredit => State is CreditReservationState.Reserved
        or CreditReservationState.Fixed or CreditReservationState.NeedsReview;

    public static CreditReservation Reserve(int attendanceSn, int passSn, string idempotencyKey,
        DateTime createdAt, Guid? gateRequestId = null, long? processingItemSn = null,
        long? previousReservationSn = null)
    {
        return new CreditReservation
        {
            AttendanceSn = attendanceSn, PassSn = passSn, IdempotencyKey = idempotencyKey,
            GateRequestId = gateRequestId, ProcessingItemSn = processingItemSn,
            PreviousReservationSn = previousReservationSn, State = CreditReservationState.Reserved,
            CreatedAt = createdAt, UpdatedAt = createdAt
        }.Validated();
    }

    public CreditReservation Fix(DateTime at)
    {
        RequireState(CreditReservationState.Reserved);
        RequireTime(at);
        return (this with { State = CreditReservationState.Fixed, FixedAt = at, UpdatedAt = at }).Validated();
    }

    public CreditReservation RequireReview(DateTime at, string code, string message)
    {
        if (State is not (CreditReservationState.Reserved or CreditReservationState.Fixed))
            throw new InvalidOperationException("Only a live reservation can enter review.");
        RequireTime(at);
        return (this with { State = CreditReservationState.NeedsReview, ReviewFromState = State,
            UpdatedAt = at, ReasonCode = code, ReasonMessage = message }).Validated();
    }

    // Only call after evidence has been revalidated; this method grants no authorization.
    public CreditReservation RestoreReviewedState(DateTime at)
    {
        RequireState(CreditReservationState.NeedsReview);
        RequireTime(at);
        return (this with { State = ReviewFromState!.Value, ReviewFromState = null, UpdatedAt = at }).Validated();
    }

    public CreditReservation Release(DateTime at, string code, string message)
    {
        if (State != CreditReservationState.Reserved
            && !(State == CreditReservationState.NeedsReview && ReviewFromState == CreditReservationState.Reserved))
            throw new InvalidOperationException("A fixed attendance cannot be released by ordinary exit/failure handling.");
        RequireTime(at);
        return (this with { State = CreditReservationState.Released, ReviewFromState = null,
            ReleasedAt = at, UpdatedAt = at, ReasonCode = code, ReasonMessage = message }).Validated();
    }

    // This transition alone does not debit a pass; settlement must persist both atomically.
    public CreditReservation Consume(DateTime at, int usageSn)
    {
        RequireState(CreditReservationState.Fixed);
        RequireTime(at);
        if (usageSn <= 0) throw new ArgumentOutOfRangeException(nameof(usageSn));
        return (this with { State = CreditReservationState.Consumed, UsageSn = usageSn,
            ConsumedAt = at, UpdatedAt = at }).Validated();
    }

    public CreditReservationData ToData() => new(ReservationSn, AttendanceSn, PassSn,
        ReservedCredits, State, ReviewFromState, IdempotencyKey, GateRequestId,
        ProcessingItemSn, PreviousReservationSn, UsageSn, CreatedAt, FixedAt,
        ReleasedAt, ConsumedAt, UpdatedAt, ReasonCode, ReasonMessage);

    // Reject invalid stored/imported data rather than allowing it into business processing.
    public static CreditReservation Rehydrate(CreditReservationData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        data.Validate();
        if (data.ReservationSn is null)
            throw new ArgumentException("A persisted reservation ID is required.", nameof(data));
        return new CreditReservation
        {
            ReservationSn = data.ReservationSn, AttendanceSn = data.AttendanceSn,
            PassSn = data.PassSn, State = data.State, ReviewFromState = data.ReviewFromState,
            IdempotencyKey = data.IdempotencyKey, GateRequestId = data.GateRequestId,
            ProcessingItemSn = data.ProcessingItemSn, PreviousReservationSn = data.PreviousReservationSn,
            UsageSn = data.UsageSn, CreatedAt = data.CreatedAt, FixedAt = data.FixedAt,
            ReleasedAt = data.ReleasedAt, ConsumedAt = data.ConsumedAt, UpdatedAt = data.UpdatedAt,
            ReasonCode = data.ReasonCode, ReasonMessage = data.ReasonMessage
        };
    }

    private CreditReservation Validated()
    {
        ToData().Validate();
        return this;
    }

    private void RequireState(CreditReservationState expected)
    {
        if (State != expected) throw new InvalidOperationException($"Expected {expected}, got {State}.");
    }

    private void RequireTime(DateTime at)
    {
        if (at < UpdatedAt) throw new ArgumentException("Processing time cannot precede the last transition.", nameof(at));
    }

}
