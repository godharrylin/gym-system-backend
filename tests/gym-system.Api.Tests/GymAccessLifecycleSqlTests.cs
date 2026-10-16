using Dapper;
using gym_system.Application.TicketsUseCase.Services;
using gym_system.Domain.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace gym_system.Api.Tests;

[Collection("Ticket SQL")]
public sealed class GymAccessLifecycleSqlTests
{
    [SqlServerFact]
    public async Task DepletedSource_ShouldActivateRenewalToday_AndSynchronizeProfileInCallerTransaction()
    {
        await using var f = new TicketSqlFixture();
        var student = await f.AddStudent();
        var code = await f.AddPlan(type: "PACK");
        var source = await f.SeedPass(student, code, "Depleted", f.Clock.Now().Date.AddDays(-10),
            f.Clock.Now().Date.AddDays(20), paidAt: f.Clock.Now().AddDays(-10),
            endedAt: f.Clock.Now().AddMinutes(-10), reason: "Depleted", remaining: 0);
        var renewal = await f.SeedPass(student, code, paidAt: f.Clock.Now().AddDays(-1), source: source.Sn);

        var result = await f.Transaction(sp => sp.GetRequiredService<ICurrentTicketService>()
            .ReconcileAsync(student, f.Clock.Now(), f.Marker));

        Assert.Equal(renewal.Id, result!.TicketId);
        Assert.Equal(f.Clock.Today(), result.TicketStartDate);
        Assert.Equal(f.Clock.Today().AddDays(29), result.TicketExpireDate);
        using var connection = f.Open();
        Assert.Equal(renewal.Id, await connection.QuerySingleAsync<string>(
            "SELECT sdt_cur_ticket_id FROM dbo.sdt_profile WHERE usr_id=@student", new { student }));
    }
}
