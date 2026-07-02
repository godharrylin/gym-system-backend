namespace gym_system.Api.Authentication
{
    public interface IRefreshTokenValidator
    {
        RefreshTokenValidationResult? Validate(string refreshToken);
    }
}
