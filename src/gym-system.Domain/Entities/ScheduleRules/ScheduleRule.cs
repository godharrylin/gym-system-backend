
namespace gym_system.Domain.Entities.ScheduleRules
{
    public sealed class ScheduleRule
    {
        /// <summary>
        ///  schedule rule sn
        /// </summary>
        public string RuleSn { get; set; } = string.Empty;
        public string ClassId { get; private set; } = string.Empty;
        public System.DayOfWeek DayOfWeek { get; private set; }
        public TimeSpan StartTime { get; private set; }
        public int Duration { get; private set; }
        public TimeSpan EndTime { get; private set; }
        public int BufferTime { get; private set; }
        public string InstructorId { get; private set; } = string.Empty;
        public bool IsActive { get; private set; }

        private ScheduleRule( string ruleSn , string classId, System.DayOfWeek dayOfweek, TimeSpan startTime,
                    int duration , TimeSpan endTime, int bufferTime ,string instructorId, bool isActive)
        {
            RuleSn = ruleSn;
            ClassId = classId;
            DayOfWeek = dayOfweek;
            StartTime = startTime;
            Duration = duration;
            EndTime = endTime;
            BufferTime = bufferTime;
            InstructorId = instructorId;
            IsActive = isActive;
        }

        public static ScheduleRule Create(string classId, System.DayOfWeek dayOfWeek, TimeSpan startTime, int duration, string instructorId)
        {
            if (string.IsNullOrWhiteSpace(classId))
            {
                throw new ArgumentException("ClassId 不得為 null 或空字串", nameof(classId));
            }

            if (string.IsNullOrWhiteSpace(instructorId))
            {
                throw new ArgumentException("InstructroId 不得為 null 或空字串", nameof(instructorId));
            }


            CheckValidity(dayOfWeek, duration);
            var endTime = CalculateEndTime(startTime, duration);

            // 檢查是否跨天 (如果總天數大於等於 1，代表超過了 23:59:59)
            // 假設 startTime 是 23:00:00，duration 是 120 分鐘： endTime 算出來會是 1.01:00:00(1 天又 1 小時)。 
            // 此時 endTime.Days 會等於 1，endTime.TotalDays 會是大於 1 的小數（約 1.0416），
            // 只要它 >= 1，就代表時間已經跨過當天的 23:59:59 到隔天了
            if (endTime.TotalDays >= 1)
            {
                throw new ArgumentException("課程結束時間不可跨越到隔天", nameof(startTime));
            }

            var bufferTime = 10;
            var isActive = true;
            return new ScheduleRule( "",classId.Trim(), dayOfWeek, startTime, duration, endTime, bufferTime, instructorId.Trim(), isActive);
        }

        public static ScheduleRule Rehydrate(
            string ruleSn,
            string classId,
            System.DayOfWeek dayOfWeek,
            TimeSpan startTime,
            int duration,
            TimeSpan endTime,
            int bufferTime,
            string instructorId,
            bool isActive)
        {
            if (string.IsNullOrWhiteSpace(classId))
            {
                throw new ArgumentException("ClassId 不得為 null 或空字串", nameof(classId));
            }

            if (string.IsNullOrWhiteSpace(instructorId))
            {
                throw new ArgumentException("InstructroId 不得為 null 或空字串", nameof(instructorId));
            }

            CheckValidity(dayOfWeek, duration);
            return new ScheduleRule(
                ruleSn,
                classId.Trim(),
                dayOfWeek,
                startTime,
                duration,
                endTime,
                bufferTime,
                instructorId.Trim(),
                isActive);
        }

        /// <summary>
        /// 輸入的合法性驗證
        /// </summary>
        /// <param name="dayOfWeek">週</param>
        /// <param name="duration">上課時長</param>
        private static void CheckValidity(System.DayOfWeek dayOfWeek, int duration)
        {
            // 檢查這個值是否真的存在於 System.DayOfWeek 定義中 (0~6)
            if (!Enum.IsDefined(typeof(DayOfWeek), dayOfWeek))
            {
                throw new ArgumentOutOfRangeException(nameof(dayOfWeek), "無效的星期數值");
            }

            if (duration <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(duration), "上課時長不可為負數或0");
            }
        }

        private static TimeSpan CalculateEndTime(TimeSpan startTime, int duration)
        {
            return startTime + TimeSpan.FromMinutes(duration);
        }
    }
}
