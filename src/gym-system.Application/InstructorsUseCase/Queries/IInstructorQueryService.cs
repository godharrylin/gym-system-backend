
namespace gym_system.Application.InstructorsUseCase.Queries
{
    //  查詢老師列表
    public interface IInstructorQueryService
    {
        Task<IReadOnlyList<InstructorResult>> GetInstructorsAsync(CancellationToken ct);
    }
}
