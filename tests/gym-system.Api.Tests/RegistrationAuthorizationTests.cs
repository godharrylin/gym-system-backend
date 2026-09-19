using System.Reflection;
using gym_system.Api.Controllers;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace gym_system.Api.Tests;

public sealed class RegistrationAuthorizationTests
{
    [Fact]
    public void RegisterEndpoint_ShouldRequireAdminRole()
    {
        AssertRequiresAdmin(
            typeof(MemberController).GetMethod(nameof(MemberController.Register))!);
    }

    [Fact]
    public void RegistrationPurchasableEndpoint_ShouldRequireAdminRole()
    {
        AssertRequiresAdmin(
            typeof(TicketPlansController).GetMethod(
                nameof(TicketPlansController.GetRegistrationPurchasableAsync))!);
    }

    private static void AssertRequiresAdmin(MemberInfo endpoint)
    {
        var authorize = endpoint.GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorize);
        Assert.Equal("Admin", authorize.Roles);
    }
}
