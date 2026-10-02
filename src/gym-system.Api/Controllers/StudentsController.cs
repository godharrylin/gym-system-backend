using gym_system.Api.Contracts;
using gym_system.Api.Contracts.Students;
using gym_system.Application.MembersUseCase.Commands.UpdateStudent;
using gym_system.Application.MembersUseCase.Queries.GetStudentMemberList;
using gym_system.Domain.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace gym_system.Api.Controllers;

[ApiController]
[Route("api/v1/students")]
[Authorize(Roles = "Admin")]
public sealed class StudentsController(GetStudentMemberListHandler list, UpdateStudentHandler update) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] GetStudentMemberListQuery query, CancellationToken ct)
    {
        try { return Ok(await list.Handle(query, ct)); }
        catch (ArgumentException ex) { return BadRequest(Error("INVALID_STUDENT_QUERY", ex.Message)); }
    }

    [HttpPatch("{studentId}")]
    public async Task<IActionResult> Update(string studentId, [FromBody] UpdateStudentRequest request, CancellationToken ct)
    {
        try
        {
            await update.Handle(new(studentId, request.Name, request.Phone), ct);
            return NoContent();
        }
        catch (ArgumentException ex) { return BadRequest(Error("INVALID_STUDENT_PROFILE", ex.Message)); }
        catch (KeyNotFoundException ex) { return NotFound(Error("STUDENT_NOT_FOUND", ex.Message)); }
        catch (MemberRegistrationRejectedException ex) { return Conflict(Error(ex.Code, ex.Message)); }
    }

    private ApiErrorResponse Error(string code, string message) => new()
    { Code = code, Message = message, TraceId = HttpContext.TraceIdentifier };
}
