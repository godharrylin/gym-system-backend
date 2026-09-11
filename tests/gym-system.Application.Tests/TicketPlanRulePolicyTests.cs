using gym_system.Application.TicketPlansUseCase.Queries;
using Xunit;

namespace gym_system.Application.Tests;

public sealed class TicketPlanRulePolicyTests
{
    // E06: keep unknown restrictions so a missing handler fails closed.
    [Fact]
    public void BuildEligibilityRuleCodes_ShouldKeepRestrictionsAndExcludeDisplayAndPausedRules()
    {
        string[] tags = ["NEW_ONLY", "RENEWAL", "HIDDEN", "FAMILY_ELIGIBLE", "FUTURE_RESTRICTION"];

        var result = TicketPlanRulePolicy.BuildEligibilityRuleCodes(tags);

        Assert.Equal(["NEW_ONLY", "RENEWAL", "FUTURE_RESTRICTION"], result);
        Assert.Contains("HIDDEN", tags);
        Assert.Contains("FAMILY_ELIGIBLE", tags);
    }

    [Fact]
    public void BuildEligibilityRuleCodes_ShouldNormalizeAndDeduplicateRuleCodes()
    {
        var result = TicketPlanRulePolicy.BuildEligibilityRuleCodes(
            [" new_only ", "NEW_ONLY", " hidden ", "family_eligible", "", " ", " FUTURE_RULE "]);

        Assert.Equal(["new_only", "FUTURE_RULE"], result);
    }

    // SQL uses these exclusions for disabled rules. Restrictions must not be exempted.
    [Theory]
    [InlineData("NEW_ONLY", true, false)]
    [InlineData("RENEWAL", true, false)]
    [InlineData("FUTURE_RESTRICTION", true, false)]
    [InlineData("FAMILY_ELIGIBLE", false, false)]
    [InlineData("HIDDEN", false, true)]
    public void CatalogExclusions_ShouldAgreeWithEligibilityClassification(
        string code, bool eligibility, bool hidden)
    {
        Assert.Equal(eligibility, TicketPlanRulePolicy.BuildEligibilityRuleCodes([code]).Length == 1);
        Assert.Equal(!eligibility, TicketPlanRulePolicy.GetNonEligibilityRuleCodes().Contains(code));
        Assert.Equal(hidden, TicketPlanRulePolicy.GetDisplayOnlyRuleCodes().Contains(code));
    }
}
