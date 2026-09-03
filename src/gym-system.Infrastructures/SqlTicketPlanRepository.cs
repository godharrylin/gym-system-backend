using Dapper;
using gym_system.Domain.Entities.Tickets;
using gym_system.Domain.Repositories;
using gym_system.Infrastructures.Connections;

namespace gym_system.Infrastructures
{
    internal sealed class SqlTicketPlanRepository : ITicketPlanRepository
    {
        private readonly ISqlSession _session;

        public SqlTicketPlanRepository(ISqlSession session)
        {
            _session = session;
        }

        public async Task<TicketPlanKind?> GetActiveByIdAsync(string ticketPlanKindId, CancellationToken ct)
        {
            const string sql = """
                SELECT
                    ticket_plan_kind_code,
                    ticket_plan_kind_type,
                    ticket_plan_family_code,
                    ticket_plan_kind_cname,
                    ticket_plan_kind_price,
                    ticket_plan_kind_default_credit,
                    ticket_plan_kind_default_expire_days,
                    ticket_plan_kind_default_is_active
                FROM dbo.ticket_plan_kind
                WHERE ticket_plan_kind_code = @ticketPlanKindId
                    AND ticket_plan_kind_default_is_active = 'Y'
                """;

            var row = await _session.Connection.QueryFirstOrDefaultAsync<TicketPlanKindRow>(
                new CommandDefinition(
                    sql,
                    new { ticketPlanKindId },
                    transaction: _session.Transaction,
                    cancellationToken: ct));

            return row is null ? null : Map(row);
        }

        internal static TicketPlanKind Map(TicketPlanKindRow row)
        {
            return new TicketPlanKind
            {
                Id = row.ticket_plan_kind_code,
                Name = row.ticket_plan_kind_cname,
                FamilyCode = row.ticket_plan_family_code,
                Type = MapType(row.ticket_plan_kind_type),
                Price = row.ticket_plan_kind_price,
                DefaultCredit = row.ticket_plan_kind_default_credit ?? 0,
                DefaultExpireDays = row.ticket_plan_kind_default_expire_days,
                IsActive = row.ticket_plan_kind_default_is_active.Equals("Y", StringComparison.OrdinalIgnoreCase)
            };
        }

        private static TicketPlanType MapType(string type)
        {
            return type.Equals("PACK", StringComparison.OrdinalIgnoreCase)
                ? TicketPlanType.Pack
                : TicketPlanType.MPass;
        }

        internal sealed class TicketPlanKindRow
        {
            public string ticket_plan_kind_code { get; init; } = string.Empty;
            public string ticket_plan_kind_type { get; init; } = string.Empty;
            public string? ticket_plan_family_code { get; init; }
            public string ticket_plan_kind_cname { get; init; } = string.Empty;
            public decimal ticket_plan_kind_price { get; init; }
            public int? ticket_plan_kind_default_credit { get; init; }
            public int? ticket_plan_kind_default_expire_days { get; init; }
            public string ticket_plan_kind_default_is_active { get; init; } = string.Empty;
        }
    }
}

