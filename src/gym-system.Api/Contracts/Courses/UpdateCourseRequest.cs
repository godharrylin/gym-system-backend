namespace gym_system.Api.Contracts.Courses
{
    public class UpdateCourseRequest
    {
        public string Name { get; set; } = string.Empty;
        public required string InstructorId { get; set; }
        public required int Duration { get; set; }
        public string Color { get; set; } = string.Empty;
        public bool IsFree { get; set; }
    }
}
