namespace gym_system.Api.Contracts.Courses
{
    public class CreateCourseRequest
    {
        public required string Name { get; set; }
        public required string InstructorId { get; set; }
        public int Duration { get; set; }
        public string Color { get; set; } = string.Empty;
        public bool? IsFree { get; set; }
        public string? Type { get; set; }
    }
}
