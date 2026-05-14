using gym_system.Api.Contracts.Courses;
using gym_system.Application.CoursesUseCase.Queries;
using gym_system.Application.CoursesUseCase.Commands;
using Microsoft.AspNetCore.Mvc;

namespace gym_system.Api.Controllers
{
    [ApiController]
    [Route("api/v1/classes")]
    public class CourseController : ControllerBase
    {
        private readonly GetCoursesListHandler _getCoursesListHandler;
        private readonly UpdateCourseHandler _updateCourseHandler;
        private readonly CreateCourseHandler _createCourseHandler;

        public CourseController(GetCoursesListHandler getCoursesListHandler,
            UpdateCourseHandler updateCourseHandler, CreateCourseHandler createCourseHandler)
        {
            _getCoursesListHandler = getCoursesListHandler;
            _updateCourseHandler = updateCourseHandler;
            _createCourseHandler = createCourseHandler;
        }

        /// <summary>
        /// 取得課程資訊列表
        /// </summary>
        /// <param name="includeInactive"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        [HttpGet]
        public async Task<ActionResult<GetCourseResponse>> GetCoursesAsync(
            [FromQuery] bool? includeInactive,
            CancellationToken ct)
        {
            var result = await _getCoursesListHandler.Handle(includeInactive, ct);
            var response = new GetCourseResponse
            {
                CoursesInfoList = result.Select(x => new CourseDto
                {
                    Id = x.class_sn.ToString(),
                    Name = x.class_name,
                    Instructor = x.usr_name,
                    Color = x.class_label_color,
                    Duration = x.class_duration.ToString(),
                    IsFree = x.class_is_free,
                    IsActive = x.class_is_active
                }).ToList()
            };

            return Ok(response);
        }

        /// <summary>
        /// 更新預設課程資訊
        /// </summary>
        /// <param name="id">預設課程 ID</param>
        /// <param name="request"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        [HttpPost("{id}")]
        public async Task<IActionResult> UpdateCourseAsync([FromRoute] string id, [FromBody] UpdateCourseRequest request, 
            CancellationToken ct)
        {
            var command = new UpdateCourseCommand
            {
                Id = id,
                Name = request.Name,
                InstructorId = request.InstructorId,
                Duration = request.Duration,
                IsFree = request.IsFree,
                LabelColor = request.Color
            };
            var result = await _updateCourseHandler.Handle(id, command, ct);
            return Ok(result);
        }

        /// <summary>
        /// 新增預設課程
        /// </summary>
        /// <param name="request"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        [HttpPost]
        public async Task<ActionResult<bool>> CreateCourseAsync([FromBody] CreateCourseRequest request, CancellationToken ct)
        {
            var command = new CreateCourseCommand
            {
                Name = request.Name,
                InstructorId = request.InstructorId,
                Duration = request.Duration,
                LabelColor = request.Color,
                IsFree = request.IsFree ?? false,
                Type = request.Type ?? ""
            };

            var result = await _createCourseHandler.Handle(command, ct);
            return Ok(result);
        }
    }
}
