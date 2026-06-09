using System.Globalization;
using gym_system.Api.Contracts.ScheduleSessions;
using gym_system.Application.ScheduleSessionsUseCase.GetOrEnsureScheduleWeek;
using Microsoft.AspNetCore.Mvc;

namespace gym_system.Api.Controllers
{
    [ApiController]
    [Route("api/v1/admin/schedule-session")]
    public sealed class ScheduleSessionController : ControllerBase
    {
        private readonly GetOrEnsureScheduleWeekHandler _getOrEnsureScheduleWeekHandler;

        public ScheduleSessionController(GetOrEnsureScheduleWeekHandler getOrEnsureScheduleWeekHandler)
        {
            _getOrEnsureScheduleWeekHandler = getOrEnsureScheduleWeekHandler;
        }



        [HttpPost("")]

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
