using System;
using System.Collections.Generic;
using System.Text;

namespace gym_system.Application.ScheduleRulesUseCase.Queries
{
    public class GetScheduleRulesQueryHandler
    {
        private readonly IScheduleRulesQueryService _scheduleRulesService;
        public GetScheduleRulesQueryHandler(IScheduleRulesQueryService scheduleRulesService) 
        {
            _scheduleRulesService = scheduleRulesService;
        }

        public async Task<IReadOnlyList<ScheduleRulesResult>> Handle(CancellationToken ct)
        => await _scheduleRulesService.GetScheduleRulesAsync(ct);

       

    }
}
