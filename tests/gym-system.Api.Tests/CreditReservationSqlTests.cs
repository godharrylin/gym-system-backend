using Dapper;
using gym_system.Application.ClassAttendanceProcessing;
using gym_system.Domain.ClassAttendanceProcessing;
using gym_system.Domain.Entities.Devices;
using gym_system.Domain.Repositories;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace gym_system.Api.Tests;

[Collection("Ticket SQL")]
public sealed class CreditReservationSqlTests
{
    [SqlServerFact]
    public async Task CleanupFailureRollsBackChildrenAndPreservesParentOwnershipForRecovery()
    {
        await using var f = new Fixture();
        await f.Initialize();
        await f.Reserve(f.Contexts[0]);
        var request = await f.AddGateEvent(1, f.Now);
        using var c = f.Tickets.Open();
        // An event not carrying this fixture's device marker must stop cleanup, not widen it.
        Assert.Equal(1, await c.ExecuteAsync("""
            UPDATE dbo.device_record SET device_id='UNEXPECTED_TEST_SOURCE'
            WHERE request_id=@request AND usr_id=@Student AND device_id=@Marker;
            """, new { request, f.Student, f.Tickets.Marker }));
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => f.DisposeAsync().AsTask());
            Assert.Equal(1, await c.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM dbo.users WHERE usr_id=@Student AND usr_name=@Marker", new { f.Student, f.Tickets.Marker }));
            Assert.Equal(1, await c.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM dbo.sdt_ticket_pass WHERE pass_sn=@pass AND owner_id=@Student", new { pass = f.Pass.Sn, f.Student }));
            Assert.Equal(1, await f.HoldCount());
            Assert.Equal(1, await c.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM dbo.sdt_att_record WHERE sdt_att_record_sn=@attendance AND user_id=@Student",
                new { attendance = f.Contexts[0].Attendance, f.Student }));
        }
        finally
        {
            // Restore only this exact test event so normal disposal can complete.
            await c.ExecuteAsync("""
                UPDATE dbo.device_record SET device_id=@Marker
                WHERE request_id=@request AND usr_id=@Student AND device_id='UNEXPECTED_TEST_SOURCE';
                """, new { request, f.Student, f.Tickets.Marker });
        }
    }

    [SqlServerFact]
    public async Task OneStudentsTechnicalFailureDoesNotRollbackOthersOrBecomeNoTicket()
    {
        await using var f = new Fixture();
        await f.InitializeAdmission();
        await f.AddGateEvent(1, f.Now);
        var brokenUser = await f.AddBrokenAttendanceCandidate();
        f.Tickets.Clock.Value = f.Now.AddMinutes(6);
        Assert.Equal(1, await f.RunCoordinator());
        Assert.Equal(CreditReservationState.Fixed, Assert.Single(await f.Holds()).State);
        using var c = f.Tickets.Open();
        var broken = await c.QuerySingleAsync<(string State, string Code)>("""
            SELECT processing_state,result_code FROM dbo.class_processing_item
            WHERE job_sn=@job AND user_id=@brokenUser;
            """, new { job = f.Contexts[0].Job, brokenUser });
        Assert.Equal(("NeedsReview", "CLASS_WORK_REVIEW_REQUIRED"), broken);
        Assert.Equal("Completed", await c.ExecuteScalarAsync<string>(
            "SELECT processing_state FROM dbo.class_processing_item WHERE item_sn=@item", new { item = f.Contexts[0].Item }));
        Assert.Equal("NeedsReview", await c.ExecuteScalarAsync<string>(
            "SELECT processing_state FROM dbo.class_processing_job WHERE job_sn=@job", new { job = f.Contexts[0].Job }));
        Assert.Equal(0, await f.RunCoordinator());
        Assert.Equal(1, await f.HoldCount());
    }

    [SqlServerFact]
    public async Task CoordinatorProcessesDueAttendanceOnceAndDoesNotPretendBCompleted()
    {
        await using var f = new Fixture();
        await f.InitializeAdmission();
        await f.AddGateEvent(1, f.Now);
        f.Tickets.Clock.Value = f.Now.AddMinutes(6);
        Assert.Equal(1, await f.RunCoordinator());
        Assert.Equal(CreditReservationState.Fixed, Assert.Single(await f.Holds()).State);
        Assert.Equal(1, (await f.Tickets.Pass(f.Pass.Sn)).Remaining);
        Assert.Equal(0, await f.RunCoordinator());
        Assert.Equal(1, await f.HoldCount());
        using var c = f.Tickets.Open();
        var phases = (await c.QueryAsync<(string Phase, string State)>("""
            SELECT phase,processing_state FROM dbo.class_processing_job
            WHERE cls_scdle_arnge_sn=@session ORDER BY phase;
            """, new { session = f.Contexts[0].Session })).ToArray();
        Assert.Equal(new[] { ("A", "Completed"), ("B", "Pending") }, phases);
    }

    [SqlServerFact]
    public async Task CoordinatorRecoversExpiredLeaseAndKeepsMissingHistoryUnsettled()
    {
        await using var f = new Fixture();
        await f.InitializeAdmission();
        f.Tickets.Clock.Value = f.Now.AddMinutes(6);
        await f.Tickets.Transaction(async sp =>
        {
            Assert.NotNull(await sp.GetRequiredService<IClassProcessingRepository>()
                .TryLeaseAsync(f.Contexts[0].Job, Guid.NewGuid(), f.Tickets.Marker, f.Now, default));
            return true;
        });
        f.Tickets.Clock.Value = f.Now.AddMinutes(2);
        Assert.Equal(1, await f.RunCoordinator());
        using var c = f.Tickets.Open();
        Assert.Equal("NeedsReview", await c.ExecuteScalarAsync<string>(
            "SELECT processing_state FROM dbo.class_processing_job WHERE job_sn=@job", new { job = f.Contexts[0].Job }));
        Assert.Equal("NeedsReview", await c.ExecuteScalarAsync<string>(
            "SELECT processing_state FROM dbo.class_processing_item WHERE item_sn=@item", new { item = f.Contexts[0].Item }));
        Assert.Equal(0, await f.RunCoordinator());
    }

    [SqlServerFact]
    public async Task GatePreparationStoresExactContextAndFailureReleasesOnlyNewHold()
    {
        await using var f = new Fixture();
        await f.InitializeAdmission();
        var request = await f.AddGateEvent(1, f.Now, "Requested");
        Assert.True((await f.Prepare(request)).Allowed);
        var context = await f.Tickets.Transaction(sp => sp.GetRequiredService<IClassAttendanceRepository>()
            .GetRequestContextAsync(request, f.Student, default));
        Assert.NotNull(context);
        Assert.Equal("RegularEntry", context.Kind);
        Assert.True(context.CreatedReservation);
        Assert.False(context.CreatedPending); // This fixture already owns a Pending attendance.
        await f.CompleteGate(request, "Failed");
        var hold = Assert.Single(await f.Holds());
        Assert.Equal(context.ReservationSn, hold.ReservationSn);
        Assert.Equal(CreditReservationState.Released, hold.State);
        Assert.Equal(1, (await f.Tickets.Pass(f.Pass.Sn)).Remaining);
    }

    [SqlServerFact]
    public async Task FailedSecondAttemptDoesNotReleaseHoldCreatedByFirstRequest()
    {
        await using var f = new Fixture();
        await f.InitializeAdmission();
        var first = await f.AddGateEvent(1, f.Now, "Requested");
        Assert.True((await f.Prepare(first)).Allowed);
        await f.CompleteGate(first, "Succeeded");
        var second = await f.AddGateEvent(1, f.Now.AddSeconds(1), "Requested");
        f.Tickets.Clock.Value = f.Now.AddSeconds(1);
        Assert.True((await f.Prepare(second)).Allowed);
        await f.CompleteGate(second, "Failed");
        Assert.Equal(CreditReservationState.Reserved, Assert.Single(await f.Holds()).State);
    }

    [SqlServerFact]
    public async Task UnknownGateCompletionKeepsReservationAndMarksAttendanceForReview()
    {
        await using var f = new Fixture();
        await f.InitializeAdmission();
        var request = await f.AddGateEvent(1, f.Now, "Requested");
        Assert.True((await f.Prepare(request)).Allowed);
        await f.CompleteGate(request, "Unknown");
        Assert.Equal(CreditReservationState.NeedsReview, Assert.Single(await f.Holds()).State);
        var row = await f.Tickets.Transaction(sp => sp.GetRequiredService<IClassAttendanceRepository>()
            .FindAttendanceAsync(f.Contexts[0].Attendance, default));
        Assert.Equal("NeedsReview", row!.Status);
    }

    [SqlServerFact]
    public async Task SimulatedGateSuccessDoesNotKeepNewCreditHold()
    {
        await using var f = new Fixture();
        await f.InitializeAdmission();
        var request = await f.AddGateEvent(1, f.Now, "Requested");
        Assert.True((await f.Prepare(request)).Allowed);
        await f.CompleteGate(request, "Succeeded", simulated: true);
        Assert.Equal(CreditReservationState.Released, Assert.Single(await f.Holds()).State);
    }

    [SqlServerFact]
    public async Task GateSuccessReceivedAfterCutoffPreservesActualTimeAndRequiresReview()
    {
        await using var f = new Fixture();
        await f.InitializeAdmission();
        var request = await f.AddGateEvent(1, f.Now, "Requested");
        Assert.True((await f.Prepare(request)).Allowed);
        f.Tickets.Clock.Value = f.Now.AddMinutes(5).AddMilliseconds(1);
        Assert.Equal("CLASS_ENTRY_LATE", (await f.CompleteGate(request, "Succeeded")).Code);
        Assert.Equal(CreditReservationState.NeedsReview, Assert.Single(await f.Holds()).State);
    }

    [SqlServerFact]
    public async Task SuccessfulEntryFixesPackAtCutoffWithoutDebitingAndReplayKeepsSameHold()
    {
        await using var f = new Fixture();
        await f.Initialize(-11, -30);
        await f.AddGateEvent(1, f.Now.AddMinutes(-1));
        var first = await f.Confirm();
        Assert.Equal("Completed", first.State);
        Assert.Equal("ATTENDANCE_CONFIRMED", first.Code);
        var rows = await f.Tickets.Transaction(sp => sp.GetRequiredService<ICreditReservationRepository>()
            .GetByAttendanceAsync(f.Contexts[0].Attendance, default));
        var hold = Assert.Single(rows);
        Assert.Equal(CreditReservationState.Fixed, hold.State);
        Assert.Equal(f.Pass.Sn, hold.PassSn);
        Assert.Equal(1, (await f.Tickets.Pass(f.Pass.Sn)).Remaining);
        Assert.Equal("ATTENDANCE_ALREADY_CONFIRMED", (await f.Confirm()).Code);
        Assert.Equal(1, await f.HoldCount());
    }

    [SqlServerFact]
    public async Task MonthlyAttendanceFreezesTicketWithoutCreditReservation()
    {
        await using var f = new Fixture();
        await f.Initialize(-11, -30, "M_PASS");
        await f.AddGateEvent(1, f.Now.AddMinutes(-2));
        Assert.Equal("ATTENDANCE_CONFIRMED", (await f.Confirm()).Code);
        Assert.Equal(0, await f.HoldCount());
        var row = await f.Tickets.Transaction(sp => sp.GetRequiredService<IClassAttendanceRepository>()
            .FindAttendanceAsync(f.Contexts[0].Attendance, default));
        Assert.True(row!.ConfirmedEligible);
        Assert.Equal(f.Pass.Id, row.PassId);
    }

    [SqlServerFact]
    public async Task ExitBeforeCutoffReleasesTemporaryHoldAndDoesNotConfirmAttendance()
    {
        await using var f = new Fixture();
        await f.Initialize(-11, -30);
        await f.Reserve(f.Contexts[0]);
        await f.AddGateEvent(1, f.Now.AddMinutes(-5));
        await f.AddGateEvent(2, f.Now.AddMinutes(-2));
        Assert.Equal("Skipped", (await f.Confirm()).State);
        var holds = await f.Tickets.Transaction(sp => sp.GetRequiredService<ICreditReservationRepository>()
            .GetByAttendanceAsync(f.Contexts[0].Attendance, default));
        Assert.Equal(CreditReservationState.Released, Assert.Single(holds).State);
        Assert.Equal(1, (await f.Tickets.Pass(f.Pass.Sn)).Remaining);
    }

    [SqlServerFact]
    public async Task UnknownGateOutcomeCannotCreateGuessedAttendanceOrReleaseHeldCredit()
    {
        await using var f = new Fixture();
        await f.Initialize(-11, -30);
        await f.Reserve(f.Contexts[0]);
        await f.AddGateEvent(1, f.Now.AddMinutes(-2), "Unknown");
        var result = await f.Confirm();
        Assert.Equal("NeedsReview", result.State);
        Assert.Equal("GATE_RESULT_UNRESOLVED", result.Code);
        var holds = await f.Tickets.Transaction(sp => sp.GetRequiredService<ICreditReservationRepository>()
            .GetByAttendanceAsync(f.Contexts[0].Attendance, default));
        Assert.Equal(CreditReservationState.NeedsReview, Assert.Single(holds).State);
    }

    [SqlServerFact]
    public async Task ConfirmationCannotSealExactlyAtInclusiveCutoff()
    {
        await using var f = new Fixture();
        await f.Initialize(-10);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Confirm());
        Assert.Equal(0, await f.HoldCount());
    }

    [SqlServerFact]
    public async Task MissingOriginalGateHistoryPreservesHoldForReviewWithoutGuessingAttendance()
    {
        await using var f = new Fixture();
        await f.Initialize(-11);
        await f.Reserve(f.Contexts[0]);
        var result = await f.Confirm();
        Assert.Equal("NeedsReview", result.State);
        Assert.Equal("GATE_HISTORY_INCOMPLETE", result.Code);
        var holds = await f.Tickets.Transaction(sp => sp.GetRequiredService<ICreditReservationRepository>()
            .GetByAttendanceAsync(f.Contexts[0].Attendance, default));
        Assert.Equal(CreditReservationState.NeedsReview, Assert.Single(holds).State);
        Assert.True(holds[0].HoldsCredit);
        Assert.Equal("NeedsReview", (await f.Confirm()).State);
    }

    [SqlServerFact]
    public async Task ConfirmedPackAttendanceWithoutFixedHoldCannotBeDeclaredComplete()
    {
        await using var f = new Fixture();
        await f.Initialize(-11);
        await f.Tickets.Transaction(async sp =>
        {
            var repo = sp.GetRequiredService<IClassAttendanceRepository>();
            var row = (await repo.FindAttendanceAsync(f.Contexts[0].Attendance, default))!;
            await repo.UpdateAttendanceAsync(row, row with { Status = "CheckIn", Eligibility = "Eligible", PassId = f.Pass.Id }, default);
            return true;
        });
        var result = await f.Confirm();
        Assert.Equal("NeedsReview", result.State);
        Assert.Equal("CONFIRMED_ATTENDANCE_INCONSISTENT", result.Code);
    }

    [SqlServerFact]
    public async Task ExpiredLeaseCanBeReclaimedAndOldTokenCannotWrite()
    {
        await using var f = new Fixture();
        await f.Initialize(-11);
        var id = f.Contexts[0].Job;
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        await f.Tickets.Transaction(async sp =>
        {
            var work = sp.GetRequiredService<IClassProcessingRepository>();
            Assert.NotNull(await work.TryLeaseAsync(id, first, f.Tickets.Marker, f.Now, default));
            Assert.Null(await work.TryLeaseAsync(id, second, f.Tickets.Marker, f.Now.AddSeconds(1), default));
            Assert.NotNull(await work.TryLeaseAsync(id, second, f.Tickets.Marker, f.Now.AddMinutes(2), default));
            return true;
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Tickets.Transaction(sp => sp
            .GetRequiredService<IClassProcessingRepository>().VerifyAndRenewLeaseAsync(id, first, f.Now.AddMinutes(2), default)));
    }

    [SqlServerFact]
    public async Task CompletedWorkItemCannotBeProcessedTwice()
    {
        await using var f = new Fixture();
        await f.Initialize(-11);
        var context = f.Contexts[0];
        var item = new ClassWorkItem(context.Item, context.Job, f.Student, context.Attendance, "Pending", 0);
        await f.Tickets.Transaction(async sp =>
        {
            await sp.GetRequiredService<IClassProcessingRepository>().CompleteItemAsync(item,
                "Completed", context.Attendance, "TEST_COMPLETED", "Test-owned work completed.", null, f.Now, default);
            return true;
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Tickets.Transaction(async sp =>
        {
            await sp.GetRequiredService<IClassProcessingRepository>().CompleteItemAsync(item,
                "Completed", context.Attendance, "TEST_COMPLETED", "Do not replay.", null, f.Now, default);
            return true;
        }));
    }

    [SqlServerFact]
    public async Task RepositoryRejectsCallsWithoutCallerTransaction()
    {
        await using var f = new Fixture();
        using var scope = f.Tickets.Provider.CreateScope();
        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.ServiceProvider
            .GetRequiredService<ICreditReservationRepository>().FindByKeyAsync("not-written", default));
    }

    [SqlServerFact]
    public async Task ReserveReplayAndFixRoundTripThroughSqlWithStoredTimePrecision()
    {
        await using var f = new Fixture();
        await f.Initialize();
        var first = await f.Reserve(f.Contexts[0]);
        var again = await f.Reserve(f.Contexts[0]);
        Assert.True(first.Created);
        Assert.False(again.Created);
        Assert.Equal(first.Reservation, again.Reservation);
        Assert.Equal(0, first.Reservation.CreatedAt.Ticks % TimeSpan.TicksPerMillisecond);
        var fixedHold = await f.Tickets.Transaction(sp => sp.GetRequiredService<CreditReservationService>()
            .FixAsync(f.Student, first.Reservation.ReservationSn!.Value, f.Now.AddMinutes(5)));
        Assert.Equal(CreditReservationState.Fixed, fixedHold.State);
        Assert.True(fixedHold.HoldsCredit);
    }

    [SqlServerFact]
    public async Task FailedCallerTransactionRollsBackInsertedHold()
    {
        await using var f = new Fixture();
        await f.Initialize();
        await Assert.ThrowsAsync<ForcedRollback>(() => f.Tickets.Transaction<int>(async sp =>
        {
            await f.ReserveIn(sp, f.Contexts[0]);
            throw new ForcedRollback();
        }));
        Assert.Equal(0, await f.HoldCount());
    }

    [SqlServerFact]
    public async Task StaleOriginalSnapshotCannotOverwriteCommittedFixedState()
    {
        await using var f = new Fixture();
        await f.Initialize();
        var original = (await f.Reserve(f.Contexts[0])).Reservation;
        await f.Tickets.Transaction(sp => sp.GetRequiredService<CreditReservationService>()
            .FixAsync(f.Student, original.ReservationSn!.Value, f.Now.AddMinutes(5)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Tickets.Transaction(sp =>
            sp.GetRequiredService<ICreditReservationRepository>().UpdateAsync(original,
                original.Release(f.Now.AddMinutes(6), "STALE", "Old request."), default)));
        var current = await f.Tickets.Transaction(sp => sp.GetRequiredService<ICreditReservationRepository>()
            .FindByIdAsync(original.ReservationSn!.Value, default));
        Assert.Equal(CreditReservationState.Fixed, current!.State);
    }

    [SqlServerFact]
    public async Task InvalidRawSqlDataIsRejectedWhenReadWithoutCheckConstraints()
    {
        await using var f = new Fixture();
        await f.Initialize();
        var row = (await f.Reserve(f.Contexts[0])).Reservation;
        using (var c = f.Tickets.Open())
            await c.ExecuteAsync("UPDATE dbo.sdt_ticket_credit_reservation SET reserved_credits=2 WHERE reservation_sn=@id",
                new { id = row.ReservationSn });
        await Assert.ThrowsAsync<ArgumentException>(() => f.Tickets.Transaction(sp =>
            sp.GetRequiredService<ICreditReservationRepository>().FindByIdAsync(row.ReservationSn!.Value, default)));
    }

    [SqlServerFact]
    public async Task UniqueIndexRejectsSecondLiveHoldEvenIfApplicationCapacityCheckIsBypassed()
    {
        await using var f = new Fixture();
        await f.Initialize();
        await f.Reserve(f.Contexts[0]);
        var error = await Assert.ThrowsAsync<SqlException>(() => f.Tickets.Transaction(sp =>
            sp.GetRequiredService<ICreditReservationRepository>().InsertAsync(
                CreditReservation.Reserve(f.Contexts[0].Attendance, f.Pass.Sn, f.Tickets.Marker + ":duplicate",
                    f.Now, processingItemSn: f.Contexts[0].Item), default)));
        Assert.Contains(error.Number, new[] { 2601, 2627 });
        Assert.Equal(1, await f.HoldCount());
    }

    [SqlServerFact]
    public async Task ConcurrentDifferentAttendancesCompeteForLastCreditWithoutOverReservation()
    {
        await using var f = new Fixture();
        await f.Initialize();
        var second = await f.AddContext(f.Now.AddHours(2));
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<bool> Attempt(Context context)
        {
            await start.Task;
            try { return (await f.Reserve(context)).Created; }
            catch (InvalidOperationException e) when (e.Message == "票券可占用堂數不足。") { return false; }
        }
        var a = Attempt(f.Contexts[0]);
        var b = Attempt(second);
        start.SetResult();
        var results = await Task.WhenAll(a, b);
        Assert.Single(results.Where(x => x));
        Assert.Equal(1, await f.HoldCount());
    }

    [SqlServerFact]
    public async Task ConcurrentSameOperationReturnsOneCreatedAndOneReplay()
    {
        await using var f = new Fixture();
        await f.Initialize();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<ReserveCreditResult> Attempt()
        { await start.Task; return await f.Reserve(f.Contexts[0]); }
        var a = Attempt();
        var b = Attempt();
        start.SetResult();
        var results = await Task.WhenAll(a, b);
        Assert.Single(results.Where(x => x.Created));
        Assert.Equal(results[0].Reservation.ReservationSn, results[1].Reservation.ReservationSn);
        Assert.Equal(1, await f.HoldCount());
    }

    [SqlServerFact]
    public async Task ReleasedHoldAllowsNewOperationAndReviewStillReservesCapacity()
    {
        await using var f = new Fixture();
        await f.Initialize();
        var context = f.Contexts[0];
        var first = (await f.Reserve(context)).Reservation;
        await f.Tickets.Transaction(sp => sp.GetRequiredService<CreditReservationService>()
            .ReleaseAsync(f.Student, first.ReservationSn!.Value, f.Now.AddMinutes(1), "EXIT", "Left before cutoff."));
        var returned = await f.Tickets.Transaction(sp => sp.GetRequiredService<CreditReservationService>()
            .ReserveAsync(f.Student, context.Attendance, f.Pass.Sn, f.Tickets.Marker + ":return", f.Now.AddMinutes(2),
                processingItemSn: context.Item, previousReservationSn: first.ReservationSn));
        await f.Tickets.Transaction(sp => sp.GetRequiredService<CreditReservationService>()
            .RequireReviewAsync(f.Student, returned.Reservation.ReservationSn!.Value,
                f.Now.AddMinutes(3), "UNKNOWN", "Awaiting evidence."));
        var another = await f.AddContext(f.Now.AddHours(2));
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Reserve(another));
        Assert.Equal(2, await f.HoldCount());
    }

    [SqlServerFact]
    public async Task ScheduleTimeChangeKeepsPendingAndHoldAndMovesBothJobDeadlines()
    {
        await using var f = new Fixture();
        await f.Initialize(startOffsetMinutes: 5);
        var original = (await f.Reserve(f.Contexts[0])).Reservation;
        var movedStart = f.Now.AddHours(2);
        await f.ChangeSchedule(movedStart, false);
        using var c = f.Tickets.Open();
        Assert.Equal("Pending", await c.ExecuteScalarAsync<string>(
            "SELECT sdt_att_record_status FROM dbo.sdt_att_record WHERE sdt_att_record_sn=@id",
            new { id = f.Contexts[0].Attendance }));
        var after = await f.Tickets.Transaction(sp => sp.GetRequiredService<ICreditReservationRepository>()
            .FindByIdAsync(original.ReservationSn!.Value, default));
        Assert.Equal(original, after);
        var deadlines = (await c.QueryAsync<DateTime>("""
            SELECT scheduled_at FROM dbo.class_processing_job WHERE cls_scdle_arnge_sn=@id ORDER BY phase;
            """, new { id = f.Contexts[0].Session })).ToArray();
        Assert.Equal(new[] { movedStart.AddMinutes(10), movedStart.AddHours(1).AddMinutes(10) }, deadlines);
    }

    [SqlServerFact]
    public async Task CancellingBeforeStartCancelsPendingReleasesHoldAndSkipsUnstartedWork()
    {
        await using var f = new Fixture();
        await f.Initialize(startOffsetMinutes: 5);
        var hold = (await f.Reserve(f.Contexts[0])).Reservation;
        await f.ChangeSchedule(f.Now.AddMinutes(5), true);
        using var c = f.Tickets.Open();
        Assert.Equal("Cancelled", await c.ExecuteScalarAsync<string>(
            "SELECT sdt_att_record_status FROM dbo.sdt_att_record WHERE sdt_att_record_sn=@id",
            new { id = f.Contexts[0].Attendance }));
        var after = await f.Tickets.Transaction(sp => sp.GetRequiredService<ICreditReservationRepository>()
            .FindByIdAsync(hold.ReservationSn!.Value, default));
        Assert.Equal(CreditReservationState.Released, after!.State);
        Assert.Equal("CLASS_CANCELLED", after.ReasonCode);
        Assert.Equal(2, await c.ExecuteScalarAsync<int>("""
            SELECT COUNT(*) FROM dbo.class_processing_job WHERE cls_scdle_arnge_sn=@id AND processing_state='Cancelled';
            """, new { id = f.Contexts[0].Session }));
        Assert.Equal("Skipped", await c.ExecuteScalarAsync<string>(
            "SELECT processing_state FROM dbo.class_processing_item WHERE item_sn=@id", new { id = f.Contexts[0].Item }));
    }

    [SqlServerFact]
    public async Task FailureAfterScheduleSyncRollsBackScheduleHoldAndAttendanceTogether()
    {
        await using var f = new Fixture();
        await f.Initialize(startOffsetMinutes: 5);
        var original = (await f.Reserve(f.Contexts[0])).Reservation;
        await Assert.ThrowsAsync<ForcedRollback>(() => f.ChangeSchedule(f.Now.AddMinutes(5), true, failAfterSync: true));
        using var c = f.Tickets.Open();
        Assert.Equal("Open", await c.ExecuteScalarAsync<string>(
            "SELECT cls_scdle_status FROM dbo.cls_scdle_arnge WHERE cls_scdle_arnge_sn=@id",
            new { id = f.Contexts[0].Session }));
        Assert.Equal("Pending", await c.ExecuteScalarAsync<string>(
            "SELECT sdt_att_record_status FROM dbo.sdt_att_record WHERE sdt_att_record_sn=@id",
            new { id = f.Contexts[0].Attendance }));
        var after = await f.Tickets.Transaction(sp => sp.GetRequiredService<ICreditReservationRepository>()
            .FindByIdAsync(original.ReservationSn!.Value, default));
        Assert.Equal(original, after);
    }

    [SqlServerFact]
    public async Task UnexpectedFixedHoldAbortsCancellationRatherThanFreeingFixedCredit()
    {
        await using var f = new Fixture();
        await f.Initialize(startOffsetMinutes: 5);
        var hold = (await f.Reserve(f.Contexts[0])).Reservation;
        await f.Tickets.Transaction(sp => sp.GetRequiredService<CreditReservationService>()
            .FixAsync(f.Student, hold.ReservationSn!.Value, f.Now.AddMinutes(1)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.ChangeSchedule(f.Now.AddMinutes(5), true));
        using var c = f.Tickets.Open();
        Assert.Equal("Open", await c.ExecuteScalarAsync<string>(
            "SELECT cls_scdle_status FROM dbo.cls_scdle_arnge WHERE cls_scdle_arnge_sn=@id",
            new { id = f.Contexts[0].Session }));
    }

    private sealed class ForcedRollback : Exception;
    private sealed record Context(int Session, int Attendance, long Job, long Item);

    private sealed class Fixture : IAsyncDisposable
    {
        public TicketSqlFixture Tickets { get; } = new();
        public DateTime Now => Tickets.Clock.Now();
        public string Student { get; private set; } = "";
        public SqlPassState Pass { get; private set; } = null!;
        public List<Context> Contexts { get; } = [];
        private readonly List<int> sessions = [];
        private readonly List<int> attendances = [];
        private readonly HashSet<string> ownedStudents = [];

        public async Task Initialize(int startOffsetMinutes = -5, int paidAtOffsetMinutes = 0, string type = "PACK")
        {
            Student = await Tickets.AddStudent();
            ownedStudents.Add(Student);
            var plan = await Tickets.AddPlan(type: type);
            Pass = await Tickets.SeedPass(Student, plan, "Active", Now.Date, Now.Date.AddDays(29),
                remaining: 1, paidAt: Now.AddMinutes(paidAtOffsetMinutes));
            await AddContext(Now.AddMinutes(startOffsetMinutes));
        }

        public async Task InitializeAdmission()
        {
            // Keep discovery/policy tests away from any existing development schedule.
            Tickets.Clock.Value = new(2099, 9, 6, 10, 0, 0, DateTimeKind.Unspecified);
            var settings = Tickets.Provider.GetRequiredService<ClassProcessingSettings>();
            settings.Enabled = true;
            settings.StartDate = Tickets.Clock.Today();
            await Initialize(-5, -30);
        }

        public async Task<string> AddBrokenAttendanceCandidate()
        {
            var user = await Tickets.AddStudent();
            ownedStudents.Add(user);
            using var c = Tickets.Open();
            // Deliberately invalid persisted state exercises per-student rollback/error isolation.
            var attendance = await c.ExecuteScalarAsync<int>("""
                INSERT dbo.sdt_att_record (user_id,cls_scdle_arnge_sn,sdt_att_record_status,
                    sdt_att_record_create_dt,sdt_att_record_upd_date)
                OUTPUT inserted.sdt_att_record_sn VALUES (@user,@session,'InvalidTestState',@now,@now);
                """, new { user, session = Contexts[0].Session, now = Now });
            attendances.Add(attendance);
            return user;
        }

        public Task<ClassPreparation> Prepare(Guid request)
            => Tickets.Transaction(sp => sp.GetRequiredService<IClassAdmissionService>()
                .PrepareEntryAsync(request, Student, default));

        public async Task<int> RunCoordinator()
        {
            using var scope = Tickets.Provider.CreateScope();
            var sp = scope.ServiceProvider;
            var coordinator = new ClassProcessingCoordinator(sp.GetRequiredService<IClassAttendanceRepository>(),
                sp.GetRequiredService<IClassProcessingRepository>(), sp.GetRequiredService<AttendanceConfirmationService>(),
                sp.GetRequiredService<IUnitOfWork>(), Tickets.Clock, sp.GetRequiredService<ClassProcessingSettings>(),
                sp.GetRequiredService<IClassProcessingErrorPolicy>(), NullLogger<ClassProcessingCoordinator>.Instance);
            return await coordinator.RunOnceAsync(default);
        }

        public Task<IReadOnlyList<CreditReservation>> Holds()
            => Tickets.Transaction(sp => sp.GetRequiredService<ICreditReservationRepository>()
                .GetByAttendanceAsync(Contexts[0].Attendance, default));

        public Task<ClassCompletion> CompleteGate(Guid request, string outcome, bool simulated = false)
            => Tickets.Transaction(async sp =>
            {
                var completion = await sp.GetRequiredService<IClassAdmissionService>().CompleteAsync(request,
                    Student, GateDirection.Entry, outcome, simulated, Now, default);
                Assert.True(await sp.GetRequiredService<IGatePassageRepository>().CompleteAsync(request, outcome,
                    completion.Code ?? "TEST_RESULT", outcome == "Succeeded", simulated,
                    outcome == "Succeeded" && !simulated, Now, null, default));
                return completion;
            });

        public Task<bool> ChangeSchedule(DateTime newStart, bool cancelled, bool failAfterSync = false)
            => Tickets.Transaction(async sp =>
            {
                var repository = sp.GetRequiredService<IScheduleSessionRepository>();
                var id = Contexts[0].Session.ToString(System.Globalization.CultureInfo.InvariantCulture);
                await repository.AcquireSessionMutationLockAsync(id, default);
                var old = (await repository.GetByIdAsync(id, default))!;
                old.EnsureEditable(Now.AddMinutes(2));
                var changed = gym_system.Domain.Entities.ScheduleSessions.ScheduleSession.Rehydrate(
                    old.ArrangeSn, old.ArrangeId, DateOnly.FromDateTime(newStart),
                    old.ClassId, old.ClassName, old.ClassLabelColor, old.InstructorId, old.InstructorName,
                    newStart, newStart.AddHours(1), cancelled
                        ? gym_system.Domain.Entities.ScheduleSessions.SessionStatus.Cancel
                        : gym_system.Domain.Entities.ScheduleSessions.SessionStatus.Open,
                    old.Source, old.RuleSn, old.IsFree);
                // Keep all schedule and dependent writes in the same scoped SQL session.
                await repository.UpdateAsync(changed, default);
                await sp.GetRequiredService<IScheduleAttendanceSynchronizer>()
                    .SynchronizeAsync(changed, Now.AddMinutes(2), Tickets.Marker, default);
                if (failAfterSync) throw new ForcedRollback();
                return true;
            });

        public async Task<Context> AddContext(DateTime start)
        {
            using var c = Tickets.Open();
            var session = await c.ExecuteScalarAsync<int>("""
                INSERT dbo.cls_scdle_arnge
                    (cls_scdle_date,class_name,cls_scdle_arnge_instructor_id,instructor_name,
                     cls_scdle_arnge_st,cls_scdle_arnge_et,cls_scdle_status,cls_scdle_arnge_is_free,
                     cls_scdle_source,cls_scdle_arnge_create_dt)
                OUTPUT inserted.cls_scdle_arnge_sn
                VALUES (@day,@Marker,@Student,@Marker,@start,@endAt,'Open',0,'Manual',@now);
                """, new { day = start.Date, Tickets.Marker, Student, start, endAt = start.AddHours(1), now = Now });
            sessions.Add(session);
            var attendance = await c.ExecuteScalarAsync<int>("""
                INSERT dbo.sdt_att_record (user_id,cls_scdle_arnge_sn,sdt_att_record_status,
                    sdt_att_record_create_dt,sdt_att_record_upd_date)
                OUTPUT inserted.sdt_att_record_sn VALUES (@Student,@session,'Pending',@now,@now);
                """, new { Student, session, now = Now });
            attendances.Add(attendance);
            var job = await c.ExecuteScalarAsync<long>("""
                INSERT dbo.class_processing_job
                    (cls_scdle_arnge_sn,phase,processing_state,class_start_at,class_end_at,instructor_id,
                     scheduled_at,created_at,updated_at)
                OUTPUT inserted.job_sn
                VALUES (@session,'A','Pending',@start,@endAt,@Student,@scheduled,@now,@now);
                """, new { session, start, endAt = start.AddHours(1), Student, scheduled = start.AddMinutes(10), now = Now });
            var item = await c.ExecuteScalarAsync<long>("""
                INSERT dbo.class_processing_item
                    (job_sn,user_id,attendance_sn,processing_state,created_at,updated_at)
                OUTPUT inserted.item_sn VALUES (@job,@Student,@attendance,'Pending',@now,@now);
                """, new { job, Student, attendance, now = Now });
            var context = new Context(session, attendance, job, item);
            Contexts.Add(context);
            return context;
        }

        public Task<ReserveCreditResult> Reserve(Context context)
            => Tickets.Transaction(sp => ReserveIn(sp, context));
        public async Task<Guid> AddGateEvent(int direction, DateTime response, string outcome = "Succeeded")
        {
            using var c = Tickets.Open();
            var request = Guid.NewGuid();
            await c.ExecuteAsync("""
                INSERT dbo.device_record
                    (usr_id,direction,method,device_id,is_success,device_record_dt,request_id,
                     identity_kind,role_snapshot,operation_kind,outcome,result_code,requested_at,
                     responded_at,affects_presence,is_simulated)
                VALUES (@Student,@direction,N'Phone',@Marker,@succeeded,@response,@request,
                    'Student',N'["Student"]','CustomerGate',@outcome,'TEST_EVENT',@requested,
                    @response,@succeeded,0);
                """, new { Student, direction, Tickets.Marker, response, outcome,
                    request, requested = response.AddSeconds(-1), succeeded = outcome == "Succeeded" });
            return request;
        }
        public Task<AttendanceConfirmationResult> Confirm()
            => Tickets.Transaction(async sp =>
            {
                var context = Contexts[0];
                var repository = sp.GetRequiredService<IClassAttendanceRepository>();
                await repository.LockCalendarAsync(default);
                var course = (await repository.LockSessionAsync(context.Session, default))!;
                return await sp.GetRequiredService<AttendanceConfirmationService>().ConfirmAsync(course,
                    new(context.Item, context.Job, Student, context.Attendance, "Pending", 0), default);
            });
        public Task<ReserveCreditResult> ReserveIn(IServiceProvider sp, Context context)
            => sp.GetRequiredService<CreditReservationService>().ReserveAsync(Student, context.Attendance,
                Pass.Sn, Tickets.Marker + ":" + context.Item, Now.AddTicks(1234), processingItemSn: context.Item);
        public async Task<int> HoldCount()
        {
            using var c = Tickets.Open();
            return await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.sdt_ticket_credit_reservation WHERE pass_sn=@id", new { id = Pass.Sn });
        }

        public async ValueTask DisposeAsync()
        {
                if (Student != "")
                {
                    using var c = Tickets.Open();
                    using var tx = c.BeginTransaction();
                    if (await c.ExecuteScalarAsync<string>("SELECT usr_name FROM dbo.users WHERE usr_id=@Student", new { Student }, tx) != Tickets.Marker)
                        throw new InvalidOperationException("Reservation cleanup ownership mismatch.");
                    foreach (var owner in ownedStudents)
                        if (await c.ExecuteScalarAsync<string>("SELECT usr_name FROM dbo.users WHERE usr_id=@owner", new { owner }, tx) != Tickets.Marker)
                            throw new InvalidOperationException("Additional test student ownership mismatch.");
                    foreach (var owner in ownedStudents)
                        await c.ExecuteAsync("DELETE dbo.device_record WHERE usr_id=@owner AND device_id=@Marker",
                            new { owner, Tickets.Marker }, tx);
                    foreach (var attendance in attendances)
                    {
                        foreach (var owner in ownedStudents)
                            await c.ExecuteAsync("""
                                DELETE r FROM dbo.sdt_ticket_credit_reservation r
                                INNER JOIN dbo.sdt_att_record a ON a.sdt_att_record_sn=r.attendance_sn
                                WHERE r.attendance_sn=@attendance AND a.user_id=@owner;
                                """, new { attendance, owner }, tx);
                    }
                    foreach (var session in sessions)
                    {
                        if (await c.ExecuteScalarAsync<int>("""
                            SELECT COUNT(*) FROM dbo.cls_scdle_arnge
                            WHERE cls_scdle_arnge_sn=@session AND class_name=@Marker
                              AND cls_scdle_arnge_instructor_id=@Student;
                            """, new { session, Student, Tickets.Marker }, tx) != 1)
                            throw new InvalidOperationException("Session cleanup ownership mismatch.");
                        var itemOwners = await c.QueryAsync<string>("""
                            SELECT i.user_id FROM dbo.class_processing_item i
                            INNER JOIN dbo.class_processing_job j ON j.job_sn=i.job_sn
                            WHERE j.cls_scdle_arnge_sn=@session;
                            """, new { session }, tx);
                        if (itemOwners.Any(owner => !ownedStudents.Contains(owner)))
                            throw new InvalidOperationException("Unexpected processing item owner during cleanup.");
                        foreach (var owner in ownedStudents)
                            await c.ExecuteAsync("""
                                DELETE i FROM dbo.class_processing_item i
                                INNER JOIN dbo.class_processing_job j ON j.job_sn=i.job_sn
                                INNER JOIN dbo.cls_scdle_arnge s ON s.cls_scdle_arnge_sn=j.cls_scdle_arnge_sn
                                WHERE j.cls_scdle_arnge_sn=@session AND i.user_id=@owner AND s.class_name=@Marker;
                                """, new { session, owner, Tickets.Marker }, tx);
                        await c.ExecuteAsync("""
                            DELETE j FROM dbo.class_processing_job j
                            INNER JOIN dbo.cls_scdle_arnge s ON s.cls_scdle_arnge_sn=j.cls_scdle_arnge_sn
                            WHERE j.cls_scdle_arnge_sn=@session AND s.class_name=@Marker;
                            """, new { session, Tickets.Marker }, tx);
                    }
                    foreach (var attendance in attendances)
                        foreach (var owner in ownedStudents)
                            await c.ExecuteAsync("DELETE dbo.sdt_att_record WHERE sdt_att_record_sn=@attendance AND user_id=@owner",
                                new { attendance, owner }, tx);
                    foreach (var session in sessions)
                        await c.ExecuteAsync("DELETE dbo.cls_scdle_arnge WHERE cls_scdle_arnge_sn=@session AND class_name=@Marker",
                            new { session, Tickets.Marker }, tx);
                    foreach (var owner in ownedStudents)
                        if (await c.ExecuteScalarAsync<int>("""
                        SELECT (SELECT COUNT(*) FROM dbo.sdt_att_record WHERE user_id=@owner)
                             + (SELECT COUNT(*) FROM dbo.cls_scdle_arnge WHERE class_name=@Marker)
                             + (SELECT COUNT(*) FROM dbo.sdt_ticket_credit_reservation WHERE pass_sn=@pass)
                             + (SELECT COUNT(*) FROM dbo.class_processing_item WHERE user_id=@owner)
                             + (SELECT COUNT(*) FROM dbo.device_record WHERE usr_id=@owner);
                        """, new { owner, Tickets.Marker, pass = Pass?.Sn ?? 0 }, tx) != 0)
                        throw new InvalidOperationException("Reservation test data remains.");
                    foreach (var session in sessions)
                        if (await c.ExecuteScalarAsync<int>(
                            "SELECT COUNT(*) FROM dbo.class_processing_job WHERE cls_scdle_arnge_sn=@session",
                            new { session }, tx) != 0)
                            throw new InvalidOperationException("Reservation processing jobs remain.");
                    tx.Commit();
                }
            // Do not delete parent fixtures if dependent cleanup/verification failed.
            // Keeping their ownership markers allows a later exact, recoverable cleanup.
            await Tickets.DisposeAsync();
        }
    }
}
