namespace gym_system.Application.CoursesUseCase.Queries
{
    public sealed class GetCoursesListHandler
    {
        private readonly ICourseCatalogQueryService _queryService;

        public GetCoursesListHandler(ICourseCatalogQueryService queryService)
        {
            _queryService = queryService;
        }

        public Task<IReadOnlyList<CourseResult>> Handle(bool? includeInactive, CancellationToken ct)
            => _queryService.GetClassesAsync(includeInactive, ct);
    }
}
