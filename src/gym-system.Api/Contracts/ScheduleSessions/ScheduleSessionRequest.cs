namespace gym_system.Api.Contracts.ScheduleSessions
{
    // 更新排課實例使用；未傳入的欄位代表保留原值。
    public class ScheduleSessionRequest
    {
        public string? date { get; set; }
        public string? classId { get; set; } //  soft reference
        public string? instructorId { get; set; }
        public bool? isFree { get; set; }
        public string? startTime { get; set; }
        public string? status { get; set; }
    }
}
