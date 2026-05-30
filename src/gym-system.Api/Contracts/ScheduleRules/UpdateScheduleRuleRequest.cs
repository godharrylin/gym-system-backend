namespace gym_system.Api.Contracts.ScheduleRules
{
    public class UpdateScheduleRuleRequest
    {
        public required string ClassId { get; set; }
        public required string InstructorId { get; set; }
        public required int DayOfWeek { get; set; }
        public string StartTime { get; set; } = string.Empty;
        public int Duration { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
