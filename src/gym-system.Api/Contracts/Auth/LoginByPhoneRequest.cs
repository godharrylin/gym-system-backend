namespace gym_system.Api.Contracts.Auth
{
    public sealed class LoginByPhoneRequest
    {
        public string Phone { get; init; } = string.Empty;
    }
}
