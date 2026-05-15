namespace gym_system.Api.Contracts.ScheduleRules
{
    public class GetScheduleRulesQueryResponse
    {
        public IReadOnlyList<ScheduleRulesDto>? WeeklyScheduleList { get; set; }
    }

    public record ScheduleRulesDto
    {
        /// <summary>
        /// 排課模板流水號
        /// </summary>
        public required int RuleSn { get; set; }
        
        /// <summary>
        /// 預設課程 ID
        /// </summary>
        public required string ClassDefId { get; set; }
        public int DayOfWeek { get; set; }
        public required string StartTime { get; set; }
        public int Duration { get; set; }
        public required string EndTime { get; set; } 
        public required string InstructorId { get; set; }
        public bool? IsActive { get; set; }
    }
}
