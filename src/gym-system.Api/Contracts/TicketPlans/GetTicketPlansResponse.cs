namespace gym_system.Api.Contracts.TicketPlans
{
    public class GetTicketPlansResponse
    {
        public IReadOnlyList<TicketPlanDto>? TicketPlans { get; set; }
    }

    public class TicketPlanDto
    {
        public required string Id { get; set; }
        public required string Name { get; set; }
        /// <summary>
        /// 票券單價
        /// </summary>
        public required string Price { get; set; } //  免費會顯示 Free，而不是 $0
        /// <summary>
        /// 到期天數
        /// </summary>
        public int Days { get; set; }
        /// <summary>
        /// 可使用堂數
        /// </summary>
        public string? Sessions { get; set; }   //  "UNLIMITED" or "10"
        /// <summary>
        /// 票券類別，堂票或月票
        /// </summary>
        public required string Type { get; set; }
        /// <summary>
        /// 票券方案標籤，用來控制畫面顯示
        /// </summary>
        public string[]? Tags { get; set; }
        public string? Description { get; set; }
    }
}
