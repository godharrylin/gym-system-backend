using gym_system.Domain.Entities.ScheduleRules;
using gym_system.Domain.Enums;
using gym_system.Domain.Repositories;

namespace gym_system.Application.ScheduleRulesUseCase.Commands.CreateScheduleRule
{
    public class CreateScheduleRuleHandler
    {
        private readonly ICourseRepository _courseRepository;
        private readonly IUserRoleRepository _roleRepository;
        private readonly IScheduleRuleRepository _scheduleRuleRepository;
        private readonly IUnitOfWork _unitOfWork;

        public CreateScheduleRuleHandler(ICourseRepository courseRepository, IUserRoleRepository roleRepository,
                            IScheduleRuleRepository scheduleRuleRepository, IUnitOfWork unitOfWork)
        {
            _courseRepository = courseRepository;
            _roleRepository = roleRepository;
            _scheduleRuleRepository = scheduleRuleRepository;
            _unitOfWork = unitOfWork;
        }
        public async Task<bool> Handle(CreateScheduleRuleCommand command ,CancellationToken ct)
        {
            //  1. 確認 command 合法性
            //      1) 檢查class id , instructor 是否存在
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
                //      2) 檢查DayOfWork 是否小於 0，放到Schedule Rule entity 處理
                var newScheduleRule = ScheduleRule.Create(command.ClassId, command.DayOfWeek, command.StartTime, command.Duration, command.InstructorId);

                //      3) 檢查時間是否有和原本的schedule template衝堂
                var ConfilcSchedule = await _scheduleRuleRepository.GetOverlappingSchedulesRuleAsync(newScheduleRule, ct);
                if (ConfilcSchedule is not null)
                {
                    throw new InvalidOperationException($"該排課已和排課{ConfilcSchedule.RuleSn}衝堂");
                }

                //  2. 新增該筆schedule template
                var result = await _scheduleRuleRepository.AddAsync(newScheduleRule, ct);
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
