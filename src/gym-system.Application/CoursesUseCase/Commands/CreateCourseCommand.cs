namespace gym_system.Application.CoursesUseCase.Commands
{
    public class CreateCourseCommand
    {
        public string Name { get; set; } = string.Empty;
        public string InstructorId { get; set; } = string.Empty;
        public int Duration { get; set; }
        public string LabelColor { get; set; } = string.Empty;
        public bool IsFree { get; set; }
        public string Type { get; set; } = "Default";
    }
}
