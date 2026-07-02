using gym_system.Domain.Entities.Users;

namespace gym_system.Application.AuthUseCase.Tokens
{
    public interface IAuthTokenGenerator
    {
        AuthTokenResult Generate(User user, IReadOnlyList<string> roles);
    }
}
