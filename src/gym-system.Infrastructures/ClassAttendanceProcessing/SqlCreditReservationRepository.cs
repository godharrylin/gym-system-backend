using Dapper;
using gym_system.Domain.ClassAttendanceProcessing;
using gym_system.Infrastructures.Connections;

namespace gym_system.Infrastructures.ClassAttendanceProcessing;

internal sealed class SqlCreditReservationRepository(ISqlSession session) : ICreditReservationRepository
{
    private const string Columns = """
        reservation_sn AS ReservationSn, attendance_sn AS AttendanceSn, pass_sn AS PassSn,
        reserved_credits AS ReservedCredits, reservation_state AS State,
        review_from_state AS ReviewFromState, idempotency_key AS IdempotencyKey,
        gate_request_id AS GateRequestId, processing_item_sn AS ProcessingItemSn,
        previous_reservation_sn AS PreviousReservationSn, usage_sn AS UsageSn,
        created_at AS CreatedAt, fixed_at AS FixedAt, released_at AS ReleasedAt,
        consumed_at AS ConsumedAt, updated_at AS UpdatedAt,
        reason_code AS ReasonCode, reason_message AS ReasonMessage
        """;

    private CommandDefinition Command(string sql, object? parameters, CancellationToken ct)
    {
        if (session.Transaction is null)
            throw new InvalidOperationException("Credit reservation operations require a caller-owned transaction.");
        return new(sql, parameters, session.Transaction, cancellationToken: ct);
    }

    public Task<CreditPassCapacity?> LockPassAsync(string studentId, int passSn, CancellationToken ct)
        => session.Connection.QuerySingleOrDefaultAsync<CreditPassCapacity>(Command("""
            SELECT p.pass_sn AS PassSn, p.owner_id AS OwnerId, p.valid_status AS ValidStatus,
                   i.order_items_payment_state AS PaymentState, p.ticket_plan_kind_type AS TicketType,
                   p.credits_remaining AS CreditsRemaining, p.valid_sdate AS ValidStartDate,
                   p.valid_edate AS ValidEndDate, p.ticket_plan_kind_code AS PlanCode
            FROM dbo.sdt_ticket_pass p WITH (UPDLOCK, HOLDLOCK)
            INNER JOIN dbo.order_items i ON i.order_items_sn = p.order_items_sn
            WHERE p.pass_sn = @passSn AND p.owner_id = @studentId;
            """, new { studentId, passSn }, ct));

    public Task<int?> FindPassSnAsync(long reservationSn, string studentId, CancellationToken ct)
        => session.Connection.QuerySingleOrDefaultAsync<int?>(Command("""
            SELECT r.pass_sn FROM dbo.sdt_ticket_credit_reservation r
            INNER JOIN dbo.sdt_ticket_pass p ON p.pass_sn = r.pass_sn
            WHERE r.reservation_sn = @reservationSn AND p.owner_id = @studentId;
            """, new { reservationSn, studentId }, ct));

    public Task<bool> AttendanceBelongsToAsync(int attendanceSn, string studentId, CancellationToken ct)
        => session.Connection.ExecuteScalarAsync<bool>(Command("""
            SELECT CAST(CASE WHEN EXISTS (
                SELECT 1 FROM dbo.sdt_att_record WITH (UPDLOCK, HOLDLOCK)
                WHERE sdt_att_record_sn = @attendanceSn AND user_id = @studentId
            ) THEN 1 ELSE 0 END AS bit);
            """, new { attendanceSn, studentId }, ct));

    public async Task<CreditReservation?> FindByKeyAsync(string key, CancellationToken ct)
    {
        var row = await session.Connection.QuerySingleOrDefaultAsync<Row>(Command(
            $"SELECT {Columns} FROM dbo.sdt_ticket_credit_reservation WITH (UPDLOCK, HOLDLOCK) WHERE idempotency_key = @key;",
            new { key }, ct));
        return row?.ToEntity();
    }

    public async Task<CreditReservation?> FindByIdAsync(long reservationSn, CancellationToken ct)
    {
        var row = await session.Connection.QuerySingleOrDefaultAsync<Row>(Command(
            $"SELECT {Columns} FROM dbo.sdt_ticket_credit_reservation WITH (UPDLOCK, HOLDLOCK) WHERE reservation_sn = @reservationSn;",
            new { reservationSn }, ct));
        return row?.ToEntity();
    }

