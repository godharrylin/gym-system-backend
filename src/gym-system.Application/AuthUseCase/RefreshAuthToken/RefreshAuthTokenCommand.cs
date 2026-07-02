namespace gym_system.Application.AuthUseCase.RefreshAuthToken
{
    public sealed class RefreshAuthTokenCommand
    {
        public string UserId { get; init; } = string.Empty;
    }
}
