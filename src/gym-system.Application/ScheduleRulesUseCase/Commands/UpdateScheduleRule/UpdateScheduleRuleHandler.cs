using gym_system.Domain.Entities.ScheduleRules;
using gym_system.Domain.Enums;
using gym_system.Domain.Repositories;

namespace gym_system.Application.ScheduleRulesUseCase.Commands.UpdateScheduleRule
{
    public class UpdateScheduleRuleHandler
    {
        private readonly ICourseRepository _courseRepository;
        private readonly IUserRoleRepository _roleRepository;
        private readonly IScheduleRuleRepository _scheduleRuleRepository;
        private readonly IUnitOfWork _unitOfWork;

        public UpdateScheduleRuleHandler(
            ICourseRepository courseRepository,
            IUserRoleRepository roleRepository,
            IScheduleRuleRepository scheduleRuleRepository,
            IUnitOfWork unitOfWork)
        {
            _courseRepository = courseRepository;
            _roleRepository = roleRepository;
            _scheduleRuleRepository = scheduleRuleRepository;
            _unitOfWork = unitOfWork;
        }

        public async Task<bool> Handle(UpdateScheduleRuleCommand command, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(command.RuleSn))
            {
                throw new InvalidOperationException("ruleSn 不可為空");
            }

            var existingRule = await _scheduleRuleRepository.GetByIdAsync(command.RuleSn, ct);
            if (existingRule is null)
            {
                throw new InvalidOperationException("排課模板不存在");
            }

            var course = await _courseRepository.GetByIdAsync(command.ClassId, ct);
            if (course is null)
            {
                throw new InvalidOperationException("輸入課程不存在 !!");
            }

            var instructor = await _roleRepository.GetUserRoleAsync(command.InstructorId, UserRoleCode.Instructor, ct);
            if (instructor is null || instructor.IsActive == false)
            {
                throw new InvalidOperationException("指導老師不存在或未被啟用");
            }

            await _unitOfWork.BeginAsync(ct);
            try
            {
                var duration = command.Duration > 0 ? command.Duration : course.Duration;

                var updateRule = ScheduleRule.Rehydrate(
                    command.RuleSn,
                    command.ClassId,
                    command.DayOfWeek,
                    command.StartTime,
                    duration,
                    command.StartTime + TimeSpan.FromMinutes(duration),
                    existingRule.BufferTime,
                    command.InstructorId,
                    command.IsActive);

                var conflictSchedule = await _scheduleRuleRepository.GetOverlappingSchedulesRuleAsync(
                    updateRule,
                    ct,
                    excludeRuleSn: command.RuleSn);

                if (conflictSchedule is not null)
                {
                    throw new InvalidOperationException($"該排課已和排課{conflictSchedule.ClassId}衝堂");
                }

                var result = await _scheduleRuleRepository.UpdateAsync(updateRule, ct);
                if (result)
                {
                    await _unitOfWork.CommitAsync(ct);
                }
                else
                {
                    await _unitOfWork.RollbackAsync(ct);
                }

                return result;
            }
            catch
            {
                await _unitOfWork.RollbackAsync(ct);
                throw;
            }
        }
    }
}
