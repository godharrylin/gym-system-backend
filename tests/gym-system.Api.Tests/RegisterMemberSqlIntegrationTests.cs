using System.Data;
using gym_system.Application.MembersUseCase.Commands.RegisterMember;
using gym_system.Application.OrdersUseCase.Services;
using gym_system.Application.TicketPlansUseCase.Queries;
using gym_system.Domain.Entities.Members;
using gym_system.Domain.Entities.Orders;
using gym_system.Domain.Entities.Tickets;
using gym_system.Domain.Entities.Users;
using gym_system.Domain.Enums;
using gym_system.Domain.Repositories;
using gym_system.Infrastructures;
using gym_system.Infrastructures.Connections;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace gym_system.Api.Tests;

[Collection("Ticket SQL")]
public sealed class RegisterMemberSqlIntegrationTests
{
    [SqlServerFact] // E01/E12: registration -> unpaid -> member payment.
    public async Task RegisterUnpaidThenPay_ShouldUseCurrentMemberRulesAndPaidDate()
    {
        await using var f = new TicketSqlFixture();
        var plan = await f.AddPlan();
        using var scope = f.Provider.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<RegisterMemberHandler>().Handle(new()
        {
            Members = [new() { Name = f.Marker, Phone = f.NewPhone() }],
            TicketPurchase = new() { TicketPlanKindId = plan, PaymentStatus = PaymentState.UnPaid }
        });
        var user = Assert.Single(result.MemberIds);
        f.TrackUser(user);
        using var c = f.Open();
        Assert.Equal(0, await Dapper.SqlMapper.ExecuteScalarAsync<int>(c, "SELECT COUNT(*) FROM dbo.sdt_ticket_pass WHERE owner_id=@user", new { user }));
        var role = await scope.ServiceProvider.GetRequiredService<IUserRoleRepository>().GetUserRoleAsync(user, UserRoleCode.Student, default);
        Assert.Equal(f.Clock.Now(), role!.AssignedAt);
        await f.AddRule(plan, "NEW_ONLY");
        f.Clock.Value = f.Clock.Value.AddDays(2);
        await scope.ServiceProvider.GetRequiredService<gym_system.Application.OrdersUseCase.Commands.PayOrder.PayOrderHandler>().Handle(new()
        {
            OrderId = result.OrderId!, PaymentMethod = "Cash", OperatorId = f.Marker
        });
        var passSn = await Dapper.SqlMapper.QuerySingleAsync<int>(c, "SELECT pass_sn FROM dbo.sdt_ticket_pass WHERE owner_id=@user", new { user });
        var pass = await f.Pass(passSn);
        Assert.Equal("Active", pass.Status);
        Assert.Equal(f.Clock.Now(), pass.PaidAt);
        Assert.Equal(f.Clock.Now().Date, pass.Start);
    }

    [SqlServerFact] // E07/E11: actual rollback leaves no registration.
    public async Task RegistrationNewOnlyUnpaid_ShouldRejectAndRollbackUser()
    {
        await using var f = new TicketSqlFixture();
        var plan = await f.AddPlan(rule: "NEW_ONLY");
        using var scope = f.Provider.CreateScope();
        var error = await Assert.ThrowsAsync<gym_system.Domain.Exceptions.TicketPurchaseRejectedException>(() =>
            scope.ServiceProvider.GetRequiredService<RegisterMemberHandler>().Handle(new()
            {
                Members = [new() { Name = f.Marker, Phone = f.NewPhone() }],
                TicketPurchase = new() { TicketPlanKindId = plan, PaymentStatus = PaymentState.UnPaid }
            }));
        Assert.Equal("TICKET_PLAN_NOT_AVAILABLE", error.Code);
        using var c = f.Open();
        Assert.Equal(0, await Dapper.SqlMapper.ExecuteScalarAsync<int>(c, "SELECT COUNT(*) FROM dbo.users WHERE usr_name=@Marker", new { f.Marker }));
    }

