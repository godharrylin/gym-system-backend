using gym_system.Domain.Entities.Members;
using gym_system.Domain.Entities.Tickets;

namespace gym_system.Domain.Repositories
{
    public interface ITicketPassRepository
    {
        Task<IReadOnlyList<TicketPassPersistenceResult>> AddRangeAsync(
            IReadOnlyList<TicketPass> passes,
            OrderPersistenceResult orderPersistence,
            CancellationToken ct);

        Task<RenewalSourcePass?> FindLatestRenewalSourceAsync(
            string ownerId,
            string familyCode,
            bool acquireLock,
            CancellationToken ct);

        Task<bool> HasQueuedPassAsync(string ownerId, CancellationToken ct);

        Task LockOwnerAsync(string ownerId, CancellationToken ct);

        Task<CancelledTicketPassResult?> CancelQueuedRenewalAsync(
            string passId,
            DateTime cancelledAt,
            string operatorId,
            CancellationToken ct);

        Task<CurrentTicketSnapshot?> ReconcileCurrentAsync(
            string ownerId,
            DateOnly today,
            DateTime updatedAt,
            string operatorId,
            CancellationToken ct);
    }

    public sealed class TicketPassPersistenceResult
    {
        public required string ClientPassId { get; init; }
        public required string OwnerId { get; init; }
        public required int PassSn { get; init; }
        public required string PassId { get; init; }
    }

    public sealed class RenewalSourcePass
    {
        public required int PassSn { get; init; }
        public required string FamilyCode { get; init; }
        public required TicketValidStatus ValidStatus { get; init; }
        public DateOnly? ValidStartDate { get; init; }
        public DateOnly? ValidEndDate { get; init; }
        public DateTime? EndedAt { get; init; }
        public TicketEndReason? EndReason { get; init; }
        public bool HasNonCancelledRenewal { get; init; }
        public bool HasCancelledRenewal { get; init; }
        public DateTime? LastCancelledRenewalAt { get; init; }
    }

    public sealed class CancelledTicketPassResult
    {
        public required string PassId { get; init; }
        public required string OwnerId { get; init; }
        public required int SourcePassSn { get; init; }
    }
}
