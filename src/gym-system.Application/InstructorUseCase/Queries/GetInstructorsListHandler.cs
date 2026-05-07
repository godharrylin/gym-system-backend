
namespace gym_system.Application.InstructorUseCase.Queries
{
    public class GetInstructorsListHandler
    {
        private IInstructorQueryService _queryService;
        public GetInstructorsListHandler(IInstructorQueryService queryService)
        {
            _queryService = queryService;
        }

        public async Task<IReadOnlyList<InstrucotrResult>> Handle(CancellationToken ct)
        => await _queryService.GetInstructorsAsync(ct);

    }
}
