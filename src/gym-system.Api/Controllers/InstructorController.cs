using Microsoft.AspNetCore.Mvc;
using gym_system.Api.Contracts.Instructors;
using gym_system.Application.InstructorUseCase.Command.CreateInstructor;
using System.Net.WebSockets;

namespace gym_system.Api.Controllers
{
    [ApiController]
    [Route("api/v1/instructors")]
    public class InstructorController :ControllerBase
    {
        CreateInstructorHandler _createInstructorHandler;
        public InstructorController(CreateInstructorHandler createInstructorHandler) 
        {
            _createInstructorHandler = createInstructorHandler;
        }
        /// <summary>
        /// 取得老師列表
        /// </summary>
        /// <returns></returns>
        //[HttpGet]
        //public async Task<IActionResult> GetInstructors()
        //{

        //}

        [HttpPost]
        public async Task<IActionResult> CreateInstructor([FromBody]CreateInstructorRequest request, CancellationToken ct)
        {
            CreateInstructorCommand command = new CreateInstructorCommand
            {
                Name  = request.Name.Trim(),
                Phone = request.Phone.Trim(),
            };

            var result = await _createInstructorHandler.Handle(command, ct);
            return Ok(result);
        }
    }
}
