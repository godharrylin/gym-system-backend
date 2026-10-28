namespace gym_system.Domain.ClassAttendanceProcessing;

public sealed record SettlementProgress(string State, string? Code, string? Message, bool Running);

public interface IClassProcessingRepository
{
    Task DiscoverJobsAsync(DateOnly startDate, DateTime now, CancellationToken ct);
    Task<IReadOnlyList<long>> GetDueJobsAsync(DateTime now, CancellationToken ct);
    Task<ClassWorkJob?> TryLeaseAsync(long jobSn, Guid token, string owner, DateTime now, CancellationToken ct);
    Task<ClassWorkJob> VerifyAndRenewLeaseAsync(long jobSn, Guid token, DateTime now, CancellationToken ct);
    Task<IReadOnlyList<ClassWorkItem>> EnsureItemsAsync(long jobSn, IReadOnlyList<string> users, DateTime now, CancellationToken ct);
    Task<ClassWorkItem?> BeginItemAsync(ClassWorkItem original, DateTime now, CancellationToken ct);
    Task<SettlementProgress?> GetSettlementProgressAsync(long itemSn, string userId, DateTime now, CancellationToken ct);
    Task CompleteItemAsync(ClassWorkItem original, string state, int? attendanceSn, string code,
        string message, long? blockedBy, DateTime now, CancellationToken ct);
    Task FinishBatchAsync(long jobSn, Guid token, DateTime now, CancellationToken ct);
}