    public async Task<IReadOnlyList<CreditReservation>> GetByAttendanceAsync(int attendanceSn, CancellationToken ct)
        => (await session.Connection.QueryAsync<Row>(Command(
            $"SELECT {Columns} FROM dbo.sdt_ticket_credit_reservation WITH (UPDLOCK, HOLDLOCK) WHERE attendance_sn = @attendanceSn ORDER BY reservation_sn;",
            new { attendanceSn }, ct))).Select(x => x.ToEntity()).ToArray();

    // Read every state, including history, so invalid raw SQL data is not silently excluded by the hold filter.
    public async Task<IReadOnlyList<CreditReservation>> GetByPassAsync(int passSn, CancellationToken ct)
        => (await session.Connection.QueryAsync<Row>(Command(
            $"SELECT {Columns} FROM dbo.sdt_ticket_credit_reservation WITH (UPDLOCK, HOLDLOCK) WHERE pass_sn = @passSn ORDER BY reservation_sn;",
            new { passSn }, ct))).Select(x => x.ToEntity()).ToArray();

    public async Task<CreditReservation> InsertAsync(CreditReservation reservation, CancellationToken ct)
    {
        var data = reservation.ToData();
        data.Validate();
        if (data.ReservationSn is not null || data.State != CreditReservationState.Reserved)
            throw new InvalidOperationException("Only a new Reserved hold may be inserted.");
        await ValidateRelationsAsync(data, ct);
        var id = await session.Connection.ExecuteScalarAsync<long>(Command("""
            INSERT dbo.sdt_ticket_credit_reservation
                (attendance_sn, pass_sn, reserved_credits, reservation_state, review_from_state,
                 idempotency_key, gate_request_id, processing_item_sn, previous_reservation_sn, usage_sn,
                 created_at, fixed_at, released_at, consumed_at, updated_at, reason_code, reason_message)
            OUTPUT inserted.reservation_sn
            VALUES (@AttendanceSn, @PassSn, @ReservedCredits, 'Reserved', NULL,
                    @IdempotencyKey, @GateRequestId, @ProcessingItemSn, @PreviousReservationSn, NULL,
                    @CreatedAt, NULL, NULL, NULL, @UpdatedAt, @ReasonCode, @ReasonMessage);
            """, data, ct));
        // Identity-dependent self-reference check must occur before the caller commits.
        (data with { ReservationSn = id }).Validate();
        return await FindByIdAsync(id, ct) ?? throw new InvalidOperationException("Inserted reservation was not readable.");
    }

    public async Task<CreditReservation> UpdateAsync(CreditReservation original, CreditReservation replacement, CancellationToken ct)
    {
        var data = replacement.ToData();
        data.Validate();
        if (original.ReservationSn is not long id || data.ReservationSn != id)
            throw new InvalidOperationException("An update must preserve its assigned reservation ID.");
        var locked = await FindByIdAsync(id, ct);
        if (locked?.ToData() != original.ToData())
            throw new InvalidOperationException("Reservation changed after it was read; rollback and re-evaluate.");
        ValidateTransition(original, replacement);
        await ValidateRelationsAsync(data, ct);
        var changed = await session.Connection.ExecuteAsync(Command("""
            UPDATE dbo.sdt_ticket_credit_reservation
            SET reservation_state = @State, review_from_state = @ReviewFromState,
                usage_sn = @UsageSn, fixed_at = @FixedAt, released_at = @ReleasedAt,
                consumed_at = @ConsumedAt, updated_at = @UpdatedAt,
                reason_code = @ReasonCode, reason_message = @ReasonMessage
            WHERE reservation_sn = @ReservationSn AND reservation_state = @ExpectedState;
            """, new
            {
                data.ReservationSn, State = data.State.ToString(),
                ReviewFromState = data.ReviewFromState?.ToString(), data.UsageSn, data.FixedAt,
                data.ReleasedAt, data.ConsumedAt, data.UpdatedAt, data.ReasonCode, data.ReasonMessage,
                ExpectedState = original.State.ToString()
            }, ct));
        if (changed != 1) throw new InvalidOperationException("Reservation state update did not affect exactly one row.");
        return await FindByIdAsync(id, ct) ?? throw new InvalidOperationException("Updated reservation was not readable.");
    }

    private static void ValidateTransition(CreditReservation original, CreditReservation replacement)
    {
        var at = replacement.UpdatedAt;
        var expected = replacement.State switch
        {
            CreditReservationState.Fixed when original.State == CreditReservationState.Reserved => original.Fix(at),
            CreditReservationState.Released => original.Release(at, replacement.ReasonCode!, replacement.ReasonMessage!),
            CreditReservationState.NeedsReview => original.RequireReview(at, replacement.ReasonCode!, replacement.ReasonMessage!),
            CreditReservationState.Consumed => original.Consume(at, replacement.UsageSn!.Value),
            _ when original.State == CreditReservationState.NeedsReview => original.RestoreReviewedState(at),
            _ => throw new InvalidOperationException("Unsupported reservation state transition.")
        };
        if (expected.ToData() != replacement.ToData())
            throw new InvalidOperationException("State transitions cannot change immutable reservation data.");
    }

