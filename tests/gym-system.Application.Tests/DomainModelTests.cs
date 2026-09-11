using gym_system.Domain.Entities.Members;
using gym_system.Domain.Entities.Tickets;
using gym_system.Domain.Entities.Users;
using gym_system.Domain.Enums;
using Xunit;

namespace gym_system.Application.Tests;

public sealed class DomainModelTests
{
    [Fact]
    public void UserRegister_ShouldExposePlainPasswordAndActiveState()
    {
        var user = User.Register(" Amy ", " 0911222333 ", " 0911222333 ");

        Assert.Equal("Amy", user.Name);
        Assert.Equal("0911222333", user.Phone);
        Assert.Equal("0911222333", user.Password);
        Assert.True(user.IsActive);
    }

    [Fact]
    public void StudentProfileCreate_ShouldUseTrimmedUserId()
    {
        var profile = StudentProfile.Create(" U0000000001 ");

        Assert.Equal("U0000000001", profile.UserId);
        Assert.Null(profile.LastVisitAt);
        Assert.Null(profile.CurrentTicket);
    }

    [Fact]
    public void StudentProfileCreate_ShouldRejectBlankUserId()
    {
        Assert.Throws<InvalidOperationException>(() => StudentProfile.Create(" "));
    }

    [Fact]
    public void StudentProfileRehydrate_ShouldRestoreProfileState()
    {
        var visitedAt = new DateTime(2026, 6, 20, 10, 30, 0);
        var snapshot = new CurrentTicketSnapshot
        {
            TicketId = "PASS-1",
            TicketType = "PACK",
            TicketValidState = "Active",
            TicketPaymentState = "Paid",
            UpdatedAt = visitedAt
        };

        var profile = StudentProfile.Rehydrate("U0000000001", visitedAt, snapshot);

        Assert.Equal(visitedAt, profile.LastVisitAt);
        Assert.Same(snapshot, profile.CurrentTicket);
    }

    [Fact]
    public void StudentProfileRecordVisit_ShouldUpdateLastVisitAt()
    {
        var profile = StudentProfile.Create("U0000000001");
        var visitedAt = new DateTime(2026, 6, 20, 11, 0, 0);

        profile.RecordVisit(visitedAt);

        Assert.Equal(visitedAt, profile.LastVisitAt);
    }

    [Fact]
    public void UserRoleAssign_ShouldRejectUndefinedRoleCode()
    {
        Assert.Throws<InvalidOperationException>(() =>
            UserRole.Assign("U0000000001", (UserRoleCode)999, DateTime.UtcNow, true));
    }

    [Theory]
    [InlineData(TicketPlanType.Pack, "PACK")]
    [InlineData(TicketPlanType.MPass, "M_PASS")]
    public void TicketPassToSnapshot_ShouldUseDatabaseTicketType(
        TicketPlanType planType,
        string expectedTicketType)
    {
        var plan = new TicketPlanKind
        {
            Id = "PLAN-1",
            Name = "Test Plan",
            Type = planType,
            Price = 100,
            DefaultCredit = 10,
            DefaultExpireDays = 30,
            IsActive = true
        };
        var pass = TicketPass.IssueQueued(
            id: "PASS-1",
            ownerId: "U0000000001",
            orderId: "ORD-1",
            orderItemId: "ITEM-1",
            plan: plan);
        pass.Activate(new DateOnly(2026, 8, 15));

        var snapshot = pass.ToSnapshot();

        Assert.Equal(expectedTicketType, snapshot.TicketType);
    }
    [Fact]
    public void TicketPassActivate_ShouldCountActivationDateAsFirstValidDay()
    {
        var plan = new TicketPlanKind
        {
            Id = "MONTHLY",
            Name = "Monthly",
            Type = TicketPlanType.MPass,
            Price = 1960,
            DefaultExpireDays = 30,
            IsActive = true
        };
        var pass = TicketPass.IssueQueued("PASS-1", "U1", "O1", "I1", plan);

        pass.Activate(new DateOnly(2026, 8, 15));

        Assert.Equal(new DateOnly(2026, 8, 15), pass.ValidStartDate);
        Assert.Equal(new DateOnly(2026, 9, 13), pass.ValidEndDate);
    }

    [Fact]
    public void SinglePassActivate_ShouldKeepValidityDatesNull()
    {
        var plan = new TicketPlanKind
        {
            Id = "SINGLE",
            Name = "Single",
            Type = TicketPlanType.Pack,
            Price = 250,
            DefaultCredit = 1,
            DefaultExpireDays = null,
            IsActive = true
        };
        var pass = TicketPass.IssueQueued("PASS-1", "U1", "O1", "I1", plan);

        pass.Activate(new DateOnly(2026, 8, 15));

        Assert.Null(pass.ValidStartDate);
        Assert.Null(pass.ValidEndDate);
        Assert.Equal(TicketValidStatus.Active, pass.ValidStatus);
    }

