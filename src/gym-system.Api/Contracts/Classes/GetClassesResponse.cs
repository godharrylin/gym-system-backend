namespace gym_system.Api.Contracts.Classes
{
    public class GetClassesResponse
    {
        public IReadOnlyList<ClassDto>? ClassInfoList { get; set; }
    }

    public class ClassDto
    {
        public required string Id { get; set; }
        public required string Name { get; set; }
        public string Instructor { get; set; } = string.Empty;
        public string Color { get; set; } = string.Empty;
        /// <summary>
        /// 上課時數 (分鐘為單位)
        /// </summary>
        public required string Duration { get; set; }
        public bool IsFree { get; set; }
        public bool IsActive { get; set; }

    }

}
