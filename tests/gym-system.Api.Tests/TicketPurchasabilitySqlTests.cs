using Dapper;
using gym_system.Application.OrdersUseCase.Services;
using gym_system.Application.TicketPlansUseCase.Queries;
using gym_system.Application.TicketsUseCase.Commands.CancelQueuedRenewal;
using gym_system.Domain.Entities.Members;
using gym_system.Domain.Enums;
using gym_system.Domain.Exceptions;
using gym_system.Domain.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace gym_system.Api.Tests;

[Collection("Ticket SQL")]
public sealed class TicketPurchasabilitySqlTests
{
    [SqlServerFact] // N03/N05/N06: known UTC is converted at the input boundary only.
    public async Task TaiwanRoleTime_RoundTrip_ShouldRespectThirtiethCalendarDay()
    {
        await using var f = new TicketSqlFixture();
        var utc = new DateTime(2026, 8, 7, 16, 10, 0, DateTimeKind.Utc);
        var taiwan = TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneInfo.FindSystemTimeZoneById("Asia/Taipei"));
        Assert.Equal(new DateTime(2026, 8, 8, 0, 10, 0), taiwan);
        var imported = await f.AddStudent(taiwan);
        var local = await f.AddStudent(new DateTime(2026, 8, 8, 0, 10, 0));
        var code = await f.AddPlan(rule: "NEW_ONLY");
        using var scope = f.Provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ITicketPlanEligibilityService>();
        var plans = await scope.ServiceProvider.GetRequiredService<ITicketPlanCatalogQueryService>().GetActiveTicketPlansAsync(default);
        var plan = Assert.Single(plans, p => p.Id == code);
        foreach (var user in new[] { imported, local })
        {
            var role = await scope.ServiceProvider.GetRequiredService<IUserRoleRepository>().GetUserRoleAsync(user, UserRoleCode.Student, default);
            Assert.Equal(taiwan, role!.AssignedAt); // SQL has no timezone: do not add eight hours on read.
            f.Clock.Value = new DateTime(2026, 9, 6, 23, 59, 59);
            Assert.True(await service.CanPurchaseAsync((await service.GetEligibilityContextAsync(user, default))!, plan, default));
            f.Clock.Value = new DateTime(2026, 9, 7, 0, 0, 0);
            Assert.False(await service.CanPurchaseAsync((await service.GetEligibilityContextAsync(user, default))!, plan, default));
        }
    }

    [SqlServerFact] // E05/E06: real Dapper list parameters, Tags and fail-closed SQL.
    public async Task Catalog_ShouldUseSharedClassificationAndDisabledRuleGuards()
    {
        await using var f = new TicketSqlFixture();
        var standard = await f.AddPlan();
        var hidden = await f.AddPlan(rule: "HIDDEN");
        var family = await f.AddPlan(rule: "FAMILY_ELIGIBLE");
        var fresh = await f.AddPlan(rule: "NEW_ONLY");
        var disabledRelation = await f.AddPlan();
        await f.AddRule(disabledRelation, "RENEWAL", enabled: false);
        var inactiveRule = await f.AddPlan(rule: await f.AddUnknownRule(active: false));
        var unknown = await f.AddPlan(rule: await f.AddUnknownRule(active: true));
        using var scope = f.Provider.CreateScope();
        var plans = await scope.ServiceProvider.GetRequiredService<ITicketPlanCatalogQueryService>().GetActiveTicketPlansAsync(default);
        Assert.Contains(plans, p => p.Id == standard);
        Assert.DoesNotContain(plans, p => p.Id == hidden || p.Id == disabledRelation || p.Id == inactiveRule);
        var familyPlan = Assert.Single(plans, p => p.Id == family);
        Assert.Contains("FAMILY_ELIGIBLE", familyPlan.Tags);
        Assert.Empty(familyPlan.EligibilityRuleCodes);
        Assert.Equal(new[] { "NEW_ONLY" }, Assert.Single(plans, p => p.Id == fresh).EligibilityRuleCodes);
        var user = await f.AddStudent();
        var eligibility = scope.ServiceProvider.GetRequiredService<ITicketPlanEligibilityService>();
        Assert.False(await eligibility.CanPurchaseAsync((await eligibility.GetEligibilityContextAsync(user, default))!,
            Assert.Single(plans, p => p.Id == unknown), default));
    }

    [SqlServerFact] // N07-N12: pass history, independent of current order-item state.
    public async Task History_ShouldKeepEligibilityConsumedForEveryPassStatus()
    {
        await using var f = new TicketSqlFixture();
        var user = await f.AddStudent();
        var other = await f.AddStudent();
        using var scope = f.Provider.CreateScope();
        var history = scope.ServiceProvider.GetRequiredService<IStudentTicketPurchaseHistoryQueryService>();
        foreach (var status in new[] { "UnActive", "Active", "Expire", "Depleted", "Cancelled" })
        {
            var code = await f.AddPlan();
            var pass = await f.SeedPass(user, code, status);
            using var c = f.Open();
            await c.ExecuteAsync("UPDATE dbo.order_items SET order_items_payment_state='Cancel' WHERE order_items_sn=@ItemSn", pass);
            Assert.True(await history.HasPurchasedTicketPlanAsync(user, code, default));
            Assert.False(await history.HasPurchasedTicketPlanAsync(other, code, default));
        }
        Assert.False(await history.HasPurchasedTicketPlanAsync(user, await f.AddPlan(), default));
    }

    [SqlServerFact] // R01/R02/R04: latest same-family source; no fallback; depleted uses ended_at.
    public async Task RenewalSource_ShouldSelectLatestAndUseDepletedEnd()
    {
        await using var f = new TicketSqlFixture();
        var user = await f.AddStudent();
        var code = await f.AddPlan(type: "PACK");
        await f.SeedPass(user, code, "Expire", new DateTime(2026, 7, 1), new DateTime(2026, 7, 31));
        var latest = await f.SeedPass(user, code, "Depleted", new DateTime(2026, 8, 1), new DateTime(2026, 9, 30),
            endedAt: new DateTime(2026, 9, 1, 20, 0, 0), reason: "Depleted", remaining: 0);
        using var scope = f.Provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<RenewalTicketPassEligibilityService>();
        var result = await service.EvaluateAsync(user, "MONTHLY", f.Clock.Now(), false, default);
        Assert.Equal(latest.Sn, result.Eligibility!.SourcePassSn);
        Assert.Equal(new DateOnly(2026, 9, 1), result.Eligibility.SourceEffectiveEndDate);
        var newer = await f.SeedPass(user, code, "Expire", new DateTime(2026, 8, 2), new DateTime(2026, 8, 20));
        Assert.Equal(newer.Sn, (await scope.ServiceProvider.GetRequiredService<ITicketPassRepository>()
            .FindLatestRenewalSourceAsync(user, "MONTHLY", false, default))!.PassSn);
        Assert.Equal("RENEWAL_WINDOW_EXPIRED", (await service.EvaluateAsync(user, "MONTHLY", f.Clock.Now(), false, default)).FailureCode);
    }

    [SqlServerFact] // S01/S02/S06/S07/S11: SINGLE yields, FIFO resumes, global slot across families.
    public async Task Single_ShouldYieldAndResumeWithoutLosingCreditsOrPaymentTime()
    {
        await using var f = new TicketSqlFixture();
        var user = await f.AddStudent();
        var single = await f.SeedPass(user, "SINGLE", "Active", paidAt: f.Clock.Now().AddDays(-1));
        var laterSingle = await f.SeedPass(user, "SINGLE");
        var monthly = await f.SeedPass(user, await f.AddPlan(days: 1));
        var crossFamily = await f.SeedPass(user, await f.AddPlan(family: "PACK_FAMILY", type: "PACK", days: 1));
        Assert.True(await f.Transaction(sp => sp.GetRequiredService<ITicketPassRepository>().HasQueuedPassAsync(user, default)));
        Assert.Equal(monthly.Id, (await Reconcile(f, user))!.TicketId);
        var yielded = await f.Pass(single.Sn);
        Assert.Equal("UnActive", yielded.Status);
        Assert.Equal(single.Remaining, yielded.Remaining);
        Assert.Equal(single.PaidAt, yielded.PaidAt);
        Assert.Null(yielded.Start);
        Assert.Null(yielded.End);
        f.Clock.Value = f.Clock.Value.AddDays(1);
        Assert.Equal(crossFamily.Id, (await Reconcile(f, user))!.TicketId);
        f.Clock.Value = f.Clock.Value.AddDays(1);
        Assert.Equal(single.Id, (await Reconcile(f, user))!.TicketId);
        Assert.Equal("UnActive", (await f.Pass(laterSingle.Sn)).Status);
        Assert.False(await f.Transaction(sp => sp.GetRequiredService<ITicketPassRepository>().HasQueuedPassAsync(user, default)));
    }

    [SqlServerFact] // S05b: first 100 future renewals must not hide a usable standard ticket.
    public async Task Reconcile_ShouldScanPastOneHundredFutureCandidates()
    {
        await using var f = new TicketSqlFixture();
        var user = await f.AddStudent();
        var code = await f.AddPlan(type: "PACK");
        for (var i = 0; i < 100; i++)
        {
            var source = await f.SeedPass(user, code, "Depleted", f.Clock.Now().Date.AddDays(-10), f.Clock.Now().Date.AddDays(20),
                endedAt: f.Clock.Now(), reason: "Depleted", remaining: 0);
            await f.SeedPass(user, code, source: source.Sn);
        }
        var usable = await f.SeedPass(user, await f.AddPlan());
        Assert.Equal(usable.Id, (await Reconcile(f, user))!.TicketId);
        using var c = f.Open();
        Assert.Equal(100, await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.sdt_ticket_pass WHERE owner_id=@user AND valid_status='UnActive'", new { user }));
    }

    [SqlServerFact] // R06/R10/C01/C03: capped retry window, cancellation updates pass only.
    public async Task Cancellation_ShouldPreserveOriginalDeadlineAndPermitReplacement()
    {
        await using var f = new TicketSqlFixture();
        f.Clock.Value = new DateTime(2026, 9, 5, 23, 50, 0);
        var user = await f.AddStudent();
        var code = await f.AddPlan();
        var source = await f.SeedPass(user, code, "Expire", new DateTime(2026, 7, 29), new DateTime(2026, 8, 27));
        var child = await f.SeedPass(user, code, source: source.Sn);
        using var scope = f.Provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<CancelQueuedRenewalHandler>().Handle(child.Id, f.Marker);
        Assert.Equal("Cancelled", (await f.Pass(child.Sn)).Status);
        using var c = f.Open();
        Assert.Equal("Paid", await c.QuerySingleAsync<string>("SELECT order_items_payment_state FROM dbo.order_items WHERE order_items_sn=@ItemSn", child));
        var service = scope.ServiceProvider.GetRequiredService<RenewalTicketPassEligibilityService>();
        Assert.NotNull((await service.EvaluateAsync(user, "MONTHLY", f.Clock.Now(), false, default)).Eligibility);
        var replacement = await f.SeedPass(user, code, source: source.Sn); // filtered unique index frees cancelled link.
        await scope.ServiceProvider.GetRequiredService<CancelQueuedRenewalHandler>().Handle(replacement.Id, f.Marker);
        Assert.NotNull((await service.EvaluateAsync(user, "MONTHLY", f.Clock.Now(), false, default)).Eligibility);
        f.Clock.Value = new DateTime(2026, 9, 6);
        Assert.Equal("RENEWAL_WINDOW_EXPIRED", (await service.EvaluateAsync(user, "MONTHLY", f.Clock.Now(), false, default)).FailureCode);
    }

    [SqlServerFact] // SQL transaction/owner lock: same source concurrent purchase, no orphan order.
    public async Task ConcurrentRenewalPurchases_ShouldCommitOnlyOneSuccessor()
    {
        await using var f = new TicketSqlFixture();
        var user = await f.AddStudent();
        var code = await f.AddPlan(rule: "RENEWAL");
        var source = await f.SeedPass(user, code, "Active", f.Clock.Now().Date.AddDays(-20), f.Clock.Now().Date.AddDays(9));
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<Exception?> Purchase()
        {
            await gate.Task;
            try
            {
                await f.Transaction(sp => sp.GetRequiredService<TicketPurchaseService>().PurchaseAsync(new()
                {
                    BuyerId = user, BeneficiaryStudentIds = [user], TicketPlanKindCode = code,
                    PaymentStatus = PaymentState.Paid, OperatorId = f.Marker
                }));
                return null;
            }
            catch (Exception e) { return e; }
        }
        var first = Purchase();
        var second = Purchase();
        gate.SetResult();
        var outcomes = await Task.WhenAll(first, second);
        Assert.Single(outcomes, x => x is null);
        Assert.Equal("RENEWAL_SOURCE_ALREADY_USED", Assert.IsType<TicketPurchaseRejectedException>(Assert.Single(outcomes, x => x is not null)).Code);
        using var c = f.Open();
        Assert.Equal(1, await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.sdt_ticket_pass WHERE renewed_from_pass_sn=@Sn AND valid_status<>'Cancelled'", source));
        Assert.Equal(2, await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.orders WHERE orders_buyer_id=@user", new { user }));
    }

    [SqlServerFact] // R11: exercise the real filtered index and repository exception translation.
    public async Task DuplicateSuccessor_ShouldMapUniqueIndexViolationAndRollback()
    {
        await using var f = new TicketSqlFixture();
        var user = await f.AddStudent();
        var code = await f.AddPlan();
        var source = await f.SeedPass(user, code, "Active", f.Clock.Now().Date.AddDays(-10), f.Clock.Now().Date.AddDays(19));
        var child = await f.SeedPass(user, code, source: source.Sn);
        await Assert.ThrowsAsync<RenewalSourceAlreadyUsedException>(() => f.Transaction(async sp =>
        {
            var plan = (await sp.GetRequiredService<ITicketPlanRepository>().GetActiveByIdAsync(code, default))!;
            var pass = gym_system.Domain.Entities.Tickets.TicketPass.IssueQueued(
                "duplicate", user, "order", "item", plan, source.Sn, f.Clock.Now());
            return await sp.GetRequiredService<ITicketPassRepository>().AddRangeAsync([pass], new()
            {
                OrderSn = child.OrderSn, OrderId = "order",
                Items = [new() { ClientItemId = "item", OrderItemSn = child.ItemSn, OrderItemId = "item" }]
            }, default);
        }));
        using var c = f.Open();
        Assert.Equal(1, await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.sdt_ticket_pass WHERE renewed_from_pass_sn=@Sn", source));
    }

    [SqlServerFact] // C02/C06/C08 / paidAtTimestamp: real SQL + handler + controller mapping.
    public async Task TicketPassContract_ShouldPreserveSecondsAndMatchCancellationRules()
    {
        await using var f = new TicketSqlFixture();
        f.Clock.Value = new DateTime(2026, 9, 6, 0, 0, 7);
        var user = await f.AddStudent();
        var code = await f.AddPlan();
        var source = await f.SeedPass(user, code, "Active", f.Clock.Now().Date.AddDays(-10), f.Clock.Now().Date.AddDays(19));
        var child = await f.SeedPass(user, code, source: source.Sn);
        using var scope = f.Provider.CreateScope();
        var controller = new gym_system.Api.Controllers.StudentTicketPassesController(
            scope.ServiceProvider.GetRequiredService<gym_system.Application.TicketsUseCase.Queries.GetStudentTicketPassesHandler>());
        async Task<IReadOnlyList<gym_system.Api.Contracts.TicketPasses.StudentTicketPassDto>> Read()
        {
            var response = await controller.GetAsync(user, default);
            return Assert.IsType<gym_system.Api.Contracts.TicketPasses.GetStudentTicketPassesResponse>(
                Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(response.Result).Value).TicketPasses;
        }
        var rows = await Read();
        var queued = Assert.Single(rows, p => p.PassId == child.Id);
        Assert.True(queued.CanCancel);
        Assert.Equal("2026-09-06", queued.PaidAt);
        Assert.Equal("2026-09-06T00:00:07+08:00", queued.PaidAtTimestamp);
        Assert.False(Assert.Single(rows, p => p.PassId == source.Id).CanCancel);
        await scope.ServiceProvider.GetRequiredService<CancelQueuedRenewalHandler>().Handle(child.Id, f.Marker);
        Assert.False(Assert.Single(await Read(), p => p.PassId == child.Id).CanCancel);
        // Advance until another renewal is Active; being unused must not permit cancellation.
        var replacement = await f.SeedPass(user, code, source: source.Sn);
        f.Clock.Value = f.Clock.Value.AddDays(20);
        Assert.False(Assert.Single(await Read(), p => p.PassId == replacement.Id).CanCancel);
        Assert.Equal("Active", (await f.Pass(replacement.Sn)).Status);
        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<CancelQueuedRenewalHandler>().Handle(replacement.Id, f.Marker));
        Assert.Equal("Active", (await f.Pass(replacement.Sn)).Status);
    }

    [SqlServerFact] // R08: following day remains valid, next midnight rejects even inside original grace.
    public async Task CancellationRetry_ShouldEndAtNextTaiwanDayWithoutResettingGrace()
    {
        await using var f = new TicketSqlFixture();
        f.Clock.Value = new DateTime(2026, 9, 5, 23, 50, 0);
        var user = await f.AddStudent();
        var code = await f.AddPlan();
        var source = await f.SeedPass(user, code, "Expire", new DateTime(2026, 8, 1), new DateTime(2026, 9, 1));
        var child = await f.SeedPass(user, code, source: source.Sn);
        using var scope = f.Provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<CancelQueuedRenewalHandler>().Handle(child.Id, f.Marker);
        var service = scope.ServiceProvider.GetRequiredService<RenewalTicketPassEligibilityService>();
        Assert.NotNull((await service.EvaluateAsync(user, "MONTHLY", new DateTime(2026, 9, 6, 23, 59, 59), false, default)).Eligibility);
        Assert.Equal("RENEWAL_RETRY_EXPIRED", (await service.EvaluateAsync(user, "MONTHLY", new DateTime(2026, 9, 7), false, default)).FailureCode);
    }

    [SqlServerFact] // Hidden blocks purchase/payment; paused family adds neither eligibility nor discount.
    public async Task HiddenAndPausedFamily_ShouldRemainEnforcedAtPurchaseAndPayment()
    {
        await using var f = new TicketSqlFixture();
        var user = await f.AddStudent();
        var other = await f.AddStudent();
        var code = await f.AddPlan(rule: "FAMILY_ELIGIBLE");
        TicketPurchaseRequest Request(PaymentState payment, IReadOnlyList<string>? members = null) => new()
        {
            BuyerId = user, BeneficiaryStudentIds = members ?? [user], TicketPlanKindCode = code,
            PaymentStatus = payment, OperatorId = f.Marker
        };
        var unpaid = await f.Transaction(sp => sp.GetRequiredService<TicketPurchaseService>().PurchaseAsync(Request(PaymentState.UnPaid)));
        Assert.Equal(100m, unpaid.ActualAmount);
        Assert.Equal(unpaid.TotalAmount, unpaid.ActualAmount);
        var error = await Assert.ThrowsAsync<TicketPurchaseRejectedException>(() => f.Transaction(sp =>
            sp.GetRequiredService<TicketPurchaseService>().PurchaseAsync(Request(PaymentState.Paid, [user, other]))));
        Assert.Equal("FAMILY_PURCHASE_NOT_AVAILABLE", error.Code);
        await f.AddRule(code, "HIDDEN");
        await Assert.ThrowsAsync<KeyNotFoundException>(() => f.Transaction(sp =>
            sp.GetRequiredService<TicketPurchaseService>().PurchaseAsync(Request(PaymentState.Paid))));
        using var scope = f.Provider.CreateScope();
        await Assert.ThrowsAsync<KeyNotFoundException>(() => scope.ServiceProvider
            .GetRequiredService<gym_system.Application.OrdersUseCase.Commands.PayOrder.PayOrderHandler>().Handle(new()
            { OrderId = unpaid.OrderId, PaymentMethod = "Cash", OperatorId = f.Marker }));
        using var c = f.Open();
        Assert.Equal("UnPaid", await c.QuerySingleAsync<string>("SELECT orders_overall_payment_state FROM dbo.orders WHERE orders_id=@OrderId", unpaid));
        Assert.Equal(0, await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.sdt_ticket_pass WHERE owner_id=@user", new { user }));
    }

    [SqlServerFact] // S08: full cancellation -> SINGLE -> paid replacement workflow.
    public async Task CancellationReplacement_ShouldYieldSingleAndKeepOriginalSource()
    {
        await using var f = new TicketSqlFixture();
        var user = await f.AddStudent();
        var code = await f.AddPlan(rule: "RENEWAL");
        var source = await f.SeedPass(user, code, "Expire", f.Clock.Now().Date.AddDays(-30), f.Clock.Now().Date.AddDays(-1));
        var child = await f.SeedPass(user, code, source: source.Sn);
        var single = await f.SeedPass(user, "SINGLE");
        using var scope = f.Provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<CancelQueuedRenewalHandler>().Handle(child.Id, f.Marker);
        Assert.Equal("Active", (await f.Pass(single.Sn)).Status);
        await f.Transaction(sp => sp.GetRequiredService<TicketPurchaseService>().PurchaseAsync(new()
        {
            BuyerId = user, BeneficiaryStudentIds = [user], TicketPlanKindCode = code,
            PaymentStatus = PaymentState.Paid, OperatorId = f.Marker
        }));
        var yielded = await f.Pass(single.Sn);
        Assert.Equal("UnActive", yielded.Status);
        Assert.Equal(single.Remaining, yielded.Remaining);
        Assert.Equal(single.PaidAt, yielded.PaidAt);
        using var c = f.Open();
        var active = await c.QuerySingleAsync<string>("SELECT pass_id FROM dbo.sdt_ticket_pass WHERE owner_id=@user AND valid_status='Active' AND renewed_from_pass_sn=@Sn", new { user, source.Sn });
        Assert.Equal(active, await c.QuerySingleAsync<string>("SELECT sdt_cur_ticket_id FROM dbo.sdt_profile WHERE usr_id=@user", new { user }));
        Assert.Equal("Cancelled", (await f.Pass(child.Sn)).Status);
    }

    [SqlServerFact] // S09/S11: just-ended source priority matches the in-memory implementation.
    public async Task Reconcile_ShouldPrioritizeDirectSuccessorAndRemainStable()
    {
        await using var f = new TicketSqlFixture();
        var user = await f.AddStudent();
        var code = await f.AddPlan();
        var old = await f.SeedPass(user, code, "Expire", f.Clock.Now().Date.AddDays(-31), f.Clock.Now().Date.AddDays(-2));
        var current = await f.SeedPass(user, code, "Active", f.Clock.Now().Date.AddDays(-30), f.Clock.Now().Date.AddDays(-1));
        var unrelated = await f.SeedPass(user, code, paidAt: f.Clock.Now().AddDays(-10), source: old.Sn);
        var direct = await f.SeedPass(user, code, paidAt: f.Clock.Now().AddDays(-2), source: current.Sn);
        Assert.Equal(direct.Id, (await Reconcile(f, user))!.TicketId);
        Assert.Equal(direct.Id, (await Reconcile(f, user))!.TicketId);
        Assert.Equal("Expire", (await f.Pass(current.Sn)).Status);
        Assert.Equal("UnActive", (await f.Pass(unrelated.Sn)).Status);
    }

    [SqlServerFact] // S12: two independent transactions serialize cancellation and SINGLE yielding.
    public async Task ConcurrentPurchaseAndCancellation_ShouldKeepOneActiveAndMatchingSnapshot()
    {
        await using var f = new TicketSqlFixture();
        var user = await f.AddStudent();
        var code = await f.AddPlan();
        var source = await f.SeedPass(user, code, "Expire", f.Clock.Now().Date.AddDays(-30), f.Clock.Now().Date.AddDays(-1));
        var child = await f.SeedPass(user, code, source: source.Sn);
        var single = await f.SeedPass(user, "SINGLE", "Active");
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task Cancel()
        {
            using var scope = f.Provider.CreateScope();
            await gate.Task;
            try { await scope.ServiceProvider.GetRequiredService<CancelQueuedRenewalHandler>().Handle(child.Id, f.Marker); }
            catch (InvalidOperationException) { Assert.Equal("Active", (await f.Pass(child.Sn)).Status); }
        }
        async Task Purchase()
        {
            await gate.Task;
            await f.Transaction(sp => sp.GetRequiredService<TicketPurchaseService>().PurchaseAsync(new()
            {
                BuyerId = user, BeneficiaryStudentIds = [user], TicketPlanKindCode = code,
                PaymentStatus = PaymentState.Paid, OperatorId = f.Marker
            }));
        }
        var cancellation = Cancel();
        var purchase = Purchase();
        gate.SetResult();
        await Task.WhenAll(cancellation, purchase);
        using var c = f.Open();
        var active = await c.QuerySingleAsync<string>("SELECT pass_id FROM dbo.sdt_ticket_pass WHERE owner_id=@user AND valid_status='Active'", new { user });
        Assert.Equal(active, await c.QuerySingleAsync<string>("SELECT sdt_cur_ticket_id FROM dbo.sdt_profile WHERE usr_id=@user", new { user }));
        Assert.Equal("UnActive", (await f.Pass(single.Sn)).Status);
        Assert.Equal(single.Remaining, (await f.Pass(single.Sn)).Remaining);
    }

    private static Task<CurrentTicketSnapshot?> Reconcile(TicketSqlFixture f, string user) =>
        f.Transaction(sp => sp.GetRequiredService<ITicketPassRepository>().ReconcileCurrentAsync(user, f.Clock.Today(), f.Clock.Now(), f.Marker, default));
}
