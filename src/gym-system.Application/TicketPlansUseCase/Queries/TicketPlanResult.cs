using System;
using System.Collections.Generic;
using System.Text;

namespace gym_system.Application.TicketPlansUseCase.Queries
{
    //  為了不汙染 Domain
    public sealed class TicketPlanResult
    {
        public required string Id { get; set; }
        /// <summary>
        /// 票券類別，堂票或月票
        /// </summary>
        public required string Type { get; set; }
        public required string Name { get; set; }
        /// <summary>
        /// 票券單價
        /// </summary>
        public required decimal Price { get; set; }
        /// <summary>
        /// 到期天數
        /// </summary>
        public int Days { get; set; }
        /// <summary>
        /// 可使用堂數
        /// </summary>
        public int Sessions { get; set; }
        
        /// <summary>
        /// 票券方案標籤，用來控制畫面顯示
        /// </summary>
        public string[]? Tags { get; set; } = new string[0];
        public string? Description { get; set; }
        
    }
}
