namespace gym_system.Application.AuthUseCase.Tokens
{
    public sealed class AuthTokenResult
    {
        public string AccessToken { get; init; } = string.Empty;
        public DateTime AccessTokenExpiresAt { get; init; }
        public string RefreshToken { get; init; } = string.Empty;
        public DateTime RefreshTokenExpiresAt { get; init; }
    }
}
