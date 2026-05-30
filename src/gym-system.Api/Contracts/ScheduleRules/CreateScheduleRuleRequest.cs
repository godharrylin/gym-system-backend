namespace gym_system.Api.Contracts.ScheduleRules
{
    public class CreateScheduleRuleRequest
    {
        public required string ClassId { get; set; }
        public required string InstructorId { get; set; }
        public required int DayOfWeek { get; set; }
        public string StartTime { get; set; } = string.Empty;
        public int duration { get; set; }
    }
}
