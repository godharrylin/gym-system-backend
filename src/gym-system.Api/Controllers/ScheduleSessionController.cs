using gym_system.Api.Contracts.ScheduleRules;
using gym_system.Api.Contracts.ScheduleSessions;
using gym_system.Application.ScheduleSessionsUseCase.Command.GetOrEnsureScheduleWeek;
using gym_system.Application.ScheduleSessionsUseCase.Command.UpdateScheduleSession;
using Microsoft.AspNetCore.Mvc;
using System.Globalization;

namespace gym_system.Api.Controllers
{
    [ApiController]
    [Route("api/v1/admin/schedule-session")]
    public sealed class ScheduleSessionController : ControllerBase
    {
        private readonly GetOrEnsureScheduleWeekHandler _getOrEnsureScheduleWeekHandler;
        private readonly UpdateScheduleSessionHandler _updateScheduleSessionHandler;

        public ScheduleSessionController(GetOrEnsureScheduleWeekHandler getOrEnsureScheduleWeekHandler,
                                         UpdateScheduleSessionHandler updateScheduleSessionHandler)
        {
            _getOrEnsureScheduleWeekHandler = getOrEnsureScheduleWeekHandler;
            _updateScheduleSessionHandler = updateScheduleSessionHandler;
        }


        /// <summary>
        /// 更新某筆已存在的排課實例，partial update
        /// </summary>
        /// <param name="id"></param>
        /// <param name="req"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        [HttpPost("{id}")]
        public async Task<ActionResult<bool>> UpdateScheduleAsync([FromRoute] string id, [FromBody] ScheduleSessionRequest req, CancellationToken ct)
        {
            var cmd = new UpdateScheduleSessionCommand
            {
                SessionId = id.Trim(),
                Date = req.date?.Trim(),
                ClassId = req.classId?.Trim(),
                InstructorId = req.instructorId?.Trim(),
                StartTime = req.startTime,
                Status = req.status,
                IsFree = req.isFree
            };
            return Ok(await _updateScheduleSessionHandler.Handle(cmd, ct));
        }
        //}
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
