namespace gym_system.Domain.Entities.Courses
{
    //  在資料庫是 class 資料表，代表預設課程
    public sealed class Course
    {
        private Course(string id, string name, string labelColor, int duration, bool isFree,
                       string defaultInstructorId, string defaultInstructorName, bool isActive, string type)
        {
            Id = id;
            Name = name;
            LabelColor = labelColor;
            Duration = duration;
            IsFree = isFree;
            DefaultInstructorId = defaultInstructorId;
            DefaultInstructorName = defaultInstructorName;
            IsActive = isActive;
            Type = type;
        }
        public string Id { get; }
        public string Name { get; private set; }
        public string LabelColor { get; private set; }
        public int Duration { get; private set; }
        public bool IsFree { get; private set; }
        public string DefaultInstructorId { get; private set; }
        public string DefaultInstructorName { get; private set; }
        public bool IsActive { get; private set; }
        public string Type { get; private set; }

        /// <summary>
        /// 回填，SQL拿到的資料塞進這裡
        /// </summary>
        public static Course Rehydrate(
            string id,
            string name,
            string labelColor,
            int duration,
            bool isFree,
            string instructorId,
            string instructorName,
            bool isActive,
            string type)
        {
            // 使用建構子賦值
            return new Course(
                id,
                name,
                labelColor ?? string.Empty,
                duration,
                isFree,
                instructorId ?? string.Empty,
                instructorName ?? string.Empty,
                isActive,
                type ?? string.Empty);
        }
/// <summary>
/// 更新描述性屬性
/// </summary>
public void UpdateBasicInfo(string? className, string? labelColor, bool? isFree)
        {
            if(className is not null && !string.IsNullOrWhiteSpace(className))
                Name = className.Trim();
            if(labelColor is not null && !string.IsNullOrWhiteSpace(labelColor))
                LabelColor = labelColor.Trim();
            if(isFree is not null)
                IsFree = isFree.Value;
        }

        /// <summary>
        /// 更新上課時數
        /// </summary>
        /// <param name="duration"></param>
        /// <exception cref="InvalidOperationException"></exception>
        public void Reschedule(int duration)
        {
            if (duration <= 0) throw new InvalidOperationException("上課時數不可為負數或0");
            Duration = duration;
            //  not-finished 未來添加「判斷是否模板課程會衝堂」的領域事件
        }
        
        public void UpdateInstructor(string instructorId, string instructorName)
        {
            DefaultInstructorId = instructorId.Trim();
            DefaultInstructorName = instructorName.Trim();
        }
    }

    
}