    [Fact]
    public void SinglePassYield_ShouldReturnToUnActiveWithoutEndingPass()
    {
        var plan = new TicketPlanKind
        {
            Id = "SINGLE",
            Name = "Single",
            FamilyCode = "SINGLE",
            Type = TicketPlanType.Pack,
            Price = 250,
            DefaultCredit = 1,
            DefaultExpireDays = null,
            IsActive = true
        };
        var pass = TicketPass.IssueQueued("PASS-1", "U1", "O1", "I1", plan);
        pass.Activate(new DateOnly(2026, 8, 15));

        pass.YieldSingleToQueue();

        Assert.Equal(TicketValidStatus.UnActive, pass.ValidStatus);
        Assert.Equal(1, pass.CreditsRemaining);
        Assert.Null(pass.ValidStartDate);
        Assert.Null(pass.ValidEndDate);
        Assert.Null(pass.EndReason);
        Assert.Null(pass.EndedAt);
    }

    [Fact]
    public void TicketPassCancel_ShouldKeepRenewalSourceAndWriteCancelledEndState()
    {
        var plan = new TicketPlanKind
        {
            Id = "PACK_10_RENEW",
            Name = "10堂票(續約)",
            FamilyCode = "PACK_10",
            Type = TicketPlanType.Pack,
            Price = 2200,
            DefaultCredit = 10,
            DefaultExpireDays = 90,
            IsActive = true
        };
        var pass = TicketPass.IssueQueued(
            "PASS-2",
            "U1",
            "O1",
            "I1",
            plan,
            renewedFromPassSn: 15,
            paidAt: new DateTime(2026, 9, 1));
        var cancelledAt = new DateTime(2026, 9, 2, 12, 30, 0);

        pass.Cancel(cancelledAt);

        Assert.Equal(15, pass.RenewedFromPassSn);
        Assert.Equal(TicketValidStatus.Cancelled, pass.ValidStatus);
        Assert.Equal(TicketEndReason.Cancelled, pass.EndReason);
        Assert.Equal(cancelledAt, pass.EndedAt);
    }

    [Fact]
    public void TicketPassUseCredit_ShouldWriteDepletedAtWhenLastCreditIsUsed()
    {
        var plan = new TicketPlanKind
        {
            Id = "SINGLE",
            Name = "單堂票",
            FamilyCode = "SINGLE",
            Type = TicketPlanType.Pack,
            Price = 250,
            DefaultCredit = 1,
            DefaultExpireDays = null,
            IsActive = true
        };
        var pass = TicketPass.IssueQueued("PASS-1", "U1", "O1", "I1", plan);
        pass.Activate(new DateOnly(2026, 9, 3));
        var usedAt = new DateTime(2026, 9, 3, 19, 30, 0);

        pass.UseCredit(usedAt);

        Assert.Equal(0, pass.CreditsRemaining);
        Assert.Equal(TicketValidStatus.Depleted, pass.ValidStatus);
        Assert.Equal(TicketEndReason.Depleted, pass.EndReason);
        Assert.Equal(usedAt, pass.EndedAt);
    }

    [Fact]
    public void RenewalSchedule_ShouldStartDayAfterSourceEnd_WhenPaidEarly()
    {
        var result = TicketActivationSchedule.GetRenewalStartDate(
            new DateOnly(2026, 9, 30),
            new DateOnly(2026, 9, 3));

        Assert.Equal(new DateOnly(2026, 10, 1), result);
    }

    [Fact]
    public void RenewalSchedule_ShouldStartOnPaidDate_WhenPaidInGracePeriod()
    {
        var result = TicketActivationSchedule.GetRenewalStartDate(
            new DateOnly(2026, 9, 1),
            new DateOnly(2026, 9, 8));

        Assert.Equal(new DateOnly(2026, 9, 8), result);
    }

    [Theory]
    [InlineData(30, "2026-10-02")]
    [InlineData(90, "2026-12-01")]
    [InlineData(210, "2027-03-31")]
    [InlineData(420, "2027-10-27")]
    public void TicketSchedule_ShouldCountStartDateAsFirstDay(
        int expireDays,
        string expected)
    {
        var result = TicketActivationSchedule.GetEndDate(
            new DateOnly(2026, 9, 3),
            expireDays);

        Assert.Equal(DateOnly.Parse(expected), result);
    }
}
