using gym_system.Domain.Repositories;

namespace gym_system.Application.ScheduleRulesUseCase.Commands.DeleteScheduleRule
{
    public class DeleteScheduleRuleHandler
    {
        private readonly IScheduleRuleRepository _scheduleRuleRepository;
        private readonly IUnitOfWork _unitOfWork;

        public DeleteScheduleRuleHandler(IScheduleRuleRepository scheduleRuleRepository, IUnitOfWork unitOfWork)
        {
            _scheduleRuleRepository = scheduleRuleRepository;
            _unitOfWork = unitOfWork;
        }

        public async Task<bool> Handle(DeleteScheduleRuleCommand command, CancellationToken ct)
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

            await _unitOfWork.BeginAsync(ct);
            try
            {
                var result = await _scheduleRuleRepository.DeleteAsync(command.RuleSn, ct);
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
