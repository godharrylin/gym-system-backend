namespace gym_system.Application.InstructorsUseCase.Command.UpdateInstructor
{
    public sealed class UpdateInstructorCommand
    {
        public string UserId { get; set; } = string.Empty;
        public string? Name { get; set; }
        public string? Phone { get; set; }
        public bool? IsEmployed { get; set; }
    }
}
