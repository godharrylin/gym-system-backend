
namespace gym_system.Application.InstructorUseCase.Queries
{
    //  查詢老師列表
    public interface IInstructorQueryService
    {
        Task<IReadOnlyList<InstrucotrResult>> GetInstructorsAsync(CancellationToken ct);
    }
}
