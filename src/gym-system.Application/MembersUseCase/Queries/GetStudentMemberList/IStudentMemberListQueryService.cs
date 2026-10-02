namespace gym_system.Application.MembersUseCase.Queries.GetStudentMemberList;

public interface IStudentMemberListQueryService
{
    Task<StudentMemberListResult> QueryAsync(GetStudentMemberListQuery query, DateOnly today, CancellationToken ct);
}
