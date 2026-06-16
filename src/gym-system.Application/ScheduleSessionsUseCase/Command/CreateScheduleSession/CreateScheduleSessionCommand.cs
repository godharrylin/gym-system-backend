using System;
using System.Collections.Generic;
using System.Text;

namespace gym_system.Application.ScheduleSessionsUseCase.Command.CreateScheduleSession
{
    public sealed class CreateScheduleSessionCommand
    {
        public required string ClassId { get; set; }
        public string Date { get; set; } = string.Empty;
        public required string StartTime { get; set; }
        public bool? IsFree { get; set; }
    }
}
