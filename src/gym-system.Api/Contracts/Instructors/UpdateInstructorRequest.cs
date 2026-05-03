namespace gym_system.Api.Contracts.Instructors
{
    public sealed class UpdateInstructorRequest
    {
        public string Name { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public bool IsEmployed { get; set; }
    }
}
