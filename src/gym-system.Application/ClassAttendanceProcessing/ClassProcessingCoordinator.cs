using gym_system.Domain.ClassAttendanceProcessing;
using gym_system.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace gym_system.Application.ClassAttendanceProcessing;

public sealed class ClassProcessingCoordinator(IClassAttendanceRepository attendance,
    IClassProcessingRepository work, AttendanceConfirmationService confirmation, ClassSettlementService settlement,
    IUnitOfWork unitOfWork, IClock clock, ClassProcessingSettings settings,
    IClassProcessingErrorPolicy errors, ILogger<ClassProcessingCoordinator> logger)
{
    private readonly string owner = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";

    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        if (!settings.Enabled) return 0;
        settings.Validate();
        IReadOnlyList<long> due;
        await unitOfWork.BeginAsync(ct);
        try
        {
            await attendance.LockCalendarAsync(ct);
            var now = clock.Now();
            // Explicit rollout date avoids inventing attendance for pre-rollout schedules.
            await work.DiscoverJobsAsync(settings.StartDate!.Value, now, ct);
            due = await work.GetDueJobsAsync(now, ct);
            await unitOfWork.CommitAsync(ct);
        }
        catch { await unitOfWork.RollbackAsync(CancellationToken.None); throw; }

        var processed = 0;
        foreach (var id in due)
        {
            ct.ThrowIfCancellationRequested();
            var token = Guid.NewGuid();
            ClassWorkJob? job;
            IReadOnlyList<ClassWorkItem> items;
            await unitOfWork.BeginAsync(ct);
            try
            {
                await attendance.LockCalendarAsync(ct);
                job = await work.TryLeaseAsync(id, token, owner, clock.Now(), ct);
                if (job is null) { await unitOfWork.CommitAsync(ct); continue; }
                var course = await attendance.LockSessionAsync(job.SessionSn, ct)
                    ?? throw new InvalidOperationException("Class work references a missing session.");
                if (course.StartAt != job.StartAt || course.EndAt != job.EndAt)
                    throw new InvalidOperationException("Class work snapshot differs from the actual session.");
                var candidates = await attendance.GetCandidatesAsync(job.SessionSn, DateOnly.FromDateTime(job.StartAt), ct,
                    includeGateHistory: job.Phase == "A");
                items = await work.EnsureItemsAsync(id, candidates, clock.Now(), ct);
                await unitOfWork.CommitAsync(ct);
            }
            catch { await unitOfWork.RollbackAsync(CancellationToken.None); throw; }

            foreach (var candidate in items.Where(x => x.State is not ("Completed" or "Skipped" or "NeedsReview")))
            {
                ClassWorkItem? item;
                // Persist the attempt before execution: restarts cannot reset retry counts, and
                // admission can distinguish this student's processing from an idle pending item.
                await unitOfWork.BeginAsync(ct);
                try
                {
                    await attendance.LockCalendarAsync(ct);
                    await work.VerifyAndRenewLeaseAsync(id, token, clock.Now(), ct);
                    if (candidate.Attempts >= 3 || clock.Now().Date != job.StartAt.Date)
                    {
                        await work.CompleteItemAsync(candidate, "NeedsReview", candidate.AttendanceSn,
                            clock.Now().Date != job.StartAt.Date ? "CROSS_DAY_WORK_REVIEW" : "CLASS_WORK_ATTEMPTS_EXHAUSTED",
                            "跨日未完成或自動嘗試已達上限，保留原資料交人工確認。", null, clock.Now(), ct);
                        await unitOfWork.CommitAsync(ct);
                        continue;
                    }
                    item = await work.BeginItemAsync(candidate, clock.Now(), ct);
                    await unitOfWork.CommitAsync(ct);
                }
                catch { await unitOfWork.RollbackAsync(CancellationToken.None); throw; }
                if (item is null) continue;
                await unitOfWork.BeginAsync(ct);
                try
                {
                    await attendance.LockCalendarAsync(ct);
                    var currentJob = await work.VerifyAndRenewLeaseAsync(id, token, clock.Now(), ct);
                    var course = await attendance.LockSessionAsync(currentJob.SessionSn, ct)
                        ?? throw new InvalidOperationException("Class session is missing.");
                    var result = currentJob.Phase == "B"
                        ? await settlement.SettleAsync(course, item, ct)
                        : await confirmation.ConfirmAsync(course, item, ct);
                    await work.CompleteItemAsync(item, result.State, result.AttendanceSn,
                        result.Code, result.Message, result.BlockedBy, clock.Now(), ct);
                    await unitOfWork.CommitAsync(ct);
                }
                catch (OperationCanceledException) { await unitOfWork.RollbackAsync(CancellationToken.None); throw; }
                catch (Exception error)
                {
                    await unitOfWork.RollbackAsync(CancellationToken.None);
                    logger.LogError(error, "Class work item failed. JobSn={JobSn} ItemSn={ItemSn}", id, item.ItemSn);
                    await unitOfWork.BeginAsync(ct);
                    try
                    {
                        await attendance.LockCalendarAsync(ct);
                        await work.VerifyAndRenewLeaseAsync(id, token, clock.Now(), ct);
                        var retry = errors.IsTransient(error) && item.Attempts < 3 && clock.Now().Date == job.StartAt.Date;
                        await work.CompleteItemAsync(item, retry ? "Failed" : "NeedsReview", item.AttendanceSn,
                            retry ? "CLASS_WORK_TRANSIENT_FAILURE" : "CLASS_WORK_REVIEW_REQUIRED",
                            retry ? "暫時性技術錯誤，保留未完成進度重試。" : "處理未完成，請核對資料與系統日誌；不視為票券不足。",
                            null, clock.Now(), ct);
                        await unitOfWork.CommitAsync(ct);
                    }
                    catch { await unitOfWork.RollbackAsync(CancellationToken.None); throw; }
                }
            }
            await unitOfWork.BeginAsync(ct);
            try
            {
                await attendance.LockCalendarAsync(ct);
                await work.FinishBatchAsync(id, token, clock.Now(), ct);
                await unitOfWork.CommitAsync(ct);
            }
            catch { await unitOfWork.RollbackAsync(CancellationToken.None); throw; }
            processed++;
        }
        return processed;
    }
}
