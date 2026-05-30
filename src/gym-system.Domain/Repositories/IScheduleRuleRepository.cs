using gym_system.Domain.Entities.ScheduleRules;
using System;
using System.Collections.Generic;
using System.Text;

namespace gym_system.Domain.Repositories
{
    public interface IScheduleRuleRepository
    {
        public Task<ScheduleRule?> GetOverlappingSchedulesRuleAsync(ScheduleRule newScheduleRule, CancellationToken ct);
        public Task<bool> AddAsync(ScheduleRule newScheduleRule, CancellationToken ct);
    }
}
