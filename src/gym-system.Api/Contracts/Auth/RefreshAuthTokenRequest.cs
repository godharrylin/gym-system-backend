namespace gym_system.Api.Contracts.Auth
{
    public sealed class RefreshAuthTokenRequest
    {
        public string RefreshToken { get; init; } = string.Empty;
    }
}
