using Microsoft.AspNetCore.Mvc;
using gym_system.Api.Contracts.ScheduleRules;
using gym_system.Application.ScheduleRulesUseCase.Commands.CreateScheduleRule;
using gym_system.Application.ScheduleRulesUseCase.Queries;
using System.Threading.Tasks.Dataflow;

namespace gym_system.Api.Controllers
{
    //  排課模板

    [ApiController]
    [Route("api/v1/schedule-rules")]
    public class ScheduleRuleController: ControllerBase
    {
        private readonly GetScheduleRulesQueryHandler _getScheduleRulesQueryHandler;
        private readonly CreateScheduleRuleHandler _createScheduleRuleHandler;
        public ScheduleRuleController(GetScheduleRulesQueryHandler getScheduleRulesQueryHandler,
            CreateScheduleRuleHandler createScheduleRuleHandler) 
        {
            _getScheduleRulesQueryHandler = getScheduleRulesQueryHandler;
            _createScheduleRuleHandler = createScheduleRuleHandler;
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

        /// <summary>
        /// 新增排課模板
        /// </summary>
        /// <param name="request"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        [HttpPost]
        public async Task<ActionResult<bool>> CreateScheduleRuleAsync([FromBody] CreateScheduleRuleRequest request, CancellationToken ct)
        {
            if(!TimeSpan.TryParse(request.StartTime, out TimeSpan parsedStartTime))
            {
                // 如果轉換失敗，直接在這裡拋出例外擋掉 (或回傳 400 Bad Request 給前端)
                throw new ArgumentException("上課時間格式不正確，請輸入如 14:30 的格式", nameof(request.StartTime));
            }
            var command = new CreateScheduleRuleCommand
            {
                ClassId = request.ClassId,
                InstructorId = request.InstructorId,
                DayOfWeek = (System.DayOfWeek)request.DayOfWeek,
                StartTime = parsedStartTime,
                Duration = request.duration
            };
            var result = await _createScheduleRuleHandler.Handle(command, ct);
            return result;
        }

        //[HttpPost]

    }
}
