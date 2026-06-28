namespace gym_system.Application.AuthUseCase.LoginByPhone
{
    public sealed class LoginByPhoneCommand
    {
        public string Phone { get; init; } = string.Empty;
    }
}
