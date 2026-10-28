namespace gym_system.Domain.ClassAttendanceProcessing;

/// <summary>Persistence snapshot. Validate before accepting stored data or writing any state.</summary>
public sealed record CreditReservationData(
    long? ReservationSn,
    int AttendanceSn,
    int PassSn,
    int ReservedCredits,
    CreditReservationState State,
    CreditReservationState? ReviewFromState,
    string IdempotencyKey,
    Guid? GateRequestId,
    long? ProcessingItemSn,
    long? PreviousReservationSn,
    int? UsageSn,
    DateTime CreatedAt,
    DateTime? FixedAt,
    DateTime? ReleasedAt,
    DateTime? ConsumedAt,
    DateTime UpdatedAt,
    string? ReasonCode,
    string? ReasonMessage)
{
    public void Validate()
    {
        if (ReservationSn <= 0 || AttendanceSn <= 0 || PassSn <= 0)
            throw new ArgumentException("Reservation (when assigned), attendance and pass IDs must be positive.");
        if (ReservedCredits != 1)
            throw new ArgumentException("Exactly one credit must be reserved.");
        if (!Enum.IsDefined(State))
            throw new ArgumentException("Unknown reservation state.");
        if (string.IsNullOrWhiteSpace(IdempotencyKey) || IdempotencyKey.Length > 160
            || IdempotencyKey.Any(c => c < 33 || c > 126))
            throw new ArgumentException("A stable printable ASCII operation key is required.");
        if (GateRequestId is null && ProcessingItemSn is null)
            throw new ArgumentException("A gate request or processing item is required.");
        if (GateRequestId == Guid.Empty || ProcessingItemSn <= 0 || PreviousReservationSn <= 0
            || (ReservationSn is not null && PreviousReservationSn == ReservationSn))
            throw new ArgumentException("Source and predecessor identifiers are invalid.");

        var review = State == CreditReservationState.NeedsReview;
        if (review ? ReviewFromState is not (CreditReservationState.Reserved or CreditReservationState.Fixed)
                   : ReviewFromState is not null)
            throw new ArgumentException("Review must preserve a live original stage; other states must clear it.");

        var requiresFixedTime = State is CreditReservationState.Fixed or CreditReservationState.Consumed
            || (review && ReviewFromState == CreditReservationState.Fixed);
        var forbidsFixedTime = State == CreditReservationState.Reserved
            || (review && ReviewFromState == CreditReservationState.Reserved);
        if ((requiresFixedTime && FixedAt is null) || (forbidsFixedTime && FixedAt is not null))
            throw new ArgumentException("Fixed timestamp does not match the reservation stage.");
        if ((State == CreditReservationState.Released) != (ReleasedAt is not null))
            throw new ArgumentException("Release timestamp must exist only for Released.");
        if (State == CreditReservationState.Consumed)
        {
            if (ConsumedAt is null || UsageSn is null || UsageSn <= 0)
                throw new ArgumentException("Consumed requires a timestamp and a valid usage record ID.");
        }
        else if (ConsumedAt is not null || UsageSn is not null)
            throw new ArgumentException("Only Consumed may reference consumption time and usage.");

        if (ReasonCode?.Length > 60 || ReasonMessage?.Length > 1000
            || (State is CreditReservationState.Released or CreditReservationState.NeedsReview
                && (string.IsNullOrWhiteSpace(ReasonCode) || string.IsNullOrWhiteSpace(ReasonMessage))))
            throw new ArgumentException("Released/review states require a reason code and message within storage limits.");
        if (UpdatedAt < CreatedAt || InvalidTimestamp(FixedAt) || InvalidTimestamp(ReleasedAt)
            || InvalidTimestamp(ConsumedAt) || (FixedAt is not null && ConsumedAt < FixedAt))
            throw new ArgumentException("Actual processing timestamps are inconsistent.");
    }

    private bool InvalidTimestamp(DateTime? value) => value is not null && (value < CreatedAt || value > UpdatedAt);
}
