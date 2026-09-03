using Dapper;
using gym_system.Domain.Entities.Members;
using gym_system.Domain.Entities.Tickets;
using gym_system.Domain.Enums;
using gym_system.Domain.Exceptions;
using gym_system.Domain.Repositories;
using gym_system.Infrastructures.Connections;
using Microsoft.Data.SqlClient;

namespace gym_system.Infrastructures
{
    internal sealed class SqlTicketPassRepository : ITicketPassRepository
    {
        private readonly ISqlSession _session;

        public SqlTicketPassRepository(ISqlSession session)
        {
            _session = session;
        }

        public async Task<IReadOnlyList<TicketPassPersistenceResult>> AddRangeAsync(
            IReadOnlyList<TicketPass> passes,
            OrderPersistenceResult orderPersistence,
            CancellationToken ct)
        {
            var results = new List<TicketPassPersistenceResult>(passes.Count);
            foreach (var pass in passes)
            {
                var item = orderPersistence.Items.First(x => x.ClientItemId == pass.OrderItemId);
                var row = await InsertAsync(pass, orderPersistence.OrderSn, item.OrderItemSn, ct);
                results.Add(new TicketPassPersistenceResult
                {
                    ClientPassId = pass.Id,
                    OwnerId = pass.OwnerId,
                    PassSn = row.pass_sn,
                    PassId = row.pass_id
                });
            }

            return results;
        }

        public async Task<RenewalSourcePass?> FindLatestRenewalSourceAsync(
            string ownerId,
            string familyCode,
            bool acquireLock,
            CancellationToken ct)
        {
            var lockHint = acquireLock ? " WITH (UPDLOCK, HOLDLOCK)" : string.Empty;
            var sql = $$"""
                SELECT TOP (1)
                    p.pass_sn,
                    k.ticket_plan_family_code,
                    p.valid_status,
                    p.valid_sdate,
                    p.valid_edate,
                    p.ended_at,
                    p.end_reason,
                    CAST(CASE WHEN EXISTS
                    (
                        SELECT 1
                        FROM dbo.sdt_ticket_pass AS child
                        WHERE child.renewed_from_pass_sn = p.pass_sn
                          AND child.valid_status <> 'Cancelled'
                    ) THEN 1 ELSE 0 END AS bit) AS has_non_cancelled_renewal,
                    CAST(CASE WHEN EXISTS
                    (
                        SELECT 1
                        FROM dbo.sdt_ticket_pass AS child
                        WHERE child.renewed_from_pass_sn = p.pass_sn
                          AND child.valid_status = 'Cancelled'
                    ) THEN 1 ELSE 0 END AS bit) AS has_cancelled_renewal,
                    (
                        SELECT MAX(child.ended_at)
                        FROM dbo.sdt_ticket_pass AS child
                        WHERE child.renewed_from_pass_sn = p.pass_sn
                          AND child.valid_status = 'Cancelled'
                    ) AS last_cancelled_renewal_at
                FROM dbo.sdt_ticket_pass AS p{{lockHint}}
                INNER JOIN dbo.order_items AS i
                    ON i.order_items_sn = p.order_items_sn
                INNER JOIN dbo.ticket_plan_kind AS k
                    ON k.ticket_plan_kind_code = p.ticket_plan_kind_code
                WHERE p.owner_id = @ownerId
                  AND i.order_items_payment_state = 'Paid'
                  AND p.valid_status IN ('Active', 'Expire', 'Depleted')
                  AND p.valid_sdate IS NOT NULL
                  AND k.ticket_plan_family_code = @familyCode
                ORDER BY p.valid_sdate DESC, p.pass_sn DESC;
                """;

            var row = await _session.Connection.QueryFirstOrDefaultAsync<RenewalSourcePassRow>(
                new CommandDefinition(
                    sql,
                    new { ownerId, familyCode },
                    transaction: _session.Transaction,
                    cancellationToken: ct));

            if (row is null || string.IsNullOrWhiteSpace(row.ticket_plan_family_code))
            {
                return null;
            }

            return new RenewalSourcePass
            {
                PassSn = row.pass_sn,
                FamilyCode = row.ticket_plan_family_code,
                ValidStatus = Enum.Parse<TicketValidStatus>(row.valid_status, ignoreCase: true),
                ValidStartDate = row.valid_sdate is null
                    ? null
                    : DateOnly.FromDateTime(row.valid_sdate.Value),
                ValidEndDate = row.valid_edate is null
                    ? null
                    : DateOnly.FromDateTime(row.valid_edate.Value),
                EndedAt = row.ended_at,
                EndReason = string.IsNullOrWhiteSpace(row.end_reason)
                    ? null
                    : Enum.Parse<TicketEndReason>(row.end_reason, ignoreCase: true),
                HasNonCancelledRenewal = row.has_non_cancelled_renewal,
                HasCancelledRenewal = row.has_cancelled_renewal,
                LastCancelledRenewalAt = row.last_cancelled_renewal_at
            };
        }

