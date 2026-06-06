namespace gym_system.Application.ScheduleSessionsUseCase.GetOrEnsureScheduleWeek
{
    public sealed class GetOrEnsureScheduleWeekCommand
    {
        /// <summary>
        /// 該週的週一日期
        /// </summary>
        public DateOnly WeekStart { get; set; }
    }
}
