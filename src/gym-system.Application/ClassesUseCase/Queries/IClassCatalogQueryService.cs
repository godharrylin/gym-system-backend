namespace gym_system.Application.ClassesUseCase.Queries
{
    public interface IClassCatalogQueryService
    {
        Task<IReadOnlyList<ClassResult>> GetClassesAsync(bool? includeInactive, CancellationToken ct);
    }
}
