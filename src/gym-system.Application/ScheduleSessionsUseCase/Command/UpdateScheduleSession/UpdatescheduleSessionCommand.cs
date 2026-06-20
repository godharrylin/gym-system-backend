using System;
using System.Collections.Generic;
using System.Text;

namespace gym_system.Application.ScheduleSessionsUseCase.Command.UpdateScheduleSession
{
    public sealed class UpdateScheduleSessionCommand
    {
        public required string SessionId { get; set; }
        public string? Date { get; set; }
        public string? ClassId { get; set; }
        public string? InstructorId { get; set; }
        public string? StartTime { get; set; }
        public string? Status { get; set; }
        public bool? IsFree { get; set; }
        public string? OperatorId { get; set; }
        public string? Remark { get; set; }
    }
}
