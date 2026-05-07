
namespace gym_system.Api.Contracts.Instructors
{
    public class GetInstructorsResponse
    {
        public IReadOnlyList<InstructorDto>? InstructorList { get; set; }
    }
    public class InstructorDto
    {
        /// <summary>
        /// 老師的使用者id (未來可能有老師專屬id)
        /// </summary>
        public required string Id { get; set; }

        /// <summary>
        /// 老師名字
        /// </summary>
        public required string Name { get; set; }
        
        /// <summary>
        /// 老師電話號碼
        /// </summary>
        public required string Phone { get; set; }
        
        /// <summary>
        /// 老師角色的啟用狀態 
        /// </summary>
        public required bool isActived { get; set; }

    }
}
