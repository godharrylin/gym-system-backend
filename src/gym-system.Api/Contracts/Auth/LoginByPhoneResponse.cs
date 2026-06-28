namespace gym_system.Api.Contracts.Auth
{
    public sealed class LoginByPhoneResponse
    {
        public string AccessToken { get; init; } = string.Empty;
        public DateTime ExpiresAt { get; init; }
        public LoginUserResponse User { get; init; } = new();
    }

    public sealed class LoginUserResponse
    {
        public string Id { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Phone { get; init; } = string.Empty;
        public IReadOnlyList<string> Roles { get; init; } = [];
    }
}
