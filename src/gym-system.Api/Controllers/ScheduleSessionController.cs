using System.IdentityModel.Tokens.Jwt;
using gym_system.Api.Contracts.ScheduleRules;
using gym_system.Api.Contracts.ScheduleSessions;
using gym_system.Application.Common;
using gym_system.Application.ScheduleSessionsUseCase.Command.CancelScheduleSession;
using gym_system.Application.ScheduleSessionsUseCase.Command.CreateScheduleSession;
using gym_system.Application.ScheduleSessionsUseCase.Command.GetOrEnsureScheduleWeek;
using gym_system.Application.ScheduleSessionsUseCase.Command.UpdateScheduleSession;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Globalization;

namespace gym_system.Api.Controllers
{
    [ApiController]
    [Route("api/v1/admin/schedule-session")]
    public sealed class ScheduleSessionController : ControllerBase
    {
        private readonly GetOrEnsureScheduleWeekHandler _getOrEnsureScheduleWeekHandler;
        private readonly CreateScheduleSessionHandler _createScheduleSessionHandler;
        private readonly UpdateScheduleSessionHandler _updateScheduleSessionHandler;
        private readonly CancelScheduleSessionHandler _cancelScheduleSessionHandler;

        public ScheduleSessionController(GetOrEnsureScheduleWeekHandler getOrEnsureScheduleWeekHandler,
                                        CreateScheduleSessionHandler createScheduleSessionHandler,
                                        UpdateScheduleSessionHandler updateScheduleSessionHandler,
                                        CancelScheduleSessionHandler cancelScheduleSessionHandler)
        {
            _getOrEnsureScheduleWeekHandler = getOrEnsureScheduleWeekHandler;
            _createScheduleSessionHandler = createScheduleSessionHandler;
            _updateScheduleSessionHandler = updateScheduleSessionHandler;
            _cancelScheduleSessionHandler = cancelScheduleSessionHandler;
        }


        [HttpPost]
        public async Task<ActionResult<bool>> CreateScheduleSessionAsync([FromBody] ScheduleSessionRequest req, CancellationToken ct)
        {
            var command = new CreateScheduleSessionCommand
            {
                ClassId = req.classId ?? "",
                IsFree = req?.isFree,
                Date = req?.date ?? "",
                StartTime = req?.startTime ?? ""
            };
            return Ok(await _createScheduleSessionHandler.Handle(command, ct));
        }

        /// <summary>
        /// 更新某筆已存在的排課實例，partial update
        /// </summary>
        /// <param name="id"></param>
        /// <param name="req"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        [HttpPost("{id}")]
        [Authorize]
        public async Task<ActionResult<bool>> UpdateScheduleAsync([FromRoute] string id, [FromBody] ScheduleSessionRequest req, CancellationToken ct)
        {
            var operatorId = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
            if (string.IsNullOrWhiteSpace(operatorId))
            {
                return Unauthorized(new { message = "Token 缺少使用者識別" });
            }

            var cmd = new UpdateScheduleSessionCommand
            {
                SessionId = id.Trim(),
                Date = req.date?.Trim(),
                ClassId = req.classId?.Trim(),
                InstructorId = req.instructorId?.Trim(),
                StartTime = req.startTime,
                Status = req.status,
                IsFree = req.isFree,
                OperatorId = operatorId.Trim(),
                Remark = req.remark?.Trim()
            };

            try
            {
                return Ok(await _updateScheduleSessionHandler.Handle(cmd, ct));
            }
            catch (ForbiddenException)
            {
                return Forbid();
            }
        }

        /// <summary>
        /// 取消特定課程實例
        /// </summary>
        /// <param name="arrangeId"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        [HttpPost("{arrangeId}/cancel")]
        public async Task<ActionResult<bool>> CancelScheduleAsync(
            [FromRoute] string arrangeId,
            [FromBody] ScheduleSessionRequest? req,
            CancellationToken ct)
        {
            var cmd = new CancelScheduleSessionCommand
            {
                ArrangeId = arrangeId,
                OperatorId = req?.operatorId?.Trim(),
                Remark = req?.remark?.Trim()
            };

            return Ok(await _cancelScheduleSessionHandler.Handle(cmd, ct));
        }

        /// <summary>
        /// 撈取當週沒有被取消的實際課程表，如果該週沒有排課則會從模板課程先排課
        /// </summary>
        /// <param name="weekStart">該週的禮拜一日期</param>
        [HttpGet("week")]
        public async Task<ActionResult<GetScheduleWeekResponse>> GetWeekAsync([FromQuery] string weekStart, CancellationToken ct)
        {
            if (!DateOnly.TryParseExact(
                    weekStart,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var parsedWeekStart))
            {
                return BadRequest(new { message = "weekStart 格式錯誤，請使用 yyyy-MM-dd" });
            }

            var result = await _getOrEnsureScheduleWeekHandler.Handle(
                new GetOrEnsureScheduleWeekCommand
                {
                    WeekStart = parsedWeekStart
                },
                ct);
            var notCancelSession = result.Sessions.ToList().Where(session => session.Status != "Cancel");

            return Ok(new GetScheduleWeekResponse
            {
                WeekStart = result.WeekStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                WeekEnd = result.WeekEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Source = result.Source,
                Created = result.Created,
                Sessions = result.Sessions.Select(session => new ScheduleSessionDto
                {
                    SessionId = string.IsNullOrWhiteSpace(session.ArrangeId)
                        ? session.ArrangeSn.ToString(CultureInfo.InvariantCulture)
                        : session.ArrangeId,
                    ScheduleId = session.RuleSn,
                    Date = session.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    DayOfWeek = (int)session.DayOfWeek,
                    StartTime = session.StartTime,
                    EndTime = session.EndTime,
                    ClassDefId = session.ClassDefId,
                    ClassName = session.ClassName,
                    InstructorId = session.InstructorId,
                    InstructorName = session.InstructorName,
                    Duration = session.Duration,
                    Color = session.Color,
                    IsFree = session.IsFree,
                    Status = session.Status,
                    Source = session.Source
                }).ToList()
            });
        }

        
    }
}
