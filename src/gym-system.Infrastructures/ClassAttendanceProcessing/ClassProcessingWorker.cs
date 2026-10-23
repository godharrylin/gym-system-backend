using gym_system.Application.ClassAttendanceProcessing;
using Microsoft.Data.SqlClient;

namespace gym_system.Infrastructures.ClassAttendanceProcessing;

// The API supplies the hosted timer; each tick uses a fresh DI scope/SQL session.
public sealed class ClassProcessingWorker(ClassProcessingCoordinator coordinator)
{
    public Task<int> RunOnceAsync(CancellationToken ct) => coordinator.RunOnceAsync(ct);
}

internal sealed class SqlClassProcessingErrorPolicy : IClassProcessingErrorPolicy
{
    public bool IsTransient(Exception error)
        => error is SqlException sql && sql.Number is -2 or 1205 or 233 or 10053 or 10054 or 10060;
}
