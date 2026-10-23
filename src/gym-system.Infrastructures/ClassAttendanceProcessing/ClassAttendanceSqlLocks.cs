using Dapper;
using gym_system.Infrastructures.Connections;

namespace gym_system.Infrastructures.ClassAttendanceProcessing;

internal static class ClassAttendanceSqlLocks
{
    public static Task LockCalendarAsync(ISqlSession session, CancellationToken ct)
    {
        if (session.Transaction is null) throw new InvalidOperationException("Calendar coordination requires a transaction.");
        return session.Connection.ExecuteAsync(new CommandDefinition("""
            DECLARE @result int;
            EXEC @result=sys.sp_getapplock @Resource='gym:attendance-calendar',
                @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000;
            IF @result < 0 THROW 51040, N'無法取得課程出席協調鎖，請稍後重試。', 1;
            """, transaction: session.Transaction, cancellationToken: ct));
    }
}
