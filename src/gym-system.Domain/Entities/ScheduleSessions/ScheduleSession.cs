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
            SessionStatus status,
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
        public DateOnly Date { get; private set; }
        public string ClassId { get; private set; }
        public string ClassName { get; private set; }
        public string ClassLabelColor { get; private set; }
        public string InstructorId { get; private set; }
        public string InstructorName { get; private set; }
        public DateTime StartAt { get; private set; }
        public DateTime EndAt { get; private set; }
        public SessionStatus Status { get; private set; }
        public string Source { get; }
        public string RuleSn { get; }
        public bool IsFree { get; private set; }
        public DateTime? UpdateTime { get; private set; }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="template">模板課</param>
        /// <param name="weekStart">星期一的日期</param>
        /// <remarks>weekStart 一定要是星期一</remarks>
        /// <returns></returns>
        public static ScheduleSession CreateAutoFromTemplate(ScheduleSessionTemplate template, DateOnly weekStart)
        {
            if (!Enum.IsDefined(template.DayOfWeek))
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
                status: SessionStatus.Open, //  模板課程建立的預設為 Open
                source: "Auto",
                ruleSn: template.RuleSn,
                isFree: template.IsFree);
        }

        /// <summary>
        /// 主要用於 Repository 從資料庫讀取資料後，重新建立 Entity
        /// </summary>
        /// <returns></returns>
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
            SessionStatus status,
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


        public void Update(
            DateOnly date,
            string classId,
            string className,
            string classLabelColor,
            string instructorId,
            string instructorName,
            DateTime startAt,
            DateTime endAt,
            SessionStatus status,
            bool isFree,
            DateTime updateTime)
        {
            if (string.IsNullOrWhiteSpace(classId))
                throw new InvalidOperationException("課程 ID 不可為空");

            if (string.IsNullOrWhiteSpace(className))
                throw new InvalidOperationException("課程名稱不可為空");

            if (endAt <= startAt)
                throw new InvalidOperationException("結束時間必須晚於起始時間");

            if (DateOnly.FromDateTime(startAt) != date)
                throw new InvalidOperationException("開始時間與課程日期不一致");

            if (DateOnly.FromDateTime(endAt) != date)
                throw new InvalidOperationException("課程不可跨日");

            Date = date;
            ClassId = classId.Trim();
            ClassName = className.Trim();
            ClassLabelColor = classLabelColor?.Trim() ?? string.Empty;
            InstructorId = instructorId?.Trim() ?? string.Empty;
            InstructorName = instructorName?.Trim() ?? string.Empty;
            StartAt = startAt;
            EndAt = endAt;
            Status = status;
            IsFree = isFree;
            UpdateTime = updateTime;
        }
    }

    public enum SessionStatus
    {
        Open,
        Cancel,
        Finished,
        Ongoing,
    }
    
}
