namespace gym_system.Domain.Entities.Tickets
{
    public sealed class TicketPlanKind
    {
        public string Id { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        /// <summary>
        /// 票券類別，堂票或月票
        /// </summary>
        public TicketPlanType Type { get; init; }
        /// <summary>
        /// 單價
        /// </summary>
        public decimal Price { get; init; }
        /// <summary>
        /// 預設額度，堂票使用
        /// </summary>
        public int DefaultCredit { get; init; }
        /// <summary>
        /// 預設到期天數
        /// </summary>
        public int DefaultExpireDays { get; init; }
        public bool IsActive { get; init; }
    }

    public enum TicketPlanType
    {
        Pack = 1,
        MPass = 2
    }
}
