namespace gym_system.Application.AuthUseCase.LoginByPhone
{
    public sealed class LoginByPhoneResult
    {
        public string AccessToken { get; init; } = string.Empty;
        public DateTime ExpiresAt { get; init; }
        public LoginUserResult User { get; init; } = new();
    }

    public sealed class LoginUserResult
    {
        public string Id { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Phone { get; init; } = string.Empty;
        public IReadOnlyList<string> Roles { get; init; } = [];
    }
}
