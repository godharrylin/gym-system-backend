using gym_system.Api.Contracts.ScheduleRules;
using gym_system.Application.ScheduleRulesUseCase.Commands.CreateScheduleRule;
using gym_system.Application.ScheduleRulesUseCase.Commands.UpdateScheduleRule;
using gym_system.Application.ScheduleRulesUseCase.Queries;
using Microsoft.AspNetCore.Mvc;

namespace gym_system.Api.Controllers
{
    //  排課模板

    [ApiController]
    [Route("api/v1/schedule-rules")]
    public class ScheduleRuleController : ControllerBase
    {
        private readonly GetScheduleRulesQueryHandler _getScheduleRulesQueryHandler;
        private readonly CreateScheduleRuleHandler _createScheduleRuleHandler;
        private readonly UpdateScheduleRuleHandler _updateScheduleRuleHandler;

        public ScheduleRuleController(
            GetScheduleRulesQueryHandler getScheduleRulesQueryHandler,
            CreateScheduleRuleHandler createScheduleRuleHandler,
            UpdateScheduleRuleHandler updateScheduleRuleHandler)
        {
            _getScheduleRulesQueryHandler = getScheduleRulesQueryHandler;
            _createScheduleRuleHandler = createScheduleRuleHandler;
            _updateScheduleRuleHandler = updateScheduleRuleHandler;
        }

        /// <summary>
        /// 取得排課模板列表
        /// </summary>
        /// <remarks> 純粹顯示現有模板課程到UI </remarks>
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
        [HttpPost]
        public async Task<ActionResult<bool>> CreateScheduleRuleAsync([FromBody] CreateScheduleRuleRequest request, CancellationToken ct)
        {
            if (!TimeSpan.TryParse(request.StartTime, out TimeSpan parsedStartTime))
            {
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

        /// <summary>
        /// 更新排課模板
        /// </summary>
        [HttpPost("{ruleSn}")]
        public async Task<ActionResult<bool>> UpdateScheduleRuleAsync([FromRoute] string ruleSn, [FromBody] UpdateScheduleRuleRequest request, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(ruleSn))
            {
                throw new ArgumentException($"ruleSn 格式錯誤，不能為null 或空字串");
            }

            if (!TimeSpan.TryParse(request.StartTime, out TimeSpan parsedStartTime))
            {
                throw new ArgumentException("上課時間格式不正確，請輸入如 14:30 的格式", nameof(request.StartTime));
            }

            var command = new UpdateScheduleRuleCommand
            {
                RuleSn = ruleSn,
                ClassId = request.ClassId,
                InstructorId = request.InstructorId,
                DayOfWeek = (System.DayOfWeek)request.DayOfWeek,
                StartTime = parsedStartTime,
                Duration = request.Duration,
                IsActive = request.IsActive
            };

            var result = await _updateScheduleRuleHandler.Handle(command, ct);
            return result;
        }
    }
}
