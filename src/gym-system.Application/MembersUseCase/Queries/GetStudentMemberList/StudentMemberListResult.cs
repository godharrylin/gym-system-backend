namespace gym_system.Application.MembersUseCase.Queries.GetStudentMemberList;

public sealed record StudentMemberListResult(
    IReadOnlyList<StudentMemberListItem> Items, int Page, int PageSize, int TotalCount);
