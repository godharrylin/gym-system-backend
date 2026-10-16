using gym_system.Domain.Entities.Tickets;
using Xunit;

namespace gym_system.Application.Tests;

public sealed class RenewalActivationDateTests
{
    [Theory]
    [InlineData(TicketEndReason.Depleted, 1, 15)]
    [InlineData(TicketEndReason.Depleted, 15, 15)]
    [InlineData(TicketEndReason.Depleted, 16, 16)]
    [InlineData(TicketEndReason.Expire, 1, 16)]
    [InlineData(TicketEndReason.Expire, 15, 16)]
    [InlineData(TicketEndReason.Expire, 16, 16)]
    [InlineData(TicketEndReason.Expire, 17, 17)]
    public void Renewal_ShouldDistinguishDepletionFromExpiry_WithoutStartingBeforePayment(
        TicketEndReason reason, int paidDay, int expectedDay)
    {
        var result = TicketActivationSchedule.GetRenewalStartDate(
            new DateOnly(2026, 10, 15), new DateOnly(2026, 10, paidDay), reason);
        Assert.Equal(new DateOnly(2026, 10, expectedDay), result);
        Assert.Equal(result.AddDays(29), TicketActivationSchedule.GetEndDate(result, 30));
    }

    [Fact]
    public void CancelledSource_ShouldNotBecomeRenewableByDateCalculation()
    {
        Assert.Throws<InvalidOperationException>(() => TicketActivationSchedule.GetRenewalStartDate(
            new DateOnly(2026, 10, 15), new DateOnly(2026, 10, 1), TicketEndReason.Cancelled));
    }
}
