using gym_system.Api.Contracts.TicketPlans;
using gym_system.Api.Controllers;
using gym_system.Application.TicketPlansUseCase.Queries;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace gym_system.Api.Tests;

public sealed class TicketPlansControllerTest
{
    [Fact]
    public async Task GetActiveTicketPlansAsync_ShouldReturnMappedResponse()
    {
        var fake = new FakeTicketPlanCatalogQuerySerivce
        {
            Result = 
            [
                new TicketPlanResult
                {
                    Id = "T_002",
                    Name = "Pack 10",
                    Price = 2300,
                    Days = 90,
                    Sessions = 10,
                    Type = "SESSION"
                }
            ]
        };

        var sut = new TicketPlansController(fake);
        var action = await sut.GetActiveTicketPlansAsync(CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(action.Result);
        var response = Assert.IsType<GetTicketPlansResponse>(ok.Value);
        var plan = Assert.Single(response.TicketPlans!);

        Assert.Equal("$2300", plan.Price);

    }

    [Fact]
    public async Task GetActiveTicketPlansAsync_ShouldReturnCorrectTicketName()
    {
        var fake = new FakeTicketPlanCatalogQuerySerivce
        {
            Result = 
                [
                    new TicketPlanResult
                    {
                        Id = "T_003",
                        Name = "Pack 10",
                        Price = 2300,
                        Days = 90,
                        Sessions = 10,
                        Type = "SESSION"
                    }
                ]
        };
    }

    private sealed class FakeTicketPlanCatalogQuerySerivce : ITicketPlanCatalogQuerySerivce
    {
        public IReadOnlyList<TicketPlanResult> Result { get; set; } = [];
        public Task<IReadOnlyList<TicketPlanResult>> GetActiveTicketPlansAsync(CancellationToken ct)
        {
            return Task.FromResult(Result);
        }
    }
}


