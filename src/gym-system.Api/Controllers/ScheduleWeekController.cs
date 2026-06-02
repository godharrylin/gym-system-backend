using System.Globalization;
using gym_system.Api.Contracts.ScheduleWeeks;
using gym_system.Application.ScheduleSessionsUseCase.GetOrEnsureScheduleWeek;
using Microsoft.AspNetCore.Mvc;

namespace gym_system.Api.Controllers
{
    [ApiController]
    [Route("api/v1/admin/schedule")]
    public sealed class ScheduleWeekController : ControllerBase
    {
        private readonly GetOrEnsureScheduleWeekHandler _getOrEnsureScheduleWeekHandler;

        public ScheduleWeekController(GetOrEnsureScheduleWeekHandler getOrEnsureScheduleWeekHandler)
        {
            _getOrEnsureScheduleWeekHandler = getOrEnsureScheduleWeekHandler;
        }

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

            return Ok(new GetScheduleWeekResponse
            {
                WeekStart = result.WeekStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                WeekEnd = result.WeekEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Source = result.Source,
                Created = result.Created,
                Sessions = result.Sessions.Select(session => new ScheduleWeekSessionDto
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
