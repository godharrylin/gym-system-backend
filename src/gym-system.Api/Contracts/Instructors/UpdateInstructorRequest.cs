namespace gym_system.Api.Contracts.Instructors
{
    public sealed class UpdateInstructorRequest
    {
        public string? Name { get; set; }
        public string? Phone { get; set; }
        public bool? IsEmployed { get; set; }
    }
}
