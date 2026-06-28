using gym_system.Domain.Entities.Users;

namespace gym_system.Application.AuthUseCase.LoginByPhone
{
    public interface IAccessTokenGenerator
    {
        AccessTokenResult Generate(User user, IReadOnlyList<string> roles);
    }
}
