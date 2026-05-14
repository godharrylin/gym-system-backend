namespace gym_system.Application.InstructorsUseCase.Command.CreateInstructor
{
    public sealed class CreateInstructorCommand
    {
        public string Name { get; set; }= string.Empty;
        public string Phone { get; set; }= string.Empty;
    }
}
