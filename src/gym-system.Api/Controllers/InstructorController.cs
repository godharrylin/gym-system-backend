using gym_system.Api.Contracts.Instructors;
using gym_system.Api.Contracts.TicketPlans;
using gym_system.Application.InstructorsUseCase.Command.CreateInstructor;
using gym_system.Application.InstructorsUseCase.Command.UpdateInstructor;
using gym_system.Application.InstructorsUseCase.Queries;
using Microsoft.AspNetCore.Mvc;

namespace gym_system.Api.Controllers
{
    [ApiController]
    [Route("api/v1/instructors")]
    public class InstructorController : ControllerBase
    {
        private readonly GetInstructorsListHandler _getInstructorsListHandler;
        private readonly CreateInstructorHandler _createInstructorHandler;
        private readonly UpdateInstructorHandler _updateInstructorHandler;

        public InstructorController(
            GetInstructorsListHandler getInstructorsListHandler,
            CreateInstructorHandler createInstructorHandler,
            UpdateInstructorHandler updateInstructorHandler)
        {
            _getInstructorsListHandler = getInstructorsListHandler;
            _createInstructorHandler = createInstructorHandler;
            _updateInstructorHandler = updateInstructorHandler;
        }

        /// <summary>
        /// 取得老師們列表資訊
        /// </summary>
        /// <param name="ct"></param>
        /// <returns></returns>
        [HttpGet]
        public async Task<IActionResult> GetInstructorsAsync(CancellationToken ct)
        {
            var result = await _getInstructorsListHandler.Handle(ct);

            var response = new GetInstructorsResponse
            {
                InstructorList = result.Select(x => new InstructorDto
                {
                    Id = x.usr_id,
                    Name = x.usr_name,
                    Phone = x.usr_phone,
                    isActived = x.user_role_is_active
                }).ToList()
            };
            return Ok(response);
        }

        /// <summary>
        /// 建立老師資訊
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> CreateInstructor([FromBody] CreateInstructorRequest request, CancellationToken ct)
        {
            var command = new CreateInstructorCommand
            {
                Name = request.Name.Trim(),
                Phone = request.Phone.Trim(),
            };

            var result = await _createInstructorHandler.Handle(command, ct);
            return Ok(result);
        }

        /// <summary>
        /// 修改老師資訊
        /// </summary>
        [HttpPost("{id}")]
        public async Task<IActionResult> UpdateInstructor([FromRoute] string id, [FromBody] UpdateInstructorRequest request, CancellationToken ct)
        {
            var command = new UpdateInstructorCommand
            {
                UserId = id,
                Name = request.Name,
                Phone = request.Phone,
                IsEmployed = request.IsEmployed
            };

            var result = await _updateInstructorHandler.Handle(command, ct);
            return Ok(result);
        }
    }
}

