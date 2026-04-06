namespace gym_system.Domain.Repositories
{
    public interface IUnitOfWork
    {
        Task BeginAsync(CancellationToken ct);
        Task CommitAsync(CancellationToken ct);
        Task RollbackAsync(CancellationToken ct);
    }
}
