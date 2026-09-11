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
        var fake = new FakeTicketPlanCatalogQueryService
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

        var sut = new TicketPlansController(fake, new FakeEligibilityService());
        var action = await sut.GetActiveTicketPlansAsync(CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(action.Result);
        var response = Assert.IsType<GetTicketPlansResponse>(ok.Value);
        var plan = Assert.Single(response.TicketPlans!);

        Assert.Equal(2300m, plan.Price);

    }

    [Fact]
    public async Task GetActiveTicketPlansAsync_ShouldReturnCorrectTicketName()
    {
        var fake = new FakeTicketPlanCatalogQueryService
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

    [Fact]
    public async Task GetRegistrationPurchasableAsync_ShouldUseEligibilityService()
    {
        var fake = new FakeTicketPlanCatalogQueryService
        {
            Result =
            [
                new TicketPlanResult
                {
                    Id = "STANDARD",
                    Name = "Standard",
                    Price = 2300,
                    Days = 90,
                    Sessions = 10,
                    Type = "SESSION"
                },
                new TicketPlanResult
                {
                    Id = "NEW_ONLY",
                    Name = "New Only",
                    Price = 2300,
                    Days = 90,
                    Sessions = 10,
                    Type = "SESSION",
                    EligibilityRuleCodes = ["NEW_ONLY"]
                }
            ]
        };

        var sut = new TicketPlansController(fake, new FakeEligibilityService());

        var action = await sut.GetRegistrationPurchasableAsync(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(action.Result);
        var response = Assert.IsType<GetTicketPlansResponse>(ok.Value);
        var plan = Assert.Single(response.TicketPlans!);
        Assert.Equal("STANDARD", plan.Id);
    }

    private sealed class FakeTicketPlanCatalogQueryService : ITicketPlanCatalogQueryService
    {
        public IReadOnlyList<TicketPlanResult> Result { get; set; } = [];
        public Task<IReadOnlyList<TicketPlanResult>> GetActiveTicketPlansAsync(CancellationToken ct)
        {
            return Task.FromResult(Result);
        }
    }

    private sealed class FakeEligibilityService : ITicketPlanEligibilityService
    {
        public Task<StudentTicketPlanEligibilityContext?> GetEligibilityContextAsync(
            string studentId,
            CancellationToken ct) => throw new NotSupportedException();

        public StudentTicketPlanEligibilityContext CreateRegistrationContext() =>
            new()
            {
                Kind = TicketPlanEligibilityContextKind.Registration,
                IsActiveStudent = false,
                Now = new DateTime(2026, 9, 6, 10, 0, 0)
            };

        public Task<bool> CanPurchaseAsync(
            StudentTicketPlanEligibilityContext context,
            TicketPlanResult ticketPlan,
            CancellationToken ct) => Task.FromResult(ticketPlan.EligibilityRuleCodes.Length == 0);
    }
}


