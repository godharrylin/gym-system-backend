namespace gym_system.Domain.Entities.ScheduleSessions
{
    public sealed class ScheduleSession
    {
        private ScheduleSession(
            int arrangeSn,
            string arrangeId,
            DateOnly date,
            string classId,
            string className,
            string classLabelColor,
            string instructorId,
            string instructorName,
            DateTime startAt,
            DateTime endAt,
            string status,
            string source,
            string ruleSn,
            bool isFree)
        {
            ArrangeSn = arrangeSn;
            ArrangeId = arrangeId;
            Date = date;
            ClassId = classId;
            ClassName = className;
            ClassLabelColor = classLabelColor;
            InstructorId = instructorId;
            InstructorName = instructorName;
            StartAt = startAt;
            EndAt = endAt;
            Status = status;
            Source = source;
            RuleSn = ruleSn;
            IsFree = isFree;
        }

        public int ArrangeSn { get; }
        public string ArrangeId { get; }
        public DateOnly Date { get; }
        public string ClassId { get; }
        public string ClassName { get; }
        public string ClassLabelColor { get; }
        public string InstructorId { get; }
        public string InstructorName { get; }
        public DateTime StartAt { get; }
        public DateTime EndAt { get; }
        public string Status { get; }
        public string Source { get; }
        public string RuleSn { get; }
        public bool IsFree { get; }

        public static ScheduleSession CreateAutoFromTemplate(ScheduleSessionTemplate template, DateOnly weekStart)
        {
            if (!Enum.IsDefined(typeof(DayOfWeek), template.DayOfWeek))
            {
                throw new InvalidOperationException("排課模板星期格式不正確");
            }

            var daysSinceMonday = ((int)template.DayOfWeek + 6) % 7;
            var sessionDate = weekStart.AddDays(daysSinceMonday);
            var startAt = sessionDate.ToDateTime(TimeOnly.FromTimeSpan(template.StartTime));
            var endAt = sessionDate.ToDateTime(TimeOnly.FromTimeSpan(template.EndTime));

            if (endAt <= startAt)
            {
                throw new InvalidOperationException("課程結束時間必須晚於開始時間");
            }

            return new ScheduleSession(
                arrangeSn: 0,
                arrangeId: string.Empty,
                date: sessionDate,
                classId: template.ClassId,
                className: template.ClassName,
                classLabelColor: template.ClassLabelColor,
                instructorId: template.InstructorId,
                instructorName: template.InstructorName,
                startAt: startAt,
                endAt: endAt,
                status: "Open",
                source: "Auto",
                ruleSn: template.RuleSn,
                isFree: template.IsFree);
        }

        public static ScheduleSession Rehydrate(
            int arrangeSn,
            string arrangeId,
            DateOnly date,
            string classId,
            string className,
            string classLabelColor,
            string instructorId,
            string instructorName,
            DateTime startAt,
            DateTime endAt,
            string status,
            string source,
            string ruleSn,
            bool isFree)
        {
            return new ScheduleSession(
                arrangeSn,
                arrangeId,
                date,
                classId,
                className,
                classLabelColor,
                instructorId,
                instructorName,
                startAt,
                endAt,
                status,
                source,
                ruleSn,
                isFree);
        }
    }
}
