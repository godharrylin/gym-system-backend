using System.ComponentModel.DataAnnotations;

namespace gym_system.Application.MembersUseCase.Commands.RegisterMember
{
    public class RegisterMemberCommand 
    {
        public string Name { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
    }
}
