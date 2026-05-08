using gym_system.Api.Contracts.Classes;
using gym_system.Application.ClassesUseCase.Queries;
using Microsoft.AspNetCore.Mvc;

namespace gym_system.Api.Controllers
{
    [ApiController]
    [Route("api/v1/classes")]
    public class ClassController : ControllerBase
    {
        private readonly GetClassesListHandler _getClassesListHandler;

        public ClassController(GetClassesListHandler getClassesListHandler)
        {
            _getClassesListHandler = getClassesListHandler;
        }

        [HttpGet]
        public async Task<ActionResult<GetClassesResponse>> GetClassesAsync(
            [FromQuery] bool? includeInactive,
            CancellationToken ct)
        {
            var result = await _getClassesListHandler.Handle(includeInactive, ct);
            var response = new GetClassesResponse
            {
                ClassInfoList = result.Select(x => new ClassDto
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
    }
}
