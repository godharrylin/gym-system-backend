namespace gym_system.Domain.Entities.ScheduleSessions
{
    public sealed class ScheduleSessionTemplate
    {
        private ScheduleSessionTemplate(
            string ruleSn,
            string classId,
            string className,
            string classLabelColor,
            bool isFree,
            DayOfWeek dayOfWeek,
            TimeSpan startTime,
            TimeSpan endTime,
            string instructorId,
            string instructorName)
        {
            RuleSn = ruleSn;
            ClassId = classId;
            ClassName = className;
            ClassLabelColor = classLabelColor;
            IsFree = isFree;
            DayOfWeek = dayOfWeek;
            StartTime = startTime;
            EndTime = endTime;
            InstructorId = instructorId;
            InstructorName = instructorName;
        }

        public string RuleSn { get; }
        public string ClassId { get; }
        public string ClassName { get; }
        public string ClassLabelColor { get; }
        public bool IsFree { get; }
        public DayOfWeek DayOfWeek { get; }
        public TimeSpan StartTime { get; }
        public TimeSpan EndTime { get; }
        public string InstructorId { get; }
        public string InstructorName { get; }

        public static ScheduleSessionTemplate Rehydrate(
            string ruleSn,
            string classId,
            string className,
            string classLabelColor,
            bool isFree,
            DayOfWeek dayOfWeek,
            TimeSpan startTime,
            TimeSpan endTime,
            string instructorId,
            string instructorName)
        {
            return new ScheduleSessionTemplate(
                ruleSn,
                classId,
                className,
                classLabelColor,
                isFree,
                dayOfWeek,
                startTime,
                endTime,
                instructorId,
                instructorName);
        }
    }
}
