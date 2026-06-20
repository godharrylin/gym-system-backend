namespace gym_system.Domain.Entities.ScheduleSessions
{
    public sealed class ScheduleSessionLog
    {
        private ScheduleSessionLog(
            int arrangeSn,
            string changedData,
            string? operatorId,
            string? remark)
        {
            ArrangeSn = arrangeSn;
            ChangedData = changedData;
            OperatorId = operatorId;
            Remark = remark;
        }

        public int ArrangeSn { get; }
        public string ChangedData { get; }
        public string? OperatorId { get; }
        public string? Remark { get; }

        public static ScheduleSessionLog Create(
            int arrangeSn,
            string changedData,
            string? operatorId,
            string? remark)
        {
            if (arrangeSn <= 0)
                throw new InvalidOperationException("排課流水號不可為空");

            if (string.IsNullOrWhiteSpace(changedData))
                throw new InvalidOperationException("異動資料不可為空");

            //if (string.IsNullOrWhiteSpace(operatorId))
            //    throw new InvalidOperationException("操作者不可為空");

            return new ScheduleSessionLog(
                arrangeSn,
                changedData,
                operatorId?.Trim(),
                remark?.Trim());
        }
    }
}
