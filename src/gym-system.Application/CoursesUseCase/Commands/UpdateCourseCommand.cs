using System;
using System.Collections.Generic;
using System.Text;

namespace gym_system.Application.CoursesUseCase.Commands
{
    public class UpdateCourseCommand
    {
        public required string Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public required string InstructorId { get; set; }
        public required int Duration { get; set; }
        public string? LabelColor { get; set; }
        public bool? IsFree { get; set; }
    }
}
