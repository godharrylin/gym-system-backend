namespace gym_system.Application.CoursesUseCase.Queries
{
    public interface ICourseCatalogQueryService
    {
        Task<IReadOnlyList<CourseResult>> GetClassesAsync(bool? includeInactive, CancellationToken ct);
    }
}
