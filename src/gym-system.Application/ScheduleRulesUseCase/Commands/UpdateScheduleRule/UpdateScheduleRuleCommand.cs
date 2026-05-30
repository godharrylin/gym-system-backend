namespace gym_system.Application.ScheduleRulesUseCase.Commands.UpdateScheduleRule
{
    public class UpdateScheduleRuleCommand
    {
        public required string RuleSn { get; set; }
        public required string ClassId { get; set; }
        public required string InstructorId { get; set; }
        public System.DayOfWeek DayOfWeek { get; set; }
        public TimeSpan StartTime { get; set; }
        public int Duration { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
