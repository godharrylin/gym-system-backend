namespace gym_system.Application.TicketPlansUseCase.Queries
{
    public enum TicketPlanEligibilityContextKind
    {
        ExistingMember = 1,
        Registration = 2
    }

    public static class TicketPlanRulePolicy
    {
        public const string NewOnly = "NEW_ONLY";
        public const string Renewal = "RENEWAL";
        public const string FamilyEligible = "FAMILY_ELIGIBLE";
        public const string Hidden = "HIDDEN";

        private static readonly string[] PausedRules = [FamilyEligible];
        private static readonly string[] DisplayOnlyRules = [Hidden];

        // Catalog SQL and eligibility projection must use the same classification.
        public static string[] GetDisplayOnlyRuleCodes() => [.. DisplayOnlyRules];

        public static string[] GetNonEligibilityRuleCodes() =>
            [.. DisplayOnlyRules, .. PausedRules];

        public static bool IsPausedRule(string ruleCode) =>
            PausedRules.Contains(ruleCode.Trim(), StringComparer.OrdinalIgnoreCase);

        public static bool IsDisplayOnlyRule(string ruleCode) =>
            DisplayOnlyRules.Contains(ruleCode.Trim(), StringComparer.OrdinalIgnoreCase);

        public static bool IsEligibilityRule(string ruleCode) =>
            !IsDisplayOnlyRule(ruleCode) && !IsPausedRule(ruleCode);

        public static string[] BuildEligibilityRuleCodes(IEnumerable<string> ruleCodes)
        {
            return ruleCodes
                .Where(ruleCode => !string.IsNullOrWhiteSpace(ruleCode))
                .Select(ruleCode => ruleCode.Trim())
                .Where(IsEligibilityRule)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }
}
