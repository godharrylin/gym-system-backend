using Dapper;
using gym_system.Application.MembersUseCase.Commands.RegisterMember;
using gym_system.Application.OrdersUseCase.Commands.PayOrder;
using gym_system.Application.OrdersUseCase.Services;
using gym_system.Application.TicketPlansUseCase.Queries;
using gym_system.Application.TicketsUseCase.Commands.CancelQueuedRenewal;
using gym_system.Application.TicketsUseCase.Queries;
using gym_system.Domain.Repositories;
using gym_system.Infrastructures;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace gym_system.Api.Tests;

[CollectionDefinition("Ticket SQL", DisableParallelization = true)]
public sealed class TicketSqlCollection;

// Each test owns explicit user/plan IDs. No fixed business IDs, phone-based
// cleanup, schema changes or database-wide resets are permitted here.
internal sealed class TicketSqlFixture : IAsyncDisposable
{
    public string Marker { get; } = "TPTEST_" + Guid.NewGuid().ToString("N");
    public FixedTaipeiClock Clock { get; } = new();
    public ServiceProvider Provider { get; }
    private readonly string _connectionString;
    private readonly HashSet<string> _users = [];
    private readonly List<int> _plans = [];
    private readonly List<string> _rules = [];

    public TicketSqlFixture()
    {
        _connectionString = Environment.GetEnvironmentVariable("TEST_DB_CONNECTION")
            ?? throw new InvalidOperationException("TEST_DB_CONNECTION must explicitly select the development database.");
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = _connectionString }).Build());
        services.AddInfrastructureSql();
        services.AddSingleton<IClock>(Clock);
        services.AddScoped<ITicketPlanEligibilityRule, NewOnlyTicketPlanEligibilityRule>();
        services.AddScoped<ITicketPlanEligibilityRule, RenewalTicketPlanEligibilityRule>();
        services.AddScoped<ITicketPlanEligibilityService, TicketPlanEligibilityService>();
        services.AddScoped<RenewalTicketPassEligibilityService>();
        services.AddScoped<TicketPurchaseService>();
        services.AddScoped<UnpaidTicketOrderPaymentService>();
        services.AddScoped<PayOrderHandler>();
        services.AddScoped<RegisterMemberHandler>();
        services.AddScoped<CancelQueuedRenewalHandler>();
        services.AddScoped<GetStudentTicketPassesHandler>();
        Provider = services.BuildServiceProvider();
    }

    public SqlConnection Open() { var c = new SqlConnection(_connectionString); c.Open(); return c; }
    public string NewPhone() => "IT" + Guid.NewGuid().ToString("N")[..18];
    public void TrackUser(string id) => _users.Add(id);

    public async Task<string> AddStudent(DateTime? assignedAt = null)
    {
        using var c = Open();
        var id = await c.QuerySingleAsync<string>("""
            INSERT dbo.users (usr_name,usr_phone,usr_pwd,usr_active)
            OUTPUT inserted.usr_id VALUES (@Marker,@phone,'test-only',1);
            """, new { Marker, phone = NewPhone() });
        _users.Add(id);
        await c.ExecuteAsync("""
            INSERT dbo.user_role (usr_id,bmc_role_id,user_role_is_active,user_role_cdt)
            SELECT @id,bmc_role_id,1,@assignedAt FROM dbo.bmc_role WHERE bmc_role_code='Student';
            INSERT dbo.sdt_profile (usr_id) VALUES (@id);
            """, new { id, assignedAt = assignedAt ?? Clock.Now().AddDays(-2) });
        return id;
    }

    public async Task<string> AddPlan(string family = "MONTHLY", string type = "M_PASS", int days = 30,
        decimal price = 100, string? rule = null)
    {
        using var c = Open();
        var code = Marker + "_" + _plans.Count;
        var sn = await c.QuerySingleAsync<int>("""
            INSERT dbo.ticket_plan_kind (ticket_plan_kind_code,ticket_plan_kind_type,ticket_plan_family_code,
                ticket_plan_kind_cname,ticket_plan_kind_price,ticket_plan_kind_default_credit,
                ticket_plan_kind_default_expire_days,ticket_plan_kind_default_is_active)
            OUTPUT inserted.ticket_plan_kind_sn VALUES (@code,@type,@family,@Marker,@price,@credits,@days,'Y');
            """, new { code, type, family, Marker, price, credits = type == "PACK" ? (int?)10 : null, days });
        _plans.Add(sn);
        if (rule is not null) await AddRule(code, rule);
        return code;
    }

    public async Task AddRule(string code, string rule, bool enabled = true)
    {
        using var c = Open();
        var sn = await c.QuerySingleAsync<int>("SELECT ticket_plan_kind_sn FROM dbo.ticket_plan_kind WHERE ticket_plan_kind_code=@code", new { code });
        if (!_plans.Contains(sn)) throw new InvalidOperationException("Only test-owned plans may be changed.");
        await c.ExecuteAsync("""
            INSERT dbo.ticket_plan_kind_rule (ticket_plan_kind_sn,plan_rule_sn,ticket_plan_kind_rule_is_enabled)
            SELECT @sn,plan_rule_sn,@enabled FROM dbo.plan_rule WHERE plan_rule_code=@rule;
            """, new { sn, rule, enabled = enabled ? "Y" : "N" });
    }

    public async Task<string> AddUnknownRule(bool active)
    {
        using var c = Open();
        var sn = Guid.NewGuid().ToString("N")[..20];
        var code = Marker + "_RULE" + _rules.Count;
        await c.ExecuteAsync("""
            INSERT dbo.plan_rule (plan_rule_sn,plan_rule_code,plan_rule_name,plan_rule_is_active)
            VALUES (@sn,@code,@Marker,@active);
            """, new { sn, code, Marker, active = active ? "Y" : "N" });
        _rules.Add(sn);
        return code;
    }

    public async Task<SqlPassState> SeedPass(string user, string code, string status = "UnActive",
        DateTime? start = null, DateTime? end = null, DateTime? paidAt = null,
        int? source = null, DateTime? endedAt = null, string? reason = null, int? remaining = null)
    {
        if (!_users.Contains(user)) throw new InvalidOperationException("Test does not own this user.");
        using var c = Open();
        using var tx = c.BeginTransaction();
        var order = await c.QuerySingleAsync<int>("""
            INSERT dbo.orders (order_buy_date,orders_buyer_id,orders_buyer_name,orders_overall_payment_state,
                orders_total_amount,orders_actual_amount,orders_create_pn)
            OUTPUT inserted.orders_sn VALUES (@paidAt,@user,@Marker,'Paid',100,100,@Marker);
            """, new { paidAt = paidAt ?? Clock.Now(), user, Marker }, tx);
        var item = await c.QuerySingleAsync<int>("""
            INSERT dbo.order_items (orders_sn,order_items_buy_date,order_items_type,order_items_ref_id,
                order_items_name,order_items_payment_state,order_items_paid_at,order_items_payment_method,
                order_items_unit_price,order_items_total_amount,order_items_actual_amount,order_items_quantity)
            OUTPUT inserted.order_items_sn VALUES (@order,@paidAt,'Ticket',@code,@Marker,'Paid',@paidAt,'Cash',100,100,100,1);
            """, new { order, paidAt = paidAt ?? Clock.Now(), code, Marker }, tx);
        var sn = await c.QuerySingleAsync<int>("""
            INSERT dbo.sdt_ticket_pass (create_dt,orders_sn,order_items_sn,owner_id,ticket_plan_kind_code,
                ticket_plan_kind_type,renewed_from_pass_sn,valid_status,valid_sdate,valid_edate,
                ended_at,end_reason,credits_total,credits_remaining,create_pn)
            OUTPUT inserted.pass_sn
            SELECT @paidAt,@order,@item,@user,@code,k.ticket_plan_kind_type,@source,@status,@start,@end,
                @endedAt,@reason,k.ticket_plan_kind_default_credit,
                COALESCE(@remaining,k.ticket_plan_kind_default_credit),@Marker
            FROM dbo.ticket_plan_kind k WHERE k.ticket_plan_kind_code=@code;
            """, new { order, item, user, code, source, status, start, end, endedAt, reason, remaining,
                paidAt = paidAt ?? Clock.Now(), Marker }, tx);
        tx.Commit();
        return await Pass(sn);
    }

    public async Task<SqlPassState> Pass(int sn)
    {
        using var c = Open();
        return await c.QuerySingleAsync<SqlPassState>("""
            SELECT p.pass_sn AS Sn,p.pass_id AS Id,p.order_items_sn AS ItemSn,p.orders_sn AS OrderSn,
                p.valid_status AS Status,p.valid_sdate AS Start,p.valid_edate AS [End],
                p.credits_remaining AS Remaining,p.ended_at AS EndedAt,p.end_reason AS Reason,
                p.renewed_from_pass_sn AS Source,i.order_items_paid_at AS PaidAt
            FROM dbo.sdt_ticket_pass p JOIN dbo.order_items i ON i.order_items_sn=p.order_items_sn WHERE p.pass_sn=@sn
            """, new { sn });
    }

    public async Task<T> Transaction<T>(Func<IServiceProvider, Task<T>> action)
    {
        using var scope = Provider.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await uow.BeginAsync(default);
        try { var result = await action(scope.ServiceProvider); await uow.CommitAsync(default); return result; }
        catch { await uow.RollbackAsync(default); throw; }
    }

    public async ValueTask DisposeAsync()
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        // A marker mismatch aborts all cleanup instead of deleting another user's data.
        foreach (var id in _users)
        {
            var name = await c.QuerySingleOrDefaultAsync<string>("SELECT usr_name FROM dbo.users WHERE usr_id=@id", new { id }, tx);
            if (name is not null && name != Marker) throw new InvalidOperationException("Cleanup ownership mismatch: " + Marker);
            await c.ExecuteAsync("""
                DELETE dbo.sdt_ticket_pass WHERE owner_id=@id;
                DELETE i FROM dbo.order_items i JOIN dbo.orders o ON o.orders_sn=i.orders_sn WHERE o.orders_buyer_id=@id;
                DELETE dbo.orders WHERE orders_buyer_id=@id;
                DELETE dbo.sdt_profile WHERE usr_id=@id;
                DELETE dbo.user_role WHERE usr_id=@id;
                DELETE dbo.users WHERE usr_id=@id AND usr_name=@Marker;
                """, new { id, Marker }, tx);
        }
        foreach (var sn in _plans)
        {
            await c.ExecuteAsync("""
                DELETE kr FROM dbo.ticket_plan_kind_rule kr JOIN dbo.ticket_plan_kind k ON k.ticket_plan_kind_sn=kr.ticket_plan_kind_sn
                WHERE k.ticket_plan_kind_sn=@sn AND k.ticket_plan_kind_cname=@Marker;
                DELETE dbo.ticket_plan_kind WHERE ticket_plan_kind_sn=@sn AND ticket_plan_kind_cname=@Marker;
                """, new { sn, Marker }, tx);
        }
        foreach (var sn in _rules)
            await c.ExecuteAsync("DELETE dbo.plan_rule WHERE plan_rule_sn=@sn AND plan_rule_name=@Marker", new { sn, Marker }, tx);
        tx.Commit();
        var remaining = await c.ExecuteScalarAsync<int>("""
            SELECT (SELECT COUNT(*) FROM dbo.users WHERE usr_name=@Marker)
                 + (SELECT COUNT(*) FROM dbo.ticket_plan_kind WHERE ticket_plan_kind_cname=@Marker)
                 + (SELECT COUNT(*) FROM dbo.plan_rule WHERE plan_rule_name=@Marker)
            """, new { Marker });
        await Provider.DisposeAsync();
        if (remaining != 0) throw new InvalidOperationException("Test data remains for " + Marker);
    }
}

internal sealed class FixedTaipeiClock : IClock
{
    public DateTime Value { get; set; } = new(2026, 9, 6, 10, 0, 0, DateTimeKind.Unspecified);
    public DateTime Now() => Value;
    public DateOnly Today() => DateOnly.FromDateTime(Value);
}

internal sealed class SqlPassState
{
    public int Sn { get; set; }
    public string Id { get; set; } = "";
    public int ItemSn { get; set; }
    public int OrderSn { get; set; }
    public string Status { get; set; } = "";
    public DateTime? Start { get; set; }
    public DateTime? End { get; set; }
    public int? Remaining { get; set; }
    public DateTime? EndedAt { get; set; }
    public string? Reason { get; set; }
    public int? Source { get; set; }
    public DateTime PaidAt { get; set; }
}
