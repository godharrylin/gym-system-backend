namespace gym_system.Application.InstructorUseCase.Command.UpdateInstructor
{
    public sealed class UpdateInstructorCommand
    {
        public string UserId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public bool IsEmployed { get; set; }
    }
}
