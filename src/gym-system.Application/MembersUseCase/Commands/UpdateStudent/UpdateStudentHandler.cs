using gym_system.Domain.Enums;
using gym_system.Domain.Exceptions;
using gym_system.Domain.Repositories;

namespace gym_system.Application.MembersUseCase.Commands.UpdateStudent;

public sealed record UpdateStudentCommand(string UserId, string Name, string Phone, string OperatorId = "admin");

public sealed class UpdateStudentHandler(IUserRepository users, IUserRoleRepository roles,
    IStudentProfileRepository profiles, IUnitOfWork unitOfWork)
{
    public async Task Handle(UpdateStudentCommand command, CancellationToken ct)
    {
        var name = command.Name?.Trim() ?? "";
        var phone = command.Phone?.Trim() ?? "";
        if (name.Length is < 1 or > 50 || phone.Length is < 1 or > 20)
            throw new ArgumentException("姓名須為 1–50 字，電話須為 1–20 字");
        await unitOfWork.BeginAsync(ct);
        try
        {
            if (await users.FindUserByIdAsync(command.UserId, ct) is null
                || await roles.GetUserRoleAsync(command.UserId, UserRoleCode.Student, ct) is null
                || await profiles.FindByUserIdAsync(command.UserId, ct) is null)
                throw new KeyNotFoundException("找不到學員");
            if (await users.ExistsPhoneForOtherUserAsync(command.UserId, phone, ct))
                throw new MemberRegistrationRejectedException("MEMBER_PHONE_ALREADY_REGISTERED", "手機號碼已被其他使用者註冊");
            if (!await users.UpdateBasicProfileAsync(command.UserId, name, phone, ct))
                throw new KeyNotFoundException("學員資料已不存在");
            await unitOfWork.CommitAsync(ct);
        }
        catch
        {
            await unitOfWork.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
}