        public async Task<bool> HasQueuedPassAsync(string ownerId, CancellationToken ct)
        {
            const string sql = """
                SELECT CAST(CASE WHEN EXISTS
                (
                    SELECT 1
                    FROM dbo.sdt_ticket_pass AS p
                    INNER JOIN dbo.order_items AS i
                        ON i.order_items_sn = p.order_items_sn
                    WHERE p.owner_id = @ownerId
                      AND p.valid_status = 'UnActive'
                      AND i.order_items_payment_state = 'Paid'
                      AND i.order_items_paid_at IS NOT NULL
                ) THEN 1 ELSE 0 END AS bit);
                """;

            return await _session.Connection.ExecuteScalarAsync<bool>(
                new CommandDefinition(
                    sql,
                    new { ownerId },
                    transaction: _session.Transaction,
                    cancellationToken: ct));
        }

        public async Task LockOwnerAsync(string ownerId, CancellationToken ct)
        {
            const string sql = """
                SELECT usr_id
                FROM dbo.sdt_profile WITH (UPDLOCK, HOLDLOCK)
                WHERE usr_id = @ownerId;
                """;
            var lockedOwnerId = await _session.Connection.QuerySingleOrDefaultAsync<string>(
                new CommandDefinition(
                    sql,
                    new { ownerId },
                    transaction: _session.Transaction,
                    cancellationToken: ct));
            if (lockedOwnerId is null)
            {
                throw new InvalidOperationException("學生沒有 Profile");
            }
        }

