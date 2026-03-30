namespace gym_system.Domain.Repositories
{
    public interface IClock
    {
        DateTime Now();
        DateOnly Today();
    }
}
