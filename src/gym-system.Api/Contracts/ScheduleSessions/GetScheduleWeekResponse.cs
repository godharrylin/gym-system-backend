namespace gym_system.Api.Contracts.ScheduleSessions
{
    public sealed class GetScheduleWeekResponse
    {
        public required string WeekStart { get; set; }
        public required string WeekEnd { get; set; }
        public required string Source { get; set; }
        public bool Created { get; set; }
        public IReadOnlyList<ScheduleSessionDto> Sessions { get; set; } = [];
    }

    public sealed class ScheduleSessionDto
    {
        public required string SessionId { get; set; }
        public string? ScheduleId { get; set; }
        public required string Date { get; set; }
        public int DayOfWeek { get; set; }
        public required string StartTime { get; set; }
        public required string EndTime { get; set; }
        public required string ClassDefId { get; set; }
        public required string ClassName { get; set; }
        public required string InstructorId { get; set; }
        public required string InstructorName { get; set; }
        public int Duration { get; set; }
        public required string Color { get; set; }
        public bool IsFree { get; set; }
        public required string Status { get; set; }
        public required string Source { get; set; }
    }
}
