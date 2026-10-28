using gym_system.Domain.Enums;

namespace gym_system.Domain.Entities.Tickets
{
    public static class TicketActivationSchedule
    {
        public static DateOnly GetRenewalStartDate(DateOnly sourceEndDate, DateOnly paidDate) =>
            GetRenewalStartDate(sourceEndDate, paidDate, TicketEndReason.Expire);

        public static DateOnly GetRenewalStartDate(
            DateOnly sourceEndDate, DateOnly paidDate, TicketEndReason endReason)
        {
            if (endReason is not TicketEndReason.Depleted and not TicketEndReason.Expire)
                throw new InvalidOperationException("只有耗盡或到期的來源票可以接續");

            var earliestDate = endReason == TicketEndReason.Depleted
                ? sourceEndDate
                : sourceEndDate.AddDays(1);
            return paidDate > earliestDate ? paidDate : earliestDate;
        }

        public static DateOnly GetEndDate(DateOnly startDate, int expireDays)
        {
            if (expireDays <= 0)
            {
                throw new InvalidOperationException("票券有效天數必須大於零");
            }

            return startDate.AddDays(expireDays - 1);
        }
    }
}
