using gym_system.Domain.Entities.Courses;

namespace gym_system.Domain.Repositories
{
    public interface ICourseRepository
    {
        public Task<Course?> GetByIdAsync(string courseId, CancellationToken ct);
        public Task<bool> UpdateAsync(Course course, CancellationToken ct);
        public Task<bool> AddAsync(Course course, CancellationToken ct);
    }
}
