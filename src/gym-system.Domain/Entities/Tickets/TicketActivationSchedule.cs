namespace gym_system.Domain.Entities.Tickets
{
    public static class TicketActivationSchedule
    {
        public static DateOnly GetRenewalStartDate(DateOnly sourceEndDate, DateOnly paidDate) =>
            paidDate <= sourceEndDate ? sourceEndDate.AddDays(1) : paidDate;

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
