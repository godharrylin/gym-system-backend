using gym_system.Domain.Enums;
using gym_system.Domain.Repositories;

namespace gym_system.Application.TicketPlansUseCase.Queries
{
    public interface ITicketPlanEligibilityService
    {
        Task<bool> CanPurchaseAsync(
            string studentId,
            TicketPlanResult ticketPlan,
            CancellationToken ct);
    }

    public sealed class TicketPlanEligibilityService : ITicketPlanEligibilityService
    {
        private const string NewOnlyRuleCode = "NEW_ONLY";
        private const int NewStudentEligibleDays = 30;

        private readonly IStudentProfileRepository _studentProfileRepository;
        private readonly IUserRoleRepository _userRoleRepository;
        private readonly IClock _clock;

        public TicketPlanEligibilityService(
            IStudentProfileRepository studentProfileRepository,
            IUserRoleRepository userRoleRepository,
            IClock clock)
        {
            _studentProfileRepository = studentProfileRepository;
            _userRoleRepository = userRoleRepository;
            _clock = clock;
        }

        /// <summary>
        /// 檢查學生購買票券的資格
        /// </summary>
        /// <param name="studentId"></param>
        /// <param name="ticketPlan"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        public async Task<bool> CanPurchaseAsync(string studentId, TicketPlanResult ticketPlan, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(studentId))
            {
                return false;
            }

            var normalizedStudentId = studentId.Trim();
            var profile = await _studentProfileRepository.FindByUserIdAsync(normalizedStudentId, ct);
            if (profile is null)
            {
                return false;
            }

            var studentRole = await _userRoleRepository.GetUserRoleAsync(normalizedStudentId, UserRoleCode.Student, ct);
            if (studentRole is null || !studentRole.IsActive)
            {
                return false;
            }

            if (!HasRule(ticketPlan, NewOnlyRuleCode))
            {
                return true;
            }

            return studentRole.AssignedAt >= _clock.Now().AddDays(-NewStudentEligibleDays);
        }

        private static bool HasRule(TicketPlanResult ticketPlan, string ruleCode)
        {
            return ticketPlan.Tags?.Contains(ruleCode, StringComparer.OrdinalIgnoreCase) == true;
        }
    }
}
