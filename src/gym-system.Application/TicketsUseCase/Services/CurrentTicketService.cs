using gym_system.Domain.Entities.Members;
using gym_system.Domain.Repositories;

namespace gym_system.Application.TicketsUseCase.Services;

public sealed class CurrentTicketService(
    ITicketPassRepository passes, IStudentProfileRepository profiles) : ICurrentTicketService
{
    public async Task<CurrentTicketSnapshot?> ReconcileAsync(
        string studentId, DateTime evaluatedAt, string operatorId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(studentId)) throw new InvalidOperationException("學生 ID 必填");
        if (string.IsNullOrWhiteSpace(operatorId)) throw new InvalidOperationException("操作者必填");

        var ownerId = studentId.Trim();
        var snapshot = await passes.ReconcileCurrentAsync(
            ownerId, DateOnly.FromDateTime(evaluatedAt), evaluatedAt, operatorId, ct);
        var updated = snapshot is null
            ? await profiles.ClearCurrentTicketAsync(ownerId, evaluatedAt, ct)
            : await profiles.UpdateCurrentTicketAsync(ownerId, snapshot, ct);
        if (!updated) throw new StudentProfileSnapshotUpdateException();
        return snapshot;
    }
}
