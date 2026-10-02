namespace gym_system.Application.MembersUseCase.Queries.GetStudentMemberList;

public sealed class GetStudentMemberListQuery
{
    public string? Keyword { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 15;
    public string[] ValidStates { get; init; } = [];
    public string[] PaymentStates { get; init; } = [];
}