        public async Task<CancelledTicketPassResult?> CancelQueuedRenewalAsync(
            string passId,
            DateTime cancelledAt,
            string operatorId,
            CancellationToken ct)
        {
            const string findOwnerSql = """
                SELECT owner_id
                FROM dbo.sdt_ticket_pass
                WHERE pass_id = @passId;
                """;
            var ownerId = await _session.Connection.QuerySingleOrDefaultAsync<string>(
                new CommandDefinition(
                    findOwnerSql,
                    new { passId },
                    transaction: _session.Transaction,
                    cancellationToken: ct));
            if (ownerId is null)
            {
                return null;
            }

            await LockOwnerAsync(ownerId, ct);

            const string findSql = """
                SELECT
                    p.pass_sn,
                    p.pass_id,
                    p.owner_id,
                    p.renewed_from_pass_sn,
                    p.ticket_plan_kind_type,
                    p.valid_status,
                    p.credits_total,
                    p.credits_remaining
                FROM dbo.sdt_ticket_pass AS p WITH (UPDLOCK, HOLDLOCK)
                WHERE p.pass_id = @passId;
                """;
            var row = await _session.Connection.QuerySingleOrDefaultAsync<CancellablePassRow>(
                new CommandDefinition(
                    findSql,
                    new { passId },
                    transaction: _session.Transaction,
                    cancellationToken: ct));
            if (row is null)
            {
                return null;
            }

            if (row.renewed_from_pass_sn is null)
            {
                throw new InvalidOperationException("只有續約票可以取消");
            }

            const string lockSourceSql = """
                SELECT pass_sn
                FROM dbo.sdt_ticket_pass WITH (UPDLOCK, HOLDLOCK)
                WHERE pass_sn = @sourcePassSn;
                """;
            var sourcePassSn = await _session.Connection.QuerySingleOrDefaultAsync<int?>(
                new CommandDefinition(
                    lockSourceSql,
                    new { sourcePassSn = row.renewed_from_pass_sn.Value },
                    transaction: _session.Transaction,
                    cancellationToken: ct));
            if (sourcePassSn is null)
            {
                throw new InvalidOperationException("續約來源票不存在");
            }

            if (!row.valid_status.Equals("UnActive", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("只有尚未啟用的續約票可以取消");
            }

            if (row.ticket_plan_kind_type.Equals("PACK", StringComparison.OrdinalIgnoreCase)
                && row.credits_remaining != row.credits_total)
            {
                throw new InvalidOperationException("已使用的續約票不能取消");
            }

            const string cancelSql = """
                UPDATE dbo.sdt_ticket_pass
                SET valid_status = 'Cancelled',
                    ended_at = @cancelledAt,
                    end_reason = 'Cancelled',
                    update_dt = @cancelledAt,
                    update_pn = @operatorId
                WHERE pass_sn = @passSn
                  AND valid_status = 'UnActive';
                """;
            var affected = await _session.Connection.ExecuteAsync(
                new CommandDefinition(
                    cancelSql,
                    new { passSn = row.pass_sn, cancelledAt, operatorId },
                    transaction: _session.Transaction,
                    cancellationToken: ct));
            if (affected != 1)
            {
                throw new InvalidOperationException("票券狀態已變更，請重新整理");
            }

            return new CancelledTicketPassResult
            {
                PassId = row.pass_id,
                OwnerId = row.owner_id,
                SourcePassSn = row.renewed_from_pass_sn.Value
            };
        }

        public async Task<CurrentTicketSnapshot?> ReconcileCurrentAsync(
            string ownerId,
            DateOnly today,
            DateTime updatedAt,
            string operatorId,
            CancellationToken ct)
        {
            await LockOwnerAsync(ownerId, ct);

            var todayDate = today.ToDateTime(TimeOnly.MinValue);
            const string activeSql = """
                SELECT
                    p.pass_sn,
                    p.pass_id,
                    p.ticket_plan_kind_type,
                    p.valid_status,
                    p.valid_edate,
                    p.credits_remaining,
                    p.ended_at,
                    p.end_reason
                FROM dbo.sdt_ticket_pass AS p
                INNER JOIN dbo.order_items AS i
                    ON i.order_items_sn = p.order_items_sn
                WHERE p.owner_id = @ownerId
                    AND p.valid_status = 'Active'
                    AND i.order_items_payment_state = 'Paid'
                ORDER BY p.pass_sn;
                """;

            var activeRows = (await _session.Connection.QueryAsync<ActiveLifecycleRow>(
                new CommandDefinition(
                    activeSql,
                    new { ownerId },
                    transaction: _session.Transaction,
                    cancellationToken: ct))).AsList();
            if (activeRows.Count > 1)
            {
                throw new InvalidOperationException($"學生 {ownerId} 同時存在多張 Active 票券");
            }

            int? justEndedPassSn = null;
            if (activeRows.Count == 1)
            {
                var active = activeRows[0];
                string? endReason = null;
                DateTime? endedAt = null;
                if (active.ticket_plan_kind_type.Equals("PACK", StringComparison.OrdinalIgnoreCase)
                    && active.credits_remaining <= 0)
                {
                    if (active.ended_at is null)
                    {
                        throw new InvalidOperationException("堂票已用完但缺少實際用完時間");
                    }

                    endReason = TicketEndReason.Depleted.ToString();
                    endedAt = active.ended_at;
                }
                else if (active.valid_edate is not null && active.valid_edate.Value.Date < todayDate)
                {
                    endReason = TicketEndReason.Expire.ToString();
                    endedAt = active.valid_edate.Value.Date;
                }

                if (endReason is not null)
                {
                    const string closeSql = """
                        UPDATE dbo.sdt_ticket_pass
                        SET valid_status = @endReason,
                            ended_at = @endedAt,
                            end_reason = @endReason,
                            update_dt = @updatedAt,
                            update_pn = @operatorId
                        WHERE pass_sn = @passSn
                          AND valid_status = 'Active';
                        """;
                    var affected = await _session.Connection.ExecuteAsync(
                        new CommandDefinition(
                            closeSql,
                            new
                            {
                                passSn = active.pass_sn,
                                endReason,
                                endedAt,
                                updatedAt,
                                operatorId
                            },
                            transaction: _session.Transaction,
                            cancellationToken: ct));
                    if (affected != 1)
                    {
                        throw new InvalidOperationException("票券結束狀態更新失敗");
                    }

                    justEndedPassSn = active.pass_sn;
                    activeRows.Clear();
                }
            }

            while (activeRows.Count == 0)
            {
                const string nextSql = """
                    SELECT TOP (1)
                        p.pass_sn,
                        p.ticket_plan_kind_code,
                        p.renewed_from_pass_sn,
                        k.ticket_plan_kind_default_expire_days,
                        i.order_items_paid_at,
                        source.valid_status AS source_valid_status,
                        source.valid_edate AS source_valid_edate,
                        source.ended_at AS source_ended_at,
                        source.end_reason AS source_end_reason
                    FROM dbo.sdt_ticket_pass AS p WITH (UPDLOCK, HOLDLOCK)
                    INNER JOIN dbo.order_items AS i
                        ON i.order_items_sn = p.order_items_sn
                    INNER JOIN dbo.ticket_plan_kind AS k
                        ON k.ticket_plan_kind_code = p.ticket_plan_kind_code
                    LEFT JOIN dbo.sdt_ticket_pass AS source
                        ON source.pass_sn = p.renewed_from_pass_sn
                    WHERE p.owner_id = @ownerId
                      AND p.valid_status = 'UnActive'
                      AND i.order_items_payment_state = 'Paid'
                      AND i.order_items_paid_at IS NOT NULL
                      AND
                      (
                          p.renewed_from_pass_sn IS NULL
                          OR source.valid_status IN ('Expire', 'Depleted')
                      )
                    ORDER BY
                        CASE
                            WHEN @justEndedPassSn IS NOT NULL
                                 AND p.renewed_from_pass_sn = @justEndedPassSn THEN 0
                            WHEN p.renewed_from_pass_sn IS NOT NULL THEN 1
                            ELSE 2
                        END,
                        i.order_items_paid_at ASC,
                        p.pass_sn ASC;
                    """;

                var next = await _session.Connection.QueryFirstOrDefaultAsync<QueuedPassRow>(
                    new CommandDefinition(
                        nextSql,
                        new { ownerId, justEndedPassSn },
                        transaction: _session.Transaction,
                        cancellationToken: ct));

                if (next is not null)
                {
                    var isSingle = next.ticket_plan_kind_code.Equals("SINGLE", StringComparison.OrdinalIgnoreCase);
                    var activationDate = today;
                    if (next.renewed_from_pass_sn is not null)
                    {
                        DateTime? sourceEndAt = next.source_end_reason?.Equals(
                            TicketEndReason.Depleted.ToString(),
                            StringComparison.OrdinalIgnoreCase) == true
                                ? next.source_ended_at
                                : next.source_valid_edate;
                        if (sourceEndAt is null || next.order_items_paid_at is null)
                        {
                            throw new InvalidOperationException("續約票缺少來源結束日或付款時間");
                        }

                        var sourceEndDate = DateOnly.FromDateTime(sourceEndAt.Value);
                        var paidDate = DateOnly.FromDateTime(next.order_items_paid_at.Value);
                        activationDate = TicketActivationSchedule.GetRenewalStartDate(
                            sourceEndDate,
                            paidDate);
                        if (activationDate > today)
                        {
                            break;
                        }
                    }

                    DateTime? validStartDate = isSingle
                        ? null
                        : activationDate.ToDateTime(TimeOnly.MinValue);
                    DateTime? validEndDate = isSingle
                        ? null
                        : next.ticket_plan_kind_default_expire_days is int expireDays && expireDays > 0
                            ? TicketActivationSchedule.GetEndDate(activationDate, expireDays)
                                .ToDateTime(TimeOnly.MinValue)
                            : throw new InvalidOperationException("非單次票方案缺少有效天數");

                    if (validEndDate is not null && validEndDate.Value.Date < todayDate)
                    {
                        const string expireQueuedSql = """
                            UPDATE dbo.sdt_ticket_pass
                            SET valid_status = 'Expire',
                                valid_sdate = @validStartDate,
                                valid_edate = @validEndDate,
                                ended_at = @validEndDate,
                                end_reason = 'Expire',
                                update_dt = @updatedAt,
                                update_pn = @operatorId
                            WHERE pass_sn = @passSn
                              AND valid_status = 'UnActive';
                            """;
                        var expired = await _session.Connection.ExecuteAsync(
                            new CommandDefinition(
                                expireQueuedSql,
                                new
                                {
                                    passSn = next.pass_sn,
                                    validStartDate,
                                    validEndDate,
                                    updatedAt,
                                    operatorId
                                },
                                transaction: _session.Transaction,
                                cancellationToken: ct));
                        if (expired != 1)
                        {
                            throw new InvalidOperationException("過期排隊票券狀態更新失敗");
                        }

                        justEndedPassSn = next.pass_sn;
                        continue;
                    }

                    const string activateSql = """
                        UPDATE dbo.sdt_ticket_pass
                        SET valid_status = 'Active',
                            valid_sdate = @validStartDate,
                            valid_edate = @validEndDate,
                            update_dt = @updatedAt,
                            update_pn = @operatorId
                        WHERE pass_sn = @passSn
                            AND valid_status = 'UnActive';
                        """;

                    var affected = await _session.Connection.ExecuteAsync(
                        new CommandDefinition(
                            activateSql,
                            new
                            {
                                passSn = next.pass_sn,
                                validStartDate,
                                validEndDate,
                                updatedAt,
                                operatorId
                            },
                            transaction: _session.Transaction,
                            cancellationToken: ct));
                    if (affected != 1)
                    {
                        throw new InvalidOperationException("FIFO 票券啟用失敗");
                    }

                    break;
                }

                break;
            }

            const string currentSql = """
                SELECT TOP (1)
                    p.pass_id,
                    p.ticket_plan_kind_type,
                    p.valid_status,
                    p.valid_edate,
                    p.credits_remaining
                FROM dbo.sdt_ticket_pass AS p
                INNER JOIN dbo.order_items AS i
                    ON i.order_items_sn = p.order_items_sn
                WHERE p.owner_id = @ownerId
                    AND p.valid_status = 'Active'
                    AND i.order_items_payment_state = 'Paid'
                ORDER BY i.order_items_paid_at ASC, p.pass_sn ASC;
                """;

            var current = await _session.Connection.QueryFirstOrDefaultAsync<ActivePassRow>(
                new CommandDefinition(
                    currentSql,
                    new { ownerId },
                    transaction: _session.Transaction,
                    cancellationToken: ct));

            return current is null
                ? null
                : new CurrentTicketSnapshot
                {
                    TicketId = current.pass_id,
                    TicketType = current.ticket_plan_kind_type,
                    TicketValidState = current.valid_status,
                    TicketPaymentState = "Paid",
                    TicketRemainCount = current.ticket_plan_kind_type.Equals("PACK", StringComparison.OrdinalIgnoreCase)
                        ? current.credits_remaining
                        : null,
                    TicketExpireDate = current.valid_edate is null
                        ? null
                        : DateOnly.FromDateTime(current.valid_edate.Value),
                    UpdatedAt = updatedAt
                };
        }

        private async Task<TicketPassInsertRow> InsertAsync(
            TicketPass pass,
            int orderSn,
            int orderItemSn,
            CancellationToken ct)
        {
            const string sql = """
                INSERT INTO dbo.sdt_ticket_pass
                (
                    create_dt,
                    order_items_sn,
                    orders_sn,
                    owner_id,
                    ticket_plan_kind_code,
                    ticket_plan_kind_type,
                    renewed_from_pass_sn,
                    valid_status,
                    valid_sdate,
                    valid_edate,
                    ended_at,
                    end_reason,
                    credits_total,
                    credits_remaining,
                    create_pn
                )
                OUTPUT INSERTED.pass_sn, INSERTED.pass_id
                VALUES
                (
                    @CreateAt,
                    @OrderItemSn,
                    @OrderSn,
                    @OwnerId,
                    @TicketPlanKindCode,
                    @TicketPlanKindType,
                    @RenewedFromPassSn,
                    @ValidStatus,
                    @ValidStartDate,
                    @ValidEndDate,
                    @EndedAt,
                    @EndReason,
                    @CreditsTotal,
                    @CreditsRemaining,
                    @CreatePn
                );
                """;

            var isPack = pass.Plan.Type == TicketPlanType.Pack;
            try
            {
                return await _session.Connection.QuerySingleAsync<TicketPassInsertRow>(
                    new CommandDefinition(
                        sql,
                        new
                        {
                            CreateAt = pass.PaidAt ?? DateTime.Now,
                            OrderItemSn = orderItemSn,
                            OrderSn = orderSn,
                            pass.OwnerId,
                            TicketPlanKindCode = pass.Plan.Id,
                            TicketPlanKindType = ToDatabaseType(pass.Plan.Type),
                            pass.RenewedFromPassSn,
                            ValidStatus = pass.ValidStatus.ToString(),
                            ValidStartDate = pass.ValidStartDate?.ToDateTime(TimeOnly.MinValue),
                            ValidEndDate = pass.ValidEndDate?.ToDateTime(TimeOnly.MinValue),
                            pass.EndedAt,
                            EndReason = pass.EndReason?.ToString(),
                            CreditsTotal = isPack ? pass.CreditsTotal : (int?)null,
                            CreditsRemaining = isPack ? pass.CreditsRemaining : (int?)null,
                            CreatePn = "ADMIN_PLACEHOLDER"
                        },
                        transaction: _session.Transaction,
                        cancellationToken: ct));
            }
            catch (SqlException ex) when (
                pass.RenewedFromPassSn is not null
                && ex.Number is 2601 or 2627)
            {
                throw new RenewalSourceAlreadyUsedException();
            }
        }

        private static string ToDatabaseType(TicketPlanType type) =>
            type == TicketPlanType.Pack ? "PACK" : "M_PASS";

        private sealed class TicketPassInsertRow
        {
            public int pass_sn { get; init; }
            public string pass_id { get; init; } = string.Empty;
        }

        private sealed class RenewalSourcePassRow
        {
            public int pass_sn { get; init; }
            public string? ticket_plan_family_code { get; init; }
            public string valid_status { get; init; } = string.Empty;
            public DateTime? valid_sdate { get; init; }
            public DateTime? valid_edate { get; init; }
            public DateTime? ended_at { get; init; }
            public string? end_reason { get; init; }
            public bool has_non_cancelled_renewal { get; init; }
            public bool has_cancelled_renewal { get; init; }
            public DateTime? last_cancelled_renewal_at { get; init; }
        }

        private sealed class CancellablePassRow
        {
            public int pass_sn { get; init; }
            public string pass_id { get; init; } = string.Empty;
            public string owner_id { get; init; } = string.Empty;
            public int? renewed_from_pass_sn { get; init; }
            public string ticket_plan_kind_type { get; init; } = string.Empty;
            public string valid_status { get; init; } = string.Empty;
            public int? credits_total { get; init; }
            public int? credits_remaining { get; init; }
        }

        private sealed class QueuedPassRow
        {
            public int pass_sn { get; init; }
            public string ticket_plan_kind_code { get; init; } = string.Empty;
            public int? renewed_from_pass_sn { get; init; }
            public int? ticket_plan_kind_default_expire_days { get; init; }
            public DateTime? order_items_paid_at { get; init; }
            public string? source_valid_status { get; init; }
            public DateTime? source_valid_edate { get; init; }
            public DateTime? source_ended_at { get; init; }
            public string? source_end_reason { get; init; }
        }

        private sealed class ActiveLifecycleRow
        {
            public int pass_sn { get; init; }
            public string pass_id { get; init; } = string.Empty;
            public string ticket_plan_kind_type { get; init; } = string.Empty;
            public string valid_status { get; init; } = string.Empty;
            public DateTime? valid_edate { get; init; }
            public int? credits_remaining { get; init; }
            public DateTime? ended_at { get; init; }
            public string? end_reason { get; init; }
        }

        private sealed class ActivePassRow
        {
            public string pass_id { get; init; } = string.Empty;
            public string ticket_plan_kind_type { get; init; } = string.Empty;
            public string valid_status { get; init; } = string.Empty;
            public DateTime? valid_edate { get; init; }
            public int? credits_remaining { get; init; }
        }
    }
}