    private async Task ValidateRelationsAsync(CreditReservationData data, CancellationToken ct)
    {
        var owner = await session.Connection.QuerySingleOrDefaultAsync<string>(Command("""
            SELECT a.user_id FROM dbo.sdt_att_record a
            INNER JOIN dbo.sdt_ticket_pass p ON p.owner_id = a.user_id
            WHERE a.sdt_att_record_sn = @AttendanceSn AND p.pass_sn = @PassSn;
            """, data, ct));
        if (owner is null) throw new InvalidOperationException("Attendance and pass owner do not match.");
        if (data.GateRequestId is Guid request)
        {
            var exists = await session.Connection.ExecuteScalarAsync<bool>(Command("""
                SELECT CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.device_record
                    WHERE request_id = @request AND usr_id = @owner AND direction = 1)
                    THEN 1 ELSE 0 END AS bit);
                """, new { request, owner }, ct));
            if (!exists) throw new InvalidOperationException("Reservation gate source is missing or belongs to another user.");
        }
        if (data.ProcessingItemSn is long item)
        {
            var exists = await session.Connection.ExecuteScalarAsync<bool>(Command("""
                SELECT CAST(CASE WHEN EXISTS (
                    SELECT 1 FROM dbo.class_processing_item i
                    INNER JOIN dbo.class_processing_job j ON j.job_sn = i.job_sn
                    INNER JOIN dbo.sdt_att_record a ON a.cls_scdle_arnge_sn = j.cls_scdle_arnge_sn
                    WHERE i.item_sn = @item AND i.user_id = @owner AND j.phase = 'A'
                      AND a.sdt_att_record_sn = @AttendanceSn
                ) THEN 1 ELSE 0 END AS bit);
                """, new { item, owner, data.AttendanceSn }, ct));
            if (!exists) throw new InvalidOperationException("Reservation processing source is missing or inconsistent.");
        }
        if (data.PreviousReservationSn is long previousId)
        {
            var previous = await FindByIdAsync(previousId, ct);
            if (previous?.State != CreditReservationState.Released || previous.AttendanceSn != data.AttendanceSn)
                throw new InvalidOperationException("Predecessor must be a released hold for the same attendance.");
        }
        if (data.UsageSn is int usage)
        {
            var exists = await session.Connection.ExecuteScalarAsync<bool>(Command("""
                SELECT CAST(CASE WHEN EXISTS (
                    SELECT 1 FROM dbo.sdt_ticket_usage_log u
                    INNER JOIN dbo.sdt_ticket_pass p ON p.pass_id = u.pass_id
                    WHERE u.usage_sn = @usage AND p.pass_sn = @PassSn
                ) THEN 1 ELSE 0 END AS bit);
                """, new { usage, data.PassSn }, ct));
            if (!exists) throw new InvalidOperationException("Consumption usage does not match the reserved pass.");
        }
    }

    private sealed class Row
    {
        public long ReservationSn { get; set; }
        public int AttendanceSn { get; set; }
        public int PassSn { get; set; }
        public int ReservedCredits { get; set; }
        public string State { get; set; } = "";
        public string? ReviewFromState { get; set; }
        public string IdempotencyKey { get; set; } = "";
        public Guid? GateRequestId { get; set; }
        public long? ProcessingItemSn { get; set; }
        public long? PreviousReservationSn { get; set; }
        public int? UsageSn { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? FixedAt { get; set; }
        public DateTime? ReleasedAt { get; set; }
        public DateTime? ConsumedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public string? ReasonCode { get; set; }
        public string? ReasonMessage { get; set; }

        private static CreditReservationState Parse(string value)
            => Enum.TryParse<CreditReservationState>(value, out var parsed)
                && Enum.IsDefined(parsed) && parsed.ToString() == value ? parsed
                : throw new InvalidOperationException("Unrecognized persisted reservation state.");

        public CreditReservation ToEntity() => CreditReservation.Rehydrate(new(
            ReservationSn, AttendanceSn, PassSn, ReservedCredits, Parse(State),
            ReviewFromState is null ? null : Parse(ReviewFromState), IdempotencyKey,
            GateRequestId, ProcessingItemSn, PreviousReservationSn, UsageSn, CreatedAt,
            FixedAt, ReleasedAt, ConsumedAt, UpdatedAt, ReasonCode, ReasonMessage));
    }
}
