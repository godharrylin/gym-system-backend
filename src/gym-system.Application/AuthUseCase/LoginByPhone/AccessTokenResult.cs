namespace gym_system.Application.AuthUseCase.LoginByPhone
{
    public sealed class AccessTokenResult
    {
        public string AccessToken { get; init; } = string.Empty;
        public DateTime ExpiresAt { get; init; }
    }
}
