namespace gym_system.Application.ScheduleSessionsUseCase.GetOrEnsureScheduleWeek
{
    public sealed class GetOrEnsureScheduleWeekResult
    {
        public required DateOnly WeekStart { get; set; }
        public required DateOnly WeekEnd { get; set; }
        public required string Source { get; set; }
        public bool Created { get; set; }
        public IReadOnlyList<ScheduleWeekSessionResult> Sessions { get; set; } = [];
    }

    public sealed class ScheduleWeekSessionResult
    {
        public int ArrangeSn { get; set; }
        public string ArrangeId { get; set; } = string.Empty;
        public DateOnly Date { get; set; }
        public DayOfWeek DayOfWeek { get; set; }
        public string StartTime { get; set; } = string.Empty;
        public string EndTime { get; set; } = string.Empty;
        public string ClassDefId { get; set; } = string.Empty;
        public string ClassName { get; set; } = string.Empty;
        public string InstructorId { get; set; } = string.Empty;
        public string InstructorName { get; set; } = string.Empty;
        public int Duration { get; set; }
        public string Color { get; set; } = string.Empty;
        public bool IsFree { get; set; }
        public string Status { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public string RuleSn { get; set; } = string.Empty;
    }
}
