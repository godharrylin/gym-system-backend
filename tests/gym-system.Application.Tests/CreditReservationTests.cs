using gym_system.Domain.ClassAttendanceProcessing;
using Xunit;

namespace gym_system.Application.Tests;

public sealed class CreditReservationTests
{
    private static readonly DateTime Now = new(2026, 10, 17, 19, 5, 0);
    private static CreditReservation Reserved() => CreditReservation.Reserve(
        10, 20, "GATE:stable-request:10:20", Now, Guid.NewGuid());

    [Fact]
    public void ConsumptionStopsHoldingOnlyAfterFixedReservationGetsUsageRecord()
    {
        var reserved = Reserved();
        var fixedHold = reserved.Fix(Now.AddMinutes(5));
        var consumed = fixedHold.Consume(Now.AddHours(1), 30);
        Assert.True(reserved.HoldsCredit);
        Assert.True(fixedHold.HoldsCredit);
        Assert.False(consumed.HoldsCredit);
        Assert.Equal(30, consumed.UsageSn);
        Assert.Equal(fixedHold.FixedAt, consumed.FixedAt);
        Assert.Throws<InvalidOperationException>(() => reserved.Consume(Now.AddHours(1), 30));
        Assert.Throws<InvalidOperationException>(() => consumed.Consume(Now.AddHours(2), 31));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReviewPreservesCapacityAndOriginalStage(bool fixedFirst)
    {
        var original = fixedFirst ? Reserved().Fix(Now.AddMinutes(5)) : Reserved();
        var review = original.RequireReview(Now.AddMinutes(6), "UNKNOWN", "Needs evidence.");
        Assert.True(review.HoldsCredit);
        Assert.Equal(original.State, review.ReviewFromState);
        Assert.Equal(original.FixedAt, review.FixedAt);
        Assert.Throws<InvalidOperationException>(() => review.Consume(Now.AddMinutes(7), 30));
        var restored = review.RestoreReviewedState(Now.AddMinutes(8));
        Assert.Equal(original.State, restored.State);
        Assert.Null(restored.ReviewFromState);
        Assert.Equal(original.FixedAt, restored.FixedAt);
    }

    [Fact]
    public void ExitCannotReleaseFixedCreditEvenWhileAwaitingReview()
    {
        var fixedHold = Reserved().Fix(Now.AddMinutes(5));
        var review = fixedHold.RequireReview(Now.AddMinutes(6), "CONFLICT", "Ticket changed.");
        Assert.Throws<InvalidOperationException>(() => fixedHold.Release(Now.AddMinutes(7), "EXIT", "Left."));
        Assert.Throws<InvalidOperationException>(() => review.Release(Now.AddMinutes(7), "EXIT", "Left."));
    }

    [Fact]
    public void ReleasedHoldCannotBeResurrectedAndReturnCreatesSeparateHistory()
    {
        var released = Reserved().Release(Now.AddMinutes(1), "EXIT", "Left before cutoff.");
        Assert.False(released.HoldsCredit);
        Assert.Throws<InvalidOperationException>(() => released.Fix(Now.AddMinutes(2)));
        Assert.Throws<InvalidOperationException>(() => released.RequireReview(Now.AddMinutes(2), "UNKNOWN", "Late retry."));
        var returned = CreditReservation.Reserve(10, 20, "GATE:new-request:10:20",
            Now.AddMinutes(2), Guid.NewGuid(), previousReservationSn: 100);
        Assert.True(returned.HoldsCredit);
        Assert.Equal(100L, returned.PreviousReservationSn);
        Assert.NotEqual(released.IdempotencyKey, returned.IdempotencyKey);
    }

    [Fact]
    public void UnknownPreEntryOutcomeKeepsCreditUntilExplicitlyResolved()
    {
        var review = Reserved().RequireReview(Now.AddMinutes(1), "UNKNOWN", "No reliable response.");
        Assert.True(review.HoldsCredit);
        var failed = review.Release(Now.AddMinutes(2), "CONFIRMED_FAILURE", "Verified that door did not open.");
        Assert.False(failed.HoldsCredit);
        Assert.Null(failed.ReviewFromState);
    }

    [Fact]
    public void InvalidInputsAndBackdatedTransitionsAreRejectedWithoutChangingOriginal()
    {
        Assert.Throws<ArgumentException>(() => CreditReservation.Reserve(10, 20, "key", Now));
        Assert.Throws<ArgumentException>(() => CreditReservation.Reserve(10, 20, "key", Now, Guid.Empty));
        Assert.Throws<ArgumentException>(() => CreditReservation.Reserve(10, 20, "中文", Now, Guid.NewGuid()));
        var original = Reserved();
        Assert.Throws<ArgumentException>(() => original.Fix(Now.AddSeconds(-1)));
        Assert.Throws<ArgumentException>(() => original.Release(Now, "", ""));
        Assert.Equal(CreditReservationState.Reserved, original.State);
        Assert.Null(original.FixedAt);
    }

    [Theory]
    [InlineData("quantity")]
    [InlineData("state")]
    [InlineData("empty-key")]
    [InlineData("long-key")]
    [InlineData("source")]
    [InlineData("empty-request")]
    [InlineData("invalid-item")]
    [InlineData("previous-self")]
    [InlineData("review-missing-origin")]
    [InlineData("review-invalid-origin")]
    [InlineData("review-unexpected-origin")]
    [InlineData("fixed-missing-time")]
    [InlineData("reserved-unexpected-fixed-time")]
    [InlineData("review-fixed-missing-time")]
    [InlineData("review-reserved-unexpected-fixed-time")]
    [InlineData("released-missing-time")]
    [InlineData("unexpected-release-time")]
    [InlineData("consumed-missing-time")]
    [InlineData("consumed-missing-usage")]
    [InlineData("consumed-invalid-usage")]
    [InlineData("unexpected-usage")]
    [InlineData("unexpected-consume-time")]
    [InlineData("released-missing-reason")]
    [InlineData("review-missing-reason")]
    [InlineData("long-reason-code")]
    [InlineData("long-reason-message")]
    [InlineData("invalid-id")]
    [InlineData("old-update-time")]
    public void StoredDataViolatingFormerChecksIsRejected(string scenario)
    {
        var valid = Reserved().ToData() with { ReservationSn = 100 };
        var fixedData = Reserved().Fix(Now).ToData() with { ReservationSn = 100 };
        var consumed = Reserved().Fix(Now).Consume(Now, 30).ToData() with { ReservationSn = 100 };
        var released = Reserved().Release(Now, "EXIT", "Left.").ToData() with { ReservationSn = 100 };
        var reviewed = Reserved().RequireReview(Now, "UNKNOWN", "Pending evidence.").ToData()
            with { ReservationSn = 100 };
        var invalid = scenario switch
        {
            "quantity" => valid with { ReservedCredits = 2 },
            "state" => valid with { State = (CreditReservationState)99 },
            "empty-key" => valid with { IdempotencyKey = "  " },
            "long-key" => valid with { IdempotencyKey = new string('a', 161) },
            "source" => valid with { GateRequestId = null, ProcessingItemSn = null },
            "empty-request" => valid with { GateRequestId = Guid.Empty },
            "invalid-item" => valid with { ProcessingItemSn = 0 },
            "previous-self" => valid with { PreviousReservationSn = 100 },
            "review-missing-origin" => reviewed with { ReviewFromState = null },
            "review-invalid-origin" => reviewed with { ReviewFromState = CreditReservationState.Released },
            "review-unexpected-origin" => valid with { ReviewFromState = CreditReservationState.Reserved },
            "fixed-missing-time" => fixedData with { FixedAt = null },
            "reserved-unexpected-fixed-time" => valid with { FixedAt = Now },
            "review-fixed-missing-time" => reviewed with { ReviewFromState = CreditReservationState.Fixed },
            "review-reserved-unexpected-fixed-time" => reviewed with { FixedAt = Now },
            "released-missing-time" => released with { ReleasedAt = null },
            "unexpected-release-time" => valid with { ReleasedAt = Now },
            "consumed-missing-time" => consumed with { ConsumedAt = null },
            "consumed-missing-usage" => consumed with { UsageSn = null },
            "consumed-invalid-usage" => consumed with { UsageSn = 0 },
            "unexpected-usage" => valid with { UsageSn = 30 },
            "unexpected-consume-time" => valid with { ConsumedAt = Now },
            "released-missing-reason" => released with { ReasonMessage = " " },
            "review-missing-reason" => reviewed with { ReasonCode = null },
            "long-reason-code" => reviewed with { ReasonCode = new string('a', 61) },
            "long-reason-message" => reviewed with { ReasonMessage = new string('a', 1001) },
            "invalid-id" => valid with { ReservationSn = 0 },
            "old-update-time" => valid with { UpdatedAt = Now.AddMilliseconds(-1) },
            _ => throw new ArgumentException("Unknown test case.", nameof(scenario))
        };
        Assert.Throws<ArgumentException>(() => invalid.Validate());
        Assert.Throws<ArgumentException>(() => CreditReservation.Rehydrate(invalid));
    }

    [Theory]
    [InlineData(CreditReservationState.Reserved)]
    [InlineData(CreditReservationState.Fixed)]
    [InlineData(CreditReservationState.Released)]
    [InlineData(CreditReservationState.Consumed)]
    [InlineData(CreditReservationState.NeedsReview)]
    public void ValidStoredStateCanBeRehydratedWithoutLosingFields(CreditReservationState state)
    {
        var model = state switch
        {
            CreditReservationState.Reserved => Reserved(),
            CreditReservationState.Fixed => Reserved().Fix(Now),
            CreditReservationState.Released => Reserved().Release(Now, "EXIT", "Left."),
            CreditReservationState.Consumed => Reserved().Fix(Now).Consume(Now, 30),
            CreditReservationState.NeedsReview => Reserved().Fix(Now).RequireReview(Now, "CONFLICT", "Review needed."),
            _ => throw new ArgumentOutOfRangeException(nameof(state))
        };
        var data = model.ToData() with { ReservationSn = 100 };
        Assert.Equal(data, CreditReservation.Rehydrate(data).ToData());
    }

    [Fact]
    public void RehydrationRequiresAssignedIdAndBackgroundSourceIsAccepted()
    {
        var pending = CreditReservation.Reserve(10, 20, "A:500:20", Now, processingItemSn: 500);
        Assert.Throws<ArgumentException>(() => CreditReservation.Rehydrate(pending.ToData()));
        var data = pending.ToData() with { ReservationSn = 101 };
        Assert.Equal(500L, CreditReservation.Rehydrate(data).ProcessingItemSn);
    }
}
