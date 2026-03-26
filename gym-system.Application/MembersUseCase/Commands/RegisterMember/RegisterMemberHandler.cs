using gym_system.Domain.Repositories;

namespace gym_system.Application.MembersUseCase.Commands.RegisterMember
{
    public class RegisterMemberHandler
    {
        private readonly IMemberRepository _memberRepository;

        public RegisterMemberHandler(IMemberRepository memberRepository)
        {
            _memberRepository = memberRepository;
        }

        public async Task Handle(RegisterMemberCommand command)
        {
            if (string.IsNullOrEmpty(command.Name))
                throw new Exception("姓名必填");
            if (string.IsNullOrEmpty(command.Phone))
                throw new Exception("手機必填");

            var phoneExists = await _memberRepository.CheckPhoneExistAsync(command.Phone);
            if (phoneExists)
                throw new Exception("手機號碼已被註冊");

            //  下一步建立Member
        }
    }
}
