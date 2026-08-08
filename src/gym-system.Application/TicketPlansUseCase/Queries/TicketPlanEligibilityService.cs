using gym_system.Domain.Enums;
using gym_system.Domain.Repositories;

namespace gym_system.Application.TicketPlansUseCase.Queries
{
    public sealed class TicketPlanEligibilityService : ITicketPlanEligibilityService
    {
        private readonly IStudentProfileRepository _studentProfileRepository;
        private readonly IUserRoleRepository _userRoleRepository;
        private readonly IClock _clock;
        private readonly IReadOnlyList<ITicketPlanEligibilityRule> _rules;

        public TicketPlanEligibilityService(
            IStudentProfileRepository studentProfileRepository,
            IUserRoleRepository userRoleRepository,
            IClock clock,
            IEnumerable<ITicketPlanEligibilityRule> rules)
        {
            _studentProfileRepository = studentProfileRepository;
            _userRoleRepository = userRoleRepository;
            _clock = clock;
            _rules = rules.ToArray();
        }

        public async Task<StudentTicketPlanEligibilityContext?> GetEligibilityContextAsync(
            string studentId,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(studentId))
            {
                return null;
            }

            var normalizedStudentId = studentId.Trim();
            var profile = await _studentProfileRepository.FindByUserIdAsync(normalizedStudentId, ct);
            if (profile is null)
            {
                return null;
            }

            var studentRole = await _userRoleRepository.GetUserRoleAsync(
                normalizedStudentId,
                UserRoleCode.Student,
                ct);
            if (studentRole is null || !studentRole.IsActive)
            {
                return null;
            }

            return new StudentTicketPlanEligibilityContext
            {
                StudentId = normalizedStudentId,
                IsActiveStudent = true,
                StudentAssignedAt = studentRole.AssignedAt,
                Now = _clock.Now()
            };
        }

        public async Task<bool> CanPurchaseAsync(
            StudentTicketPlanEligibilityContext context,
            TicketPlanResult ticketPlan,
            CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(ticketPlan);

            if (!context.IsActiveStudent)
            {
                return false;
            }

            foreach (var rule in _rules)
            {
                if (!rule.AppliesTo(ticketPlan))
                {
                    continue;
                }

                if (!await rule.IsSatisfiedAsync(context, ticketPlan, ct))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
