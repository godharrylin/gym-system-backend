namespace gym_system.Application.ScheduleRulesUseCase.Commands.CreateScheduleRule
{
    public class CreateScheduleRuleCommand
    {
        public required string ClassId { get; set; }
        public required string InstructorId { get; set; }
        public System.DayOfWeek DayOfWeek { get; set; }
        public TimeSpan StartTime { get; set; }
        public int Duration { get; set; }
    }
}
