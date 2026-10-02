using Dapper;
using gym_system.Api.Controllers;
using gym_system.Application.MembersUseCase.Commands.UpdateStudent;
using gym_system.Application.MembersUseCase.Queries.GetStudentMemberList;
using gym_system.Domain.Exceptions;
using gym_system.Domain.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace gym_system.Api.Tests;

[Collection("Ticket SQL")]
public sealed class StudentMemberListTests
{
    private static Task<StudentMemberListResult> List(TicketSqlFixture f, GetStudentMemberListQuery? query = null)
    {
        // Infrastructure query factory opens/disposes a connection per request.
        var service = f.Provider.GetRequiredService<IStudentMemberListQueryService>();
        return new GetStudentMemberListHandler(service, f.Clock).Handle(
            query ?? new() { Keyword = f.Marker, PageSize = 100 }, default);
    }

    private static UpdateStudentHandler Editor(IServiceProvider sp) => new(
        sp.GetRequiredService<IUserRepository>(), sp.GetRequiredService<IUserRoleRepository>(),
        sp.GetRequiredService<IStudentProfileRepository>(), sp.GetRequiredService<IUnitOfWork>());

    [Fact]
    public void EndpointsRequireAdmin()
    {
        var auth = Assert.Single(typeof(StudentsController).GetCustomAttributes(typeof(AuthorizeAttribute), true));
        Assert.Equal("Admin", ((AuthorizeAttribute)auth).Roles);
    }