    [SqlServerFact]
    public async Task RegisterPaidSingles_ShouldCreateOneActiveAndFourQueued()
    {
        await using var f = new TicketSqlFixture();
        using var scope = f.Provider.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<RegisterMemberHandler>().Handle(new()
        {
            Members = [new() { Name = f.Marker, Phone = f.NewPhone() }],
            TicketPurchase = new() { TicketPlanKindId = "SINGLE", Quantity = 5, PaymentStatus = PaymentState.Paid }
        });
        var user = Assert.Single(result.MemberIds);
        f.TrackUser(user);
        using var c = f.Open();
        var states = (await Dapper.SqlMapper.QueryAsync<string>(c, "SELECT valid_status FROM dbo.sdt_ticket_pass WHERE owner_id=@user", new { user })).ToList();
        Assert.Equal(5, states.Count);
        Assert.Single(states, x => x == "Active");
        Assert.Equal(4, states.Count(x => x == "UnActive"));
        Assert.Equal(0, await Dapper.SqlMapper.ExecuteScalarAsync<int>(c, "SELECT COUNT(*) FROM dbo.sdt_ticket_pass WHERE owner_id=@user AND (valid_sdate IS NOT NULL OR valid_edate IS NOT NULL)", new { user }));
    }

    [SqlServerFact]
    public async Task DuplicatePhone_ShouldRollbackWithoutDeletingExistingTestUser()
    {
        await using var f = new TicketSqlFixture();
        var user = await f.AddStudent();
        using var scope = f.Provider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var existing = (await repo.FindUserByIdAsync(user, default))!;
        var error = await Assert.ThrowsAsync<gym_system.Domain.Exceptions.MemberRegistrationRejectedException>(() => f.Transaction(async sp =>
            await sp.GetRequiredService<IUserRepository>().AddAsync(User.Register(f.Marker, existing.Phone, "test-only"), default)));
        Assert.Equal("MEMBER_PHONE_ALREADY_REGISTERED", error.Code);
        var sqlError = Assert.IsType<Microsoft.Data.SqlClient.SqlException>(error.InnerException);
        Assert.Contains(sqlError.Number, new[] { 2601, 2627 });
        Assert.NotNull(await repo.FindUserByIdAsync(user, default));
    }

    [SqlServerFact]
    public async Task ConcurrentRegistrationWithSamePhone_ShouldCreateAtMostOneMember()
    {
        await using var f = new TicketSqlFixture();
        var phone = f.NewPhone();

        async Task<(RegisterMembersResult? Result, Exception? Error)> Register()
        {
            using var scope = f.Provider.CreateScope();
            try
            {
                var result = await scope.ServiceProvider
                    .GetRequiredService<RegisterMemberHandler>()
                    .Handle(new RegisterMembersCommand
                    {
                        Members = [new MemberRegisterInput { Name = f.Marker, Phone = phone }]
                    });
                return (result, null);
            }
            catch (Exception error)
            {
                return (null, error);
            }
        }

        var attempts = await Task.WhenAll(Register(), Register());
        var success = Assert.Single(attempts, attempt => attempt.Result is not null);
        var failure = Assert.Single(attempts, attempt => attempt.Error is not null);
        var userId = Assert.Single(success.Result!.MemberIds);
        f.TrackUser(userId);

        var registrationError = Assert.IsType<gym_system.Domain.Exceptions.MemberRegistrationRejectedException>(
            failure.Error);
        Assert.Equal("MEMBER_PHONE_ALREADY_REGISTERED", registrationError.Code);

        using var connection = f.Open();
        Assert.Equal(
            1,
            await Dapper.SqlMapper.ExecuteScalarAsync<int>(
                connection,
                "SELECT COUNT(*) FROM dbo.users WHERE usr_phone=@phone",
                new { phone }));
    }
}
