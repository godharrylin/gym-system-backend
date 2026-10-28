using gym_system.Domain.Entities.Members;

namespace gym_system.Application.TicketsUseCase.Services;

public interface ICurrentTicketService
{
    /// <summary>
    /// Reconciles authoritative passes and synchronizes the profile projection.
    /// The caller owns the transaction; this service never begins or commits one.
    /// evaluatedAt is a Taiwan business time, not a client-provided timestamp.
    /// This is current-state reconciliation, not historical attendance replay.
    /// </summary>
    Task<CurrentTicketSnapshot?> ReconcileAsync(
        string studentId, DateTime evaluatedAt, string operatorId, CancellationToken ct = default);
}
