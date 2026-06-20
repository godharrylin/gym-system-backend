namespace gym_system.Application.ScheduleSessionsUseCase.Command.CancelScheduleSession
{
    public sealed class CancelScheduleSessionCommand
    {
        public required string ArrangeId { get; set; }
        public string? OperatorId { get; set; }
        public string? Remark { get; set; }
    }
}