    [Theory]
    [InlineData(0, 15)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    [InlineData(int.MaxValue, 100)]
    public async Task InvalidPaginationIsRejectedBeforeDatabase(int page, int size)
    {
        var handler = new GetStudentMemberListHandler(null!, null!);
        await Assert.ThrowsAsync<ArgumentException>(() => handler.Handle(new() { Page = page, PageSize = size }, default));
    }

    [Fact]
    public async Task UnsupportedFiltersAreRejected()
    {
        var handler = new GetStudentMemberListHandler(null!, null!);
        await Assert.ThrowsAsync<ArgumentException>(() => handler.Handle(new() { ValidStates = ["Expired"] }, default));
        await Assert.ThrowsAsync<ArgumentException>(() => handler.Handle(new() { PaymentStates = ["VisitedToday"] }, default));
    }

    [SqlServerFact]
    public async Task SelectionUsesOldestEffectiveActiveOtherwiseLatestIncludingCancelled()
    {
        await using var f = new TicketSqlFixture();
        var plan = await f.AddPlan();
        var user = await f.AddStudent();
        var old = await f.SeedPass(user, plan, "Active", end: f.Clock.Now().AddDays(20), paidAt: f.Clock.Now().AddDays(-3));
        await f.SeedPass(user, plan, "Active", end: f.Clock.Now().AddDays(25), paidAt: f.Clock.Now().AddDays(-2));
        var cancelled = await f.SeedPass(user, plan, "Cancelled", paidAt: f.Clock.Now());
        var selected = Assert.Single((await List(f)).Items);
        Assert.Equal(old.Id, selected.CurrentPassId);
        Assert.Equal(f.Marker, selected.CurrentPlanName);
        using var c = f.Open();
        await c.ExecuteAsync("UPDATE dbo.sdt_ticket_pass SET valid_edate=@end WHERE owner_id=@user AND valid_status='Active'",
            new { user, end = f.Clock.Now().Date.AddDays(-1) });
        selected = Assert.Single((await List(f)).Items);
        Assert.Equal(cancelled.Id, selected.CurrentPassId);
        Assert.Equal("Cancelled", selected.DisplayValidState);
        Assert.Equal("Paid", selected.PaymentState);
        // The read did not reconcile/expire the stale Active rows.
        Assert.Equal(2, await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.sdt_ticket_pass WHERE owner_id=@user AND valid_status='Active'", new { user }));
    }

    [SqlServerFact]
    public async Task CalendarBoundariesAndFiltersDoNotMutateLifecycle()
    {
        await using var f = new TicketSqlFixture();
        var plan = await f.AddPlan();
        var expected = new Dictionary<string, string>();
        foreach (var days in new int?[] { -1, 0, 7, 8, null })
        {
            var user = await f.AddStudent();
            await f.SeedPass(user, plan, "Active", end: days.HasValue ? f.Clock.Now().Date.AddDays(days.Value) : null);
            expected[user] = days == -1 ? "Expire" : days is 0 or 7 ? "Expiring" : "Active";
        }
        var all = await List(f);
        Assert.Equal(5, all.TotalCount);
        foreach (var row in all.Items)
        {
            Assert.Equal("Active", row.ValidStatus);
            Assert.Equal(expected[row.Id], row.DisplayValidState);
            if (row.ValidEndDate is not null) Assert.Matches("^\\d{4}-\\d{2}-\\d{2}$", row.ValidEndDate);
        }
        var active = await List(f, new() { Keyword = f.Marker, ValidStates = ["Active"] });
        Assert.Equal(4, active.TotalCount);
        var expiring = await List(f, new() { Keyword = f.Marker, ValidStates = ["Expiring"] });
        Assert.Equal(2, expiring.TotalCount);
        var combined = await List(f, new() { Keyword = f.Marker, ValidStates = ["Expiring", "Expire"], PaymentStates = ["Paid"] });
        Assert.Equal(3, combined.TotalCount);
        var unpaid = await List(f, new() { Keyword = f.Marker, PaymentStates = ["UnPaid"] });
        Assert.Empty(unpaid.Items);
    }

    [SqlServerFact]
    public async Task ScopePaginationAndLiteralSearchAreAppliedBeforePaging()
    {
        await using var f = new TicketSqlFixture();
        var ids = new List<string>();
        for (var index = 0; index < 17; index++) ids.Add(await f.AddStudent());
        var noRole = await f.AddStudent();
        var noProfile = await f.AddStudent();
        using var c = f.Open();
        await c.ExecuteAsync("DELETE dbo.user_role WHERE usr_id=@noRole; DELETE dbo.sdt_profile WHERE usr_id=@noProfile;", new { noRole, noProfile });
        await c.ExecuteAsync("""
            INSERT dbo.user_role (usr_id,bmc_role_id,user_role_is_active,user_role_cdt)
            SELECT @id,bmc_role_id,1,@now FROM dbo.bmc_role WHERE bmc_role_code='Instructor';
            """, new { id = ids[0], now = f.Clock.Now() });
        var first = await List(f, new() { Keyword = "  " + f.Marker + "  " });
        Assert.Equal(17, first.TotalCount);
        Assert.Equal(15, first.Items.Count);
        Assert.All(first.Items, x => { Assert.Null(x.CurrentPassId); Assert.Null(x.DisplayValidState); Assert.Null(x.PaymentState); });
        var second = await List(f, new() { Keyword = f.Marker, Page = 2 });
        Assert.Equal(2, second.Items.Count);
        Assert.Equal(17, first.Items.Concat(second.Items).Select(x => x.Id).Distinct().Count());
        var emptyPage = await List(f, new() { Keyword = f.Marker, Page = 3 });
        Assert.Empty(emptyPage.Items);
        Assert.Equal(17, emptyPage.TotalCount);
        var phone = await c.QuerySingleAsync<string>("SELECT usr_phone FROM dbo.users WHERE usr_id=@id", new { id = ids[0] });
        Assert.Equal(ids[0], Assert.Single((await List(f, new() { Keyword = " " + phone + " " })).Items).Id);
        Assert.Empty((await List(f, new() { Keyword = f.Marker + "%" })).Items);
        Assert.Empty((await List(f, new() { Keyword = f.Marker + "_" })).Items);
        Assert.Empty((await List(f, new() { Keyword = f.Marker + "[" })).Items);
        Assert.Empty((await List(f, new() { Keyword = f.Marker, ValidStates = ["Expire"] })).Items);
    }

    [SqlServerFact]
    public async Task SameTimestampUsesPassIdAndPaymentBelongsToSelectedPass()
    {
        await using var f = new TicketSqlFixture();
        var plan = await f.AddPlan();
        var user = await f.AddStudent();
        var first = await f.SeedPass(user, plan, "Active", end: f.Clock.Now().Date.AddDays(1));
        var next = await f.SeedPass(user, plan, "UnActive");
        using var c = f.Open();
        await c.ExecuteAsync("UPDATE dbo.order_items SET order_items_payment_state='UnPaid',order_items_paid_at=NULL WHERE order_items_sn=@sn", new { sn = next.ItemSn });
        var row = Assert.Single((await List(f)).Items);
        Assert.Equal(first.Id, row.CurrentPassId);
        Assert.Equal("Paid", row.PaymentState);
        await c.ExecuteAsync("UPDATE dbo.sdt_ticket_pass SET valid_status='Cancelled' WHERE pass_sn=@sn", new { sn = first.Sn });
        row = Assert.Single((await List(f)).Items);
        Assert.Equal(next.Id, row.CurrentPassId);
        Assert.Equal("UnPaid", row.PaymentState);
    }

    [SqlServerFact]
    public async Task SortingUsesStateThenUnpaidThenStableUserId()
    {
        await using var f = new TicketSqlFixture();
        var plan = await f.AddPlan();
        var expected = new List<string>();
        using var c = f.Open();
        // Insert in reverse display order so insertion order cannot accidentally satisfy the assertion.
        foreach (var state in new string?[] { null, "Cancelled", "Depleted", "Expire", "UnActive", "Active", "Expiring" })
        {
            var user = await f.AddStudent();
            expected.Insert(0, user);
            if (state is not null)
                await f.SeedPass(user, plan, state == "Expiring" ? "Active" : state,
                    end: state == "Expiring" ? f.Clock.Now().Date.AddDays(7) : f.Clock.Now().Date.AddDays(8));
        }
        var unpaidUser = await f.AddStudent();
        var unpaidPass = await f.SeedPass(unpaidUser, plan, "Active", end: f.Clock.Now().Date);
        await c.ExecuteAsync("UPDATE dbo.order_items SET order_items_payment_state='UnPaid',order_items_paid_at=NULL WHERE order_items_sn=@sn", new { sn = unpaidPass.ItemSn });
        expected.Insert(0, unpaidUser);
        Assert.Equal(expected, (await List(f)).Items.Select(x => x.Id));
    }

    [SqlServerFact]
    public async Task EditingReusesPhoneChecksAndUniqueConstraintAndPreservesOtherData()
    {
        await using var f = new TicketSqlFixture();
        var a = await f.AddStudent();
        var b = await f.AddStudent();
        using var scope = f.Provider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var original = (await repo.FindUserByIdAsync(a, default))!;
        var other = (await repo.FindUserByIdAsync(b, default))!;
        var editor = Editor(scope.ServiceProvider);
        try
        {
            await editor.Handle(new(a, f.Marker + " edited", original.Phone), default);
            Assert.Equal(f.Marker + " edited", (await repo.FindUserByIdAsync(a, default))!.Name);
        }
        finally
        {
            // Restore this fixture-owned user's marker so guarded cleanup can verify ownership.
            await repo.UpdateBasicProfileAsync(a, f.Marker, original.Phone, default);
        }
        // Whitespace normalization and unchanged own phone are accepted.
        await editor.Handle(new(a, " " + f.Marker + " ", " " + original.Phone + " "), default);
        var error = await Assert.ThrowsAsync<MemberRegistrationRejectedException>(() => editor.Handle(new(a, f.Marker, other.Phone), default));
        Assert.Equal("MEMBER_PHONE_ALREADY_REGISTERED", error.Code);
        // Bypass application precheck to exercise the database constraint translation.
        await Assert.ThrowsAsync<MemberRegistrationRejectedException>(() => repo.UpdateBasicProfileAsync(a, f.Marker, other.Phone, default));
        Assert.Equal(original.Phone, (await repo.FindUserByIdAsync(a, default))!.Phone);
        var phone = f.NewPhone();
        await editor.Handle(new(a, f.Marker, phone), default);
        Assert.Equal(phone, (await repo.FindUserByIdAsync(a, default))!.Phone);
        await Assert.ThrowsAsync<ArgumentException>(() => editor.Handle(new(a, " ", phone), default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => editor.Handle(new("not-a-student", f.Marker, phone), default));
    }
}
