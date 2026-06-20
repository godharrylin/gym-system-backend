using gym_system.Domain.Entities.Members;
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
}
