namespace gym_system.Api.Authentication
{
    public sealed class JwtSettings
    {
        //  簽證者(本後端系統)
        public string Issuer { get; init; } = string.Empty;
        //  使用對象(可以使用本服務的系統)
        public string Audience { get; init; } = string.Empty;
        public string SecretKey { get; init; } = string.Empty;
        public int ExpiresMinutes { get; init; } = 60;
    }
}
