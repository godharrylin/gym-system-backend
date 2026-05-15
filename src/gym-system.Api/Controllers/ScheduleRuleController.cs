using Microsoft.AspNetCore.Mvc;
using gym_system.Api.Contracts.ScheduleRules;
using gym_system.Application.ScheduleRulesUseCase.Queries;

namespace gym_system.Api.Controllers
{
    //  排課模板

    [ApiController]
    [Route("api/v1/schedule-rules")]
    public class ScheduleRuleController: ControllerBase
    {
        private readonly GetScheduleRulesQueryHandler _getScheduleRulesQueryHandler;
        public ScheduleRuleController(GetScheduleRulesQueryHandler getScheduleRulesQueryHandler) 
        {
            _getScheduleRulesQueryHandler = getScheduleRulesQueryHandler;
        }

        /// <summary>
        /// 取得排課模板列表
        /// </summary>
        /// <remarks> 純粹顯示現有模板課程到UI </remarks>
        /// <param name="request"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        [HttpGet]
        public async Task<ActionResult<GetScheduleRulesQueryResponse>> GetScheduleRulesQueryAsync(CancellationToken ct)
        {
            var result = await _getScheduleRulesQueryHandler.Handle(ct);

            var response = new GetScheduleRulesQueryResponse
            {
                WeeklyScheduleList = result.Select(x => new ScheduleRulesDto
                {
                    RuleSn = x.cls_scdle_rules_sn,
                    ClassDefId = x.class_id,
                    DayOfWeek = x.cls_scdle_rules_day_wk,
                    Duration = x.cls_scdle_duration,
                    StartTime = x.cls_scdle_rules_st.ToString(@"hh\:mm"),
                    EndTime = x.cls_scdle_rules_et.ToString(@"hh\:mm"),
                    InstructorId = x.cls_scdle_instructor_id,
                    IsActive = x.cls_scdle_rules_is_active
                }).ToList()
            };
            return Ok(response);
        }
    }
}
