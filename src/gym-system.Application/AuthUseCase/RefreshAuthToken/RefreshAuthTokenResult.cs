namespace gym_system.Application.AuthUseCase.RefreshAuthToken
{
    public sealed class RefreshAuthTokenResult
    {
        public string AccessToken { get; init; } = string.Empty;
        public DateTime AccessTokenExpiresAt { get; init; }
        public string RefreshToken { get; init; } = string.Empty;
        public DateTime RefreshTokenExpiresAt { get; init; }
        public RefreshUserResult User { get; init; } = new();
    }

    public sealed class RefreshUserResult
    {
        public string Id { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Phone { get; init; } = string.Empty;
        public IReadOnlyList<string> Roles { get; init; } = [];
    }
}
