namespace gym_system.Application.MembersUseCase.Queries.GetStudentMemberList;

public sealed class StudentMemberListItem
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Phone { get; init; } = "";
    public string? CurrentPassId { get; init; }
    public string? CurrentPlanName { get; init; }
    public string? ValidStatus { get; init; }
    public string? DisplayValidState { get; init; }
    // Taiwan calendar date, yyyy-MM-dd; deliberately not a timestamp.
    public string? ValidEndDate { get; init; }
    public string? PaymentState { get; init; }
}
