using gym_system.Domain.Entities.Tickets;
using gym_system.Domain.Repositories;

namespace gym_system.Application.TicketPlansUseCase.Queries
{
    public sealed class RenewalTicketPassEligibilityService
    {
        private const int RenewalGraceDays = 9;
        private readonly ITicketPassRepository _ticketPassRepository;

        public RenewalTicketPassEligibilityService(ITicketPassRepository ticketPassRepository)
        {
            _ticketPassRepository = ticketPassRepository;
        }

        public async Task<RenewalEligibilityResult?> FindEligibleSourceAsync(
            string studentId,
            string? targetFamilyCode,
            DateTime now,
            bool acquireLock,
            CancellationToken ct)
        {
            var evaluation = await EvaluateAsync(
                studentId,
                targetFamilyCode,
                now,
                acquireLock,
                ct);
            return evaluation.Eligibility;
        }

        public async Task<RenewalEligibilityEvaluation> EvaluateAsync(
            string studentId,
            string? targetFamilyCode,
            DateTime now,
            bool acquireLock,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(studentId)
                || string.IsNullOrWhiteSpace(targetFamilyCode))
            {
                return RenewalEligibilityEvaluation.Rejected(
                    "RENEWAL_FAMILY_MISMATCH",
                    "續約方案缺少有效的方案家族");
            }

            var familyCode = targetFamilyCode.Trim();
            var source = await _ticketPassRepository.FindLatestRenewalSourceAsync(
                studentId.Trim(),
                familyCode,
                acquireLock,
                ct);
            if (source is null
                || source.ValidStartDate is null)
            {
                return RenewalEligibilityEvaluation.Rejected(
                    "TICKET_PLAN_NOT_AVAILABLE",
                    "找不到可承接的同家族來源票券");
            }

            if (string.IsNullOrWhiteSpace(source.FamilyCode)
                || !source.FamilyCode.Equals(familyCode, StringComparison.OrdinalIgnoreCase))
            {
                return RenewalEligibilityEvaluation.Rejected(
                    "RENEWAL_FAMILY_MISMATCH",
                    "來源票券與續約方案不屬於相同家族");
            }

            var today = DateOnly.FromDateTime(now);
            if (source.ValidStartDate.Value > today)
            {
                return RenewalEligibilityEvaluation.Rejected(
                    "TICKET_PLAN_NOT_AVAILABLE",
                    "來源票券尚未生效");
            }

            if (source.HasNonCancelledRenewal)
            {
                return RenewalEligibilityEvaluation.Rejected(
                    "RENEWAL_SOURCE_ALREADY_USED",
                    "來源票券已經有尚未取消的續約票");
            }

            var effectiveEndDate = GetEffectiveEndDate(source);
            if (effectiveEndDate is null)
            {
                return RenewalEligibilityEvaluation.Rejected(
                    "TICKET_PLAN_NOT_AVAILABLE",
                    "來源票券缺少可判斷的結束日期");
            }

            if (today > effectiveEndDate.Value.AddDays(RenewalGraceDays))
            {
                return RenewalEligibilityEvaluation.Rejected(
                    "RENEWAL_WINDOW_EXPIRED",
                    "來源票券已超過續約期限");
            }

            var isCancellationRetry = source.HasCancelledRenewal;
            if (isCancellationRetry)
            {
                if (source.LastCancelledRenewalAt is null)
                {
                    return RenewalEligibilityEvaluation.Rejected(
                        "RENEWAL_RETRY_EXPIRED",
                        "取消續約票後缺少可判斷的取消時間");
                }

                var retryDeadline = DateOnly.FromDateTime(
                    source.LastCancelledRenewalAt.Value).AddDays(1);
                var originalRenewalDeadline = effectiveEndDate.Value.AddDays(RenewalGraceDays);
                if (retryDeadline > originalRenewalDeadline)
                {
                    retryDeadline = originalRenewalDeadline;
                }

                if (today > retryDeadline)
                {
                    return RenewalEligibilityEvaluation.Rejected(
                        "RENEWAL_RETRY_EXPIRED",
                        "取消續約票後已超過可重訂期限");
                }
            }
            else if (await _ticketPassRepository.HasQueuedPassAsync(studentId.Trim(), ct))
            {
                return RenewalEligibilityEvaluation.Rejected(
                    "RENEWAL_QUEUE_CONFLICT",
                    "已有其他排隊票券，無法購買續約票");
            }

            return RenewalEligibilityEvaluation.Allowed(new RenewalEligibilityResult
                {
                    SourcePassSn = source.PassSn,
                    SourceEffectiveEndDate = effectiveEndDate.Value,
                    IsCancellationRetry = isCancellationRetry,
                    IsEarlyPurchase = today <= effectiveEndDate.Value
                });
        }

        private static DateOnly? GetEffectiveEndDate(RenewalSourcePass source)
        {
            if (source.ValidStatus == TicketValidStatus.Depleted)
            {
                return source.EndedAt is null
                    ? null
                    : DateOnly.FromDateTime(source.EndedAt.Value);
            }

            return source.ValidEndDate;
        }
    }

    public sealed class RenewalEligibilityResult
    {
        public required int SourcePassSn { get; init; }
        public required DateOnly SourceEffectiveEndDate { get; init; }
        public required bool IsCancellationRetry { get; init; }
        public required bool IsEarlyPurchase { get; init; }
    }

    public sealed class RenewalEligibilityEvaluation
    {
        private RenewalEligibilityEvaluation(
            RenewalEligibilityResult? eligibility,
            string? failureCode,
            string? failureMessage)
        {
            Eligibility = eligibility;
            FailureCode = failureCode;
            FailureMessage = failureMessage;
        }

        public RenewalEligibilityResult? Eligibility { get; }
        public string? FailureCode { get; }
        public string? FailureMessage { get; }

        public static RenewalEligibilityEvaluation Allowed(RenewalEligibilityResult eligibility) =>
            new(eligibility, null, null);

        public static RenewalEligibilityEvaluation Rejected(string code, string message) =>
            new(null, code, message);
    }
}
