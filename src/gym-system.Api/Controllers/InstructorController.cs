using gym_system.Api.Contracts.Instructors;
using gym_system.Application.InstructorUseCase.Command.CreateInstructor;
using gym_system.Application.InstructorUseCase.Command.UpdateInstructor;
using Microsoft.AspNetCore.Mvc;

namespace gym_system.Api.Controllers
{
    [ApiController]
    [Route("api/v1/instructors")]
    public class InstructorController : ControllerBase
    {
        private readonly CreateInstructorHandler _createInstructorHandler;
        private readonly UpdateInstructorHandler _updateInstructorHandler;

        public InstructorController(
            CreateInstructorHandler createInstructorHandler,
            UpdateInstructorHandler updateInstructorHandler)
        {
            _createInstructorHandler = createInstructorHandler;
            _updateInstructorHandler = updateInstructorHandler;
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
