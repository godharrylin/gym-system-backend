
namespace gym_system.Application.InstructorsUseCase.Queries
{
    public class GetInstructorsListHandler
    {
        private IInstructorQueryService _queryService;
        public GetInstructorsListHandler(IInstructorQueryService queryService)
        {
            _queryService = queryService;
        }

        public async Task<IReadOnlyList<InstructorResult>> Handle(CancellationToken ct)
        => await _queryService.GetInstructorsAsync(ct);

    }
}
