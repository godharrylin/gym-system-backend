namespace gym_system.Application.ClassesUseCase.Queries
{
    public sealed class GetClassesListHandler
    {
        private readonly IClassCatalogQueryService _queryService;

        public GetClassesListHandler(IClassCatalogQueryService queryService)
        {
            _queryService = queryService;
        }

        public Task<IReadOnlyList<ClassResult>> Handle(bool? includeInactive, CancellationToken ct)
            => _queryService.GetClassesAsync(includeInactive, ct);
    }
}
